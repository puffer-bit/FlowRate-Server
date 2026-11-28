using System.Globalization;
using Server.Application.Abstractions.LinkPreviews;

namespace Server.Infrastructure.Coordinator.LinkPreviews;

/// <summary>The images of link previews as files in one directory, named by the preview id.</summary>
public sealed class LinkPreviewImageStore : ILinkPreviewImageStore
{
    private readonly string _directory;

    public LinkPreviewImageStore(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
    }

    private string PathOf(int previewId) =>
        Path.Combine(_directory, previewId.ToString(CultureInfo.InvariantCulture));

    public async Task SaveAsync(int previewId, byte[] content, CancellationToken cancellationToken = default)
    {
        // Written aside and moved, so that a reader never sees half a file
        var path = PathOf(previewId);
        var temporary = path + ".tmp";
        await File.WriteAllBytesAsync(temporary, content, cancellationToken);
        File.Move(temporary, path, overwrite: true);
    }

    public Task<Stream?> OpenReadAsync(int previewId, CancellationToken cancellationToken = default)
    {
        try
        {
            return Task.FromResult<Stream?>(new FileStream(PathOf(previewId), FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete, 64 * 1024, useAsync: true));
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return Task.FromResult<Stream?>(null);
        }
    }

    public Task DeleteAsync(int previewId, CancellationToken cancellationToken = default)
    {
        File.Delete(PathOf(previewId));
        return Task.CompletedTask;
    }
}
