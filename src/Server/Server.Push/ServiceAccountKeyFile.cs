using System.Text.Json;

namespace Server.Push;

public sealed record ServiceAccountKey(string ProjectId, string ClientEmail, string PrivateKeyPem, string TokenUri);

public sealed class ServiceAccountKeyFile
{
    private const string DefaultTokenUri = "https://oauth2.googleapis.com/token";

    private readonly string _path;
    private readonly Lock _lock = new();
    private ServiceAccountKey? _key;
    private DateTime _loadedWriteTime;

    public ServiceAccountKeyFile(string path)
    {
        _path = path;
    }

    public ServiceAccountKey Get()
    {
        lock (_lock)
        {
            var writeTime = File.GetLastWriteTimeUtc(_path);
            if (_key != null && writeTime == _loadedWriteTime)
                return _key;

            _key = Parse(File.ReadAllBytes(_path));
            _loadedWriteTime = writeTime;
            return _key;
        }
    }

    public static ServiceAccountKey Parse(ReadOnlySpan<byte> json)
    {
        var reader = new Utf8JsonReader(json);
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        return new ServiceAccountKey(
            Required(root, "project_id"),
            Required(root, "client_email"),
            Required(root, "private_key"),
            root.TryGetProperty("token_uri", out var tokenUri) && tokenUri.GetString() is { Length: > 0 } uri
                ? uri
                : DefaultTokenUri);
    }

    private static string Required(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.GetString() is { Length: > 0 } text
            ? text
            : throw new FormatException($"The service account key has no '{name}'.");
}
