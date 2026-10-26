using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Server.Push;

namespace Server.PushRelay;

public class Program
{
    private const int MaxRequestBodySize = 8 * 1024;
    private static readonly TimeSpan FcmTimeout = TimeSpan.FromSeconds(10);

    public static async Task Main(string[] args)
    {
        await Build(args).RunAsync();
    }

    public static WebApplication Build(string[] args, Action<IServiceCollection>? overrideServices = null)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = MaxRequestBodySize);

        builder.Services.AddOptions<RelayOptions>()
            .BindConfiguration(RelayOptions.SectionName)
            .Validate(options =>
            {
                var results = new List<ValidationResult>();
                return Validator.TryValidateObject(options, new ValidationContext(options), results, true);
            }, "The Relay section is not valid: ServiceAccountPath must point to an existing key file and the limits must be positive.")
            .ValidateOnStart();

        builder.Services.ConfigureHttpJsonOptions(json =>
            json.SerializerOptions.TypeInfoResolverChain.Insert(0, RelayJsonContext.Default));

        builder.Services.AddSingleton(provider => new RelayLimiter(provider.GetRequiredService<IOptions<RelayOptions>>().Value));
        builder.Services.AddSingleton<IFcmClient>(provider => new FcmClient(
            new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }) { Timeout = FcmTimeout },
            new ServiceAccountKeyFile(provider.GetRequiredService<IOptions<RelayOptions>>().Value.ServiceAccountPath)));

        overrideServices?.Invoke(builder.Services);

        var app = builder.Build();

        if (app.Services.GetRequiredService<IOptions<RelayOptions>>().Value.TrustForwardedHeaders)
        {
            var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor };
            forwarded.KnownIPNetworks.Clear();
            forwarded.KnownProxies.Clear();
            app.UseForwardedHeaders(forwarded);
        }

        app.MapPost(RelayContract.PushPath, RelayEndpoint.HandleAsync);

        return app;
    }
}
