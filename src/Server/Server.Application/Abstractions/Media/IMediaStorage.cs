namespace Server.Application.Abstractions.Media;

public interface IMediaStorage
{
    Task<Stream?> OpenReadAsync(string id, CancellationToken cancellationToken = default);

    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}
