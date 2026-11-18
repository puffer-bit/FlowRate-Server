namespace Server.Application.Abstractions.LinkPreviews;

/// <summary>An image of a page, checked to be one the clients can show.</summary>
public sealed record FetchedLinkImage(byte[] Content, string MimeType, int Width, int Height);

/// <summary>What a page says about itself, with the texts already cut to the protocol limits.</summary>
public sealed record FetchedLinkPreview(
    string? SiteName,
    string? Title,
    string? Description,
    FetchedLinkImage? Image,
    bool IsLargeImage);

/// <summary>
/// Fetches web pages for previews. Only public addresses are reached: an address of the server's own
/// networks, a port other than 80 and 443 or a redirect to such an address ends the fetch.
/// </summary>
public interface ILinkPreviewFetcher
{
    /// <returns>Null when the page could not be fetched or has nothing to show.</returns>
    Task<FetchedLinkPreview?> FetchAsync(Uri url, CancellationToken cancellationToken = default);
}

/// <summary>The images of link previews, one file per preview id.</summary>
public interface ILinkPreviewImageStore
{
    Task SaveAsync(int previewId, byte[] content, CancellationToken cancellationToken = default);
    Task<Stream?> OpenReadAsync(int previewId, CancellationToken cancellationToken = default);
    Task DeleteAsync(int previewId, CancellationToken cancellationToken = default);
}
