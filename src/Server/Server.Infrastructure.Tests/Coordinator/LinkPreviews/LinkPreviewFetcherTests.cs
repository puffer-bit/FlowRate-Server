using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Infrastructure.Coordinator.LinkPreviews;
using Xunit;

namespace Server.Infrastructure.Tests.Coordinator.LinkPreviews;

public class LinkPreviewFetcherTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48,
        0x44, 0x52, 0, 0, 0x04, 0xB0, 0, 0, 0x02, 0x76, 8, 6, 0, 0, 0];

    private sealed class FakeWeb(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<Uri> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requested.Add(request.RequestUri!);
            var response = answer(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private static HttpResponseMessage Html(string html) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(html, Encoding.UTF8, "text/html")
    };

    private static HttpResponseMessage Redirect(string location) => new(HttpStatusCode.Found)
    {
        Headers = { Location = new Uri(location, UriKind.RelativeOrAbsolute) }
    };

    [Fact]
    public async Task APage_GivesItsPreview_WithARelativeImage()
    {
        var web = new FakeWeb(request => request.RequestUri!.AbsolutePath switch
        {
            "/a" => Html("""<meta property="og:title" content="Title"><meta property="og:image" content="/i.png">"""),
            "/i.png" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png) },
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        using var fetcher = new LinkPreviewFetcher(NullLogger.Instance, web);

        var preview = await fetcher.FetchAsync(new Uri("https://www.example.com/a"));

        Assert.NotNull(preview);
        Assert.Equal("Title", preview.Title);
        Assert.Equal("example.com", preview.SiteName);
        Assert.Equal(("image/png", 1200, 630), (preview.Image?.MimeType, preview.Image?.Width, preview.Image?.Height));
        Assert.True(preview.IsLargeImage);
        Assert.Equal(new Uri("https://www.example.com/i.png"), web.Requested[1]);
    }

    [Fact]
    public async Task ARedirectToAnotherPort_IsNotFollowed()
    {
        var web = new FakeWeb(request => request.RequestUri!.Port == 443
            ? Redirect("https://example.com:8443/")
            : Html("<title>inside</title>"));
        using var fetcher = new LinkPreviewFetcher(NullLogger.Instance, web);

        Assert.Null(await fetcher.FetchAsync(new Uri("https://example.com/")));
        Assert.Single(web.Requested);
    }

    [Fact]
    public async Task RedirectsEndAfterALimit()
    {
        var web = new FakeWeb(request => Redirect(request.RequestUri!.AbsolutePath + "x"));
        using var fetcher = new LinkPreviewFetcher(NullLogger.Instance, web);

        Assert.Null(await fetcher.FetchAsync(new Uri("https://example.com/")));
        Assert.Equal(LinkPreviewFetcher.MaxRedirects + 1, web.Requested.Count);
    }

    [Fact]
    public async Task SomethingThatIsNotAPage_HasNoPreview()
    {
        var web = new FakeWeb(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Png) { Headers = { ContentType = new("image/png") } }
        });
        using var fetcher = new LinkPreviewFetcher(NullLogger.Instance, web);

        Assert.Null(await fetcher.FetchAsync(new Uri("https://example.com/i.png")));
    }

    [Fact]
    public async Task AnImageThatIsNotOne_IsLeftOut()
    {
        var web = new FakeWeb(request => request.RequestUri!.AbsolutePath == "/"
            ? Html("""<meta property="og:title" content="T"><meta property="og:image" content="/x.svg">""")
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<svg/>") });
        using var fetcher = new LinkPreviewFetcher(NullLogger.Instance, web);

        var preview = await fetcher.FetchAsync(new Uri("https://example.com/"));

        Assert.NotNull(preview);
        Assert.Null(preview.Image);
    }

    [Theory]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://localhost/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    public async Task OwnNetworks_AreNeverReached(string url)
    {
        // The real handler: the address is checked when connecting, after the name is resolved
        using var fetcher = new LinkPreviewFetcher(NullLogger.Instance);

        Assert.Null(await fetcher.FetchAsync(new Uri(url)));
    }
}
