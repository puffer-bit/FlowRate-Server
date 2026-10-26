namespace Server.Core.Models.TextChat.Messages.Content;

public sealed record ChatFileContentModel
{
    public required int Id { get; init; }
    public required string FileName { get; init; }
    public required long Size { get; init; }
    public required string MimeType { get; init; }

    public string UploadId { get; init; } = string.Empty;
}
