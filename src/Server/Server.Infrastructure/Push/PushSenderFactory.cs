using Server.Application.Abstractions.Push;
using Server.Infrastructure.Options;
using Server.Push;

namespace Server.Infrastructure.Push;

public static class PushSenderFactory
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    public static IPushSender Create(PushOptions options)
    {
        if (options.SendsDirectly)
            return new FcmPushSender(new FcmClient(CreateHttpClient(), new ServiceAccountKeyFile(options.ServiceAccountPath!)));

        if (options.SendsThroughRelay)
            return new RelayPushSender(CreateHttpClient(), options.RelayUrl!);

        return new DisabledPushSender();
    }

    private static HttpClient CreateHttpClient() =>
        new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }) { Timeout = RequestTimeout };
}
