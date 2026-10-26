using Shared.Models.Media;

namespace Server.Application.Coordinator.Media;

public static class AvatarAddress
{
    private const string Prefix = MediaEndpoints.Avatars + "/";

    public static string Of(string uploadId) => Prefix + uploadId;

    public static string? UploadIdOf(string? avatarUrl) =>
        avatarUrl != null && avatarUrl.StartsWith(Prefix, StringComparison.Ordinal) && avatarUrl.Length > Prefix.Length
            ? avatarUrl[Prefix.Length..]
            : null;
}
