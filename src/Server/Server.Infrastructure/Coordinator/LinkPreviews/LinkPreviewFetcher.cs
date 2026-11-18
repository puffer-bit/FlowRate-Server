using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Server.Application.Abstractions.LinkPreviews;
using Shared.Validation;

namespace Server.Infrastructure.Coordinator.LinkPreviews;

/// <summary>
/// Fetches pages for previews from the public internet only. Each connection resolves the host itself
/// and connects to an address it has checked, so neither a host that resolves to a private address, nor
/// one that changes its answer between a check and the connection, nor a redirect reaches the server's
/// own networks. Every step has a time and size limit.
/// </summary>
public sealed class LinkPreviewFetcher : ILinkPreviewFetcher, IDisposable
{
    public const int MaxPageBytes = 512 * 1024;
    public const int MaxImageBytes = 5 * 1024 * 1024;
    public const int MaxRedirects = 3;

    // Smaller pictures are icons and tracking pixels rather than pictures of the page
    private const int MinImageSide = 50;
    private const int MinLargeImageWidth = 400;
    private const double MinLargeImageAspect = 1.3;

    private static readonly TimeSpan PageTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan ImageTimeout = TimeSpan.FromSeconds(8);

    private const string UserAgent = "Mozilla/5.0 (compatible; FlowRateBot/1.0; link preview)";

    private readonly ILogger _logger;
    private readonly HttpClient _client;

    public LinkPreviewFetcher(ILogger logger, HttpMessageHandler? handler = null)
    {
        _logger = logger;
        _client = new HttpClient(handler ?? CreateHandler(), disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
            MaxResponseContentBufferSize = MaxImageBytes
        };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ru-RU,ru;q=0.9,en;q=0.8");
    }

    /// <summary>
    /// The handler every fetch goes through: no proxy, no redirects of its own and a connection only to
    /// public addresses on the allowed ports.
    /// </summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        MaxResponseHeadersLength = 64,
        ConnectCallback = ConnectToPublicAddressAsync
    };

    private static async ValueTask<Stream> ConnectToPublicAddressAsync(SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var endpoint = context.DnsEndPoint;
        if (!PublicAddresses.IsAllowedPort(endpoint.Port))
            throw new LinkPreviewRefusedException();

        var addresses = IPAddress.TryParse(endpoint.Host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(endpoint.Host, cancellationToken);

        // A host with any private address is refused as a whole: it is either misconfigured or a trap
        if (addresses.Length == 0 || !addresses.All(PublicAddresses.IsPublic))
            throw new LinkPreviewRefusedException();

        Exception? last = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(address, endpoint.Port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception exception) when (exception is SocketException)
            {
                socket.Dispose();
                last = exception;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw last ?? new LinkPreviewRefusedException();
    }

    public async Task<FetchedLinkPreview?> FetchAsync(Uri url, CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(PageTimeout);

            using var response = await GetAsync(url, "text/html,application/xhtml+xml;q=0.9,*/*;q=0.1", timeout.Token);
            if (response == null || response.Content.Headers.ContentType?.MediaType is not ("text/html" or "application/xhtml+xml"))
                return null;

            var bytes = await ReadLimitedAsync(response.Content, MaxPageBytes, truncate: true, timeout.Token);
            if (bytes == null)
                return null;

            var encoding = PageMetadata.EncodingOf(bytes, response.Content.Headers.ContentType?.CharSet);
            var page = PageMetadata.Parse(encoding.GetString(bytes));
            // Relative images are of the page the redirects ended at
            var pageUrl = response.RequestMessage?.RequestUri ?? url;

            var title = PageMetadata.Cut(page.Title, Limits.LinkPreviewTitleMaxLength);
            var description = PageMetadata.Cut(page.Description, Limits.LinkPreviewDescriptionMaxLength);
            if (title == null && description == null)
                return null;

            var siteName = PageMetadata.Cut(page.SiteName ?? SiteNameOf(pageUrl), Limits.LinkPreviewSiteNameMaxLength);
            var image = page.ImageUrl != null && Uri.TryCreate(pageUrl, page.ImageUrl, out var imageUrl)
                ? await FetchImageAsync(imageUrl, cancellationToken)
                : null;

            return new FetchedLinkPreview(siteName, title, description, image, image != null && IsLarge(page, image));
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Nothing of the reason goes further: whether an address answered is not for the user to learn
            _logger.LogDebug(exception, "Fetching a link preview failed.");
            return null;
        }
    }

    private async Task<FetchedLinkImage?> FetchImageAsync(Uri url, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ImageTimeout);

            using var response = await GetAsync(url, "image/webp,image/png,image/jpeg,image/gif;q=0.9,*/*;q=0.1", timeout.Token);
            if (response == null)
                return null;

            var bytes = await ReadLimitedAsync(response.Content, MaxImageBytes, truncate: false, timeout.Token);
            if (bytes == null || !ImageHeader.TryRead(bytes, out var mimeType, out var width, out var height)
                || width < MinImageSide || height < MinImageSide)
                return null;

            return new FetchedLinkImage(bytes, mimeType, width, height);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(exception, "Fetching the image of a link preview failed.");
            return null;
        }
    }

    /// <summary>
    /// GET with redirects followed here, so that every address on the way is checked like the first.
    /// </summary>
    /// <returns>A successful response, or null.</returns>
    private async Task<HttpResponseMessage?> GetAsync(Uri url, string accept, CancellationToken cancellationToken)
    {
        for (var redirects = 0; ; redirects++)
        {
            if (!IsFetchable(url))
                return null;

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd(accept);
            var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                response.Dispose();
                if (redirects >= MaxRedirects || !Uri.TryCreate(url, location, out var next))
                    return null;
                url = next;
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                response.Dispose();
                return null;
            }

            return response;
        }
    }

    private static bool IsFetchable(Uri url) =>
        url.IsAbsoluteUri
        && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps)
        && url.UserInfo.Length == 0
        && PublicAddresses.IsAllowedPort(url.Port)
        && url.AbsoluteUri.Length <= Limits.UrlMaxLength;

    /// <param name="truncate">
    /// Whether the start of a longer body is enough. A page is read only for its head; an image must be whole.
    /// </param>
    private static async Task<byte[]?> ReadLimitedAsync(HttpContent content, int maxBytes, bool truncate,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > maxBytes && !truncate)
            return null;

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[Math.Clamp((int?)content.Headers.ContentLength ?? 64 * 1024, 4096, maxBytes)];
        var length = 0;
        while (true)
        {
            if (length == buffer.Length)
            {
                if (buffer.Length >= maxBytes)
                {
                    // One more byte tells a body of exactly the limit from a longer one
                    if (!truncate && await stream.ReadAsync(new byte[1], cancellationToken) > 0)
                        return null;
                    break;
                }
                Array.Resize(ref buffer, (int)Math.Min(maxBytes, buffer.Length * 2L));
            }

            var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken);
            if (read == 0)
                break;
            length += read;
        }

        return length == 0 ? null : buffer[..length];
    }

    private static bool IsLarge(PageMetadata page, FetchedLinkImage image) => page.Card switch
    {
        "summary_large_image" => true,
        "summary" => false,
        _ => image.Width >= MinLargeImageWidth && image.Width >= image.Height * MinLargeImageAspect
    };

    private static string SiteNameOf(Uri url) =>
        url.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? url.Host[4..] : url.Host;

    public void Dispose() => _client.Dispose();
}

/// <summary>The address of a page or of a redirect is not a public one, or not on an allowed port.</summary>
public sealed class LinkPreviewRefusedException() : IOException("The address is not allowed for link previews.");
