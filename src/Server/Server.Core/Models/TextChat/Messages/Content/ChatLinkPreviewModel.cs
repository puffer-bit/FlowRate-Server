using Shared.Models.DataTransferObjects.ChatSession.Messages.Links;

namespace Server.Core.Models.TextChat.Messages.Content;

/// <summary>
/// What a web page said about itself when it was fetched. A preview never changes: a page fetched
/// again makes a new one, and the messages that show the old one keep it.
/// </summary>
public sealed record ChatLinkPreviewModel
{
    public int Id { get; init; }
    public required string CoordinatorInstanceId { get; init; }

    /// <summary>The address the page was fetched from, as <see cref="LinkPreviewAddress.KeyOf"/> writes it.</summary>
    public required string Url { get; init; }

    public string? SiteName { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }

    public string? ImageMimeType { get; init; }
    public int ImageWidth { get; init; }
    public int ImageHeight { get; init; }
    public long ImageSize { get; init; }
    public bool IsLargeImage { get; init; }

    public required DateTime FetchedAt { get; init; }

    public bool HasImage => ImageMimeType != null;
}

/// <summary>
/// One spelling of an address, so that the same page is cached once: the fragment, which a server never
/// sees, is dropped, and <see cref="Uri"/> writes the scheme and host in lower case.
/// </summary>
public static class LinkPreviewAddress
{
    public static string KeyOf(Uri uri) => new UriBuilder(uri) { Fragment = string.Empty }.Uri.AbsoluteUri;

    /// <summary>The address a message with this text shows a preview of, in its key form.</summary>
    public static string? TargetOf(string? text) =>
        ChatLinks.PreviewTarget(text) is { } uri ? KeyOf(uri) : null;
}
