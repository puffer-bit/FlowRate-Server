namespace Server.Core.Models.TextChat.Messages.Content;

public sealed record ChatFileContentModel
{
    public required int Id { get; init; }
    public required string FileName { get; init; }
    public required long Size { get; init; }
    public required string MimeType { get; init; }

    public string UploadId { get; init; } = string.Empty;

    /// <summary>Set for a file that plays as a video.</summary>
    public ChatFileVideoModel? Video { get; init; }

    /// <summary>The upload of the frame the video is shown with, given by the sender.</summary>
    public string? PreviewUploadId { get; init; }
}

/// <param name="Width">The sides of the picture as shown; 0 when unknown.</param>
/// <param name="Duration">Null when the index of the file could not be read.</param>
public sealed record ChatFileVideoModel(int Width, int Height, TimeSpan? Duration);
