using System.Net;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Hosting.Server;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Shared.Serialization;
using Server.Infrastructure.Database;
using Server.Infrastructure.Coordinator.Connection.Gateway;
using Server.Infrastructure.Database.Repositories;
using ILogger = Serilog.ILogger;
using Server.Application.Orchestrator;
using Server.Asp.Factories;
using Microsoft.Extensions.Options;
using Server.Application.Orchestrator.SFUPool;
using Server.Application.Orchestrator.Bootstrap;
using Server.Application.Orchestrator.Settings;
using Server.Application.Orchestrator.Roles;
using Server.Application.Orchestrator.Security;
using Server.Application.Options;
using Server.Asp.Configuration;
using Server.Asp.Diagnostics;
using Server.Asp.Web;
using Server.Infrastructure.Options;
using Server.Infrastructure.Security;
using Server.Application.Abstractions.Persistence;
using Server.Application.Abstractions.Factories;
using Server.Application.Abstractions.Gateway;
using Server.Infrastructure.Coordinator.Services;
using Server.Application.Abstractions.Sfu;
using Server.Application.Abstractions.Push;
using Server.Application.Abstractions.Security;
using Server.Infrastructure.Push;

namespace Server.Asp
{
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            try
            {
                Console.WriteLine("Configuring services...");

                var builder = CreateWebApplicationBuilder(args);
                var configuration = builder.Configuration;

                ConfigureServices(builder.Services);
                ConfigureDI(builder.Services);
                ConfigureKestrel(builder.WebHost);
                ConfigureLogging(builder.Host, configuration);

                var app = builder.Build();
                await app.Services.InitializeDatabaseAsync();
                
                ConfigureMiddleware(app);
                await app.RunAsync();
                return Environment.ExitCode;
            }
            catch (HostAbortedException)
            {
                return 0;
            }
            catch (Exception e)
            {
                FatalErrorReport.Write(e);
                return 1;
            }
            finally
            {
                await Log.CloseAndFlushAsync();
            }
        }
        
        private static WebApplicationBuilder CreateWebApplicationBuilder(string[] args)
        {
            var options = new WebApplicationOptions
            {
                Args = args,
                WebRootPath = "Web/wwwroot"
            };

            return WebApplication.CreateBuilder(options);
        }

        private static void ConfigureLogging(IHostBuilder host, IConfiguration configuration)
        {
            host.UseSerilog((context, services, loggerConfig) =>
            {
                loggerConfig
                    .ReadFrom.Configuration(configuration)
                    .ReadFrom.Services(services);
                
            });
        }

        private static void ConfigureServices(IServiceCollection services)
        {
            // Enums go out as names and numbers only as numbers. The OpenAPI schema reads the minimal API
            // options, so both get the same settings
            services.AddControllers().AddJsonOptions(json => ConfigurePanelJson(json.JsonSerializerOptions));
            services.ConfigureHttpJsonOptions(json => ConfigurePanelJson(json.SerializerOptions));
            services.AddProblemDetails();
            services.AddOpenApi();
            services.AddPanel();

            // Only what is needed before the database is read; everything else lives in the database
            services.AddValidatedOptions<StorageOptions>(StorageOptions.SectionName);
            services.AddValidatedOptions<CertificateOptions>(CertificateOptions.SectionName);
            services.AddValidatedOptions<OrchestratorOptions>(OrchestratorOptions.SectionName);
            services.AddValidatedOptions<PushOptions>(PushOptions.SectionName);
            services.AddValidatedOptions<Fido2Options>(Fido2Options.SectionName);
        }

        private static void ConfigurePanelJson(JsonSerializerOptions options)
        {
            options.Converters.Add(new JsonStringEnumConverter());
            options.NumberHandling = JsonNumberHandling.Strict;
        }

        // The control panel is the orchestrator's web interface, so Kestrel listens on the orchestrator address
        private static void ConfigureKestrel(IWebHostBuilder webHost)
        {
            webHost.ConfigureKestrel(kestrel =>
            {
                var orchestrator = kestrel.ApplicationServices.GetRequiredService<IOptions<OrchestratorOptions>>().Value;
                var certificate = kestrel.ApplicationServices.GetRequiredService<ServerCertificate>();

                kestrel.Listen(IPAddress.Parse(orchestrator.ListenIp), orchestrator.Port,
                    listen => listen.UseHttps(certificate.Certificate));
            });
        }
        
        private static void ConfigureDI(IServiceCollection services)
        {
            services.AddHostedService<OrchestratorService>();

            services.AddPooledDbContextFactory<ServerDbContext>((provider, options) =>
                options.UseSqlite(provider.GetRequiredService<IOptions<StorageOptions>>().Value.DatabaseConnectionString)
                    .AddInterceptors(new SqliteSynchronousInterceptor()));
            
            services.AddSingleton<ServerCertificate>();
            services.AddSingleton(ProtocolJsonContext.Default.Options);
            services.AddSingleton<IMessageSerializer, MessageSerializer>();
            
            services.AddSingleton<IVideoSessionFactory, HybridSessionFactory>();
            services.AddSingleton<ICoordinatorInstanceFactory, CoordinatorInstanceFactory>();
            services.AddSingleton<ISFUNodeFactory, SFUNodeFactory>();
            services.AddSingleton<ILoggerFactory, LoggerFactory>();
            services.AddSingleton<ISfuNodeRegistry, SfuNodeRegistry>();
            services.AddSingleton<IOrchestratorInstanceContext, OrchestratorInstanceContext>();
            services.AddSingleton<IOrchestratorInstance, OrchestratorInstance>();
            services.AddSingleton<ConfigurationSeeder>();
            services.AddSingleton<ICoordinatorSettingsStore, CoordinatorSettingsStore>();
            services.AddSingleton<ISfuNodeStore, SfuNodeStore>();
            services.AddSingleton<ICoordinatorSettingsService, CoordinatorSettingsService>();
            services.AddSingleton<IRoleService, RoleService>();
            services.AddSingleton<IUserSecurityService, UserSecurityService>();
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<IListenEndpointProbe, ListenEndpointProbe>();
            
            services.AddSingleton<IOrchestratorInstanceRepository, OrchestratorInstanceRepository>();
            services.AddSingleton<IUserRepository, UserRepository>();
            services.AddSingleton<ICoordinatorInstanceRepository, CoordinatorInstanceRepository>();
            services.AddSingleton<IVideoSessionRepository, VideoSessionRepository>();
            services.AddSingleton<IMessagesRepository, MessagesRepository>();
            services.AddSingleton<IChatReadStateRepository, ChatReadStateRepository>();
            services.AddSingleton<IChatSessionRepository, ChatSessionRepository>();
            services.AddSingleton<ISessionsRepository, SessionsRepository>();
            services.AddSingleton<ISessionLayoutRepository, SessionLayoutRepository>();
            services.AddSingleton<IRoleRepository, RoleRepository>();
            services.AddSingleton<ISessionPermissionsRepository, SessionPermissionsRepository>();
            services.AddSingleton<ISfuNodeRepository, SfuNodeRepository>();
            services.AddSingleton<IMediaUploadRepository, MediaUploadRepository>();
            services.AddSingleton<IPushDeviceRepository>(provider =>
                new PushDeviceRepository(provider.GetRequiredService<IDbContextFactory<ServerDbContext>>()));
            services.AddSingleton<IPushSender>(provider =>
                PushSenderFactory.Create(provider.GetRequiredService<IOptions<PushOptions>>().Value));
            
            services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
            services.AddSingleton<ITwoFactorService, TwoFactorService>();
            services.AddSingleton<IPasskeyService, PasskeyService>();
            services.AddSingleton<IUserSecurityDataRepository, UserSecurityDataRepository>();
            services.AddSingleton<IUserPasskeyRepository, UserPasskeyRepository>();
        }

        private static void ConfigureMiddleware(WebApplication app)
        {
            if (app.Environment.IsDevelopment())
            {
                // The schema the panel frontend generates its API types from
                app.MapOpenApi();
            }
            else
            {
                app.UseExceptionHandler();
                app.UseHsts();
            }

            app.MapPanel();
        }
        
        public class OrchestratorService(
            IOrchestratorInstance orchestratorInstance,
            IHostApplicationLifetime lifetime,
            ILogger logger) : BackgroundService
        {
            protected override async Task ExecuteAsync(CancellationToken stoppingToken)
            {
                try
                {
                    await orchestratorInstance.Configure();
                    await orchestratorInstance.StartAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                }
                catch (Exception e)
                {
                    logger.Fatal(e, "The orchestrator failed to start. Stopping the server.");
                    Environment.ExitCode = 1;
                    lifetime.StopApplication();
                }
            }
            
            public override async Task StopAsync(CancellationToken cancellationToken)
            {
                try
                {
                    await orchestratorInstance.StopAsync(cancellationToken);
                }
                catch (Exception e)
                {
                    logger.Error(e, "Stopping the orchestrator failed.");
                }

                await base.StopAsync(cancellationToken);
            }
        }
    }
    
}
