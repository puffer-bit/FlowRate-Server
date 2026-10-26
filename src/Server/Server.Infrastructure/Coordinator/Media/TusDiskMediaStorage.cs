using Server.Application.Abstractions.Media;
using tusdotnet.Stores;
using tusdotnet.Stores.FileIdProviders;

namespace Server.Infrastructure.Coordinator.Media;

public sealed class TusDiskMediaStorage : IMediaStorage
{
    public TusDiskStore Store { get; }

    public TusDiskMediaStorage(string directory)
    {
        Directory.CreateDirectory(directory);
        Store = new TusDiskStore(directory, false, TusDiskBufferSize.Default, new GuidFileIdProvider("n"));
    }

    public async Task<Stream?> OpenReadAsync(string id, CancellationToken cancellationToken = default)
    {
        var file = await Store.GetFileAsync(id, cancellationToken);
        return file == null ? null : await file.GetContentAsync(cancellationToken);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        if (await Store.FileExistAsync(id, cancellationToken))
            await Store.DeleteFileAsync(id, cancellationToken);
    }
}
