using System.ComponentModel.DataAnnotations;
using Shared.Validation;

namespace Server.Database.Entities.TextChat;

/// <summary>
/// A fetched web page, shared by the messages that show it. Rows are never changed: a page fetched
/// again is a new row. The image is a file named by <see cref="Id"/>.
/// </summary>
public class LinkPreviewEntity
{
    public int Id { get; set; }

    [MaxLength(36)]
    public required string CoordinatorInstanceId { get; set; }

    [MaxLength(Limits.UrlMaxLength)]
    public required string Url { get; set; }

    [MaxLength(Limits.LinkPreviewSiteNameMaxLength)]
    public string? SiteName { get; set; }
    [MaxLength(Limits.LinkPreviewTitleMaxLength)]
    public string? Title { get; set; }
    [MaxLength(Limits.LinkPreviewDescriptionMaxLength)]
    public string? Description { get; set; }

    [MaxLength(Limits.MimeTypeMaxLength)]
    public string? ImageMimeType { get; set; }
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
    public long ImageSize { get; set; }
    public bool IsLargeImage { get; set; }

    public DateTimeOffset FetchedAt { get; set; }
}
