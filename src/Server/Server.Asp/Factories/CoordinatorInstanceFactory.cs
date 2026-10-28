using Server.Core.Models.Coordinator;
using Server.Infrastructure.Coordinator.Connection.Gateway;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Notifications;
using Server.Infrastructure.Coordinator.Services;
using Server.Application.Orchestrator.SFUPool;
using Server.Application.Coordinator;
using Server.Application.Coordinator.Connections;
using Server.Application.Coordinator.Users;
using Server.Application.Abstractions.Gateway;
using Server.Application.Coordinator.Events;
using Server.Application.Coordinator.Authentication;
using Server.Application.Coordinator.Handlers.Hybrid;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Heartbeat;
using Server.Application.Coordinator.Sessions;
using Server.Application.Coordinator.Sessions.Layout;
using Server.Application.Coordinator.Handlers.Authentication;
using Server.Application.Coordinator.Handlers.CoordinatorInfo;
using Server.Application.Coordinator.Handlers.Heartbeat;
using Server.Application.Coordinator.Handlers.Chat;
using Server.Application.Coordinator.Handlers.Sessions;
using Server.Application.Coordinator.Handlers.Users;
using Server.Application.Abstractions.Factories;
using Server.Asp.Web.Realtime;
using Server.Application.Abstractions.Persistence;
using Server.Application.Abstractions.Push;
using Server.Application.Coordinator.Handlers.Push;
using Server.Application.Coordinator.Handlers.Media;
using Server.Application.Coordinator.Media;
using Server.Application.Abstractions.Media;
using Server.Infrastructure.Coordinator.Media;
using Server.Infrastructure.Coordinator.Push;
using Server.Application.Coordinator.Push;
using Microsoft.Extensions.Options;
using Server.Application.Coordinator.Connections.RateLimiting;
using Server.Infrastructure.Options;
using Server.Application.Coordinator.Settings;
using Server.Application.Coordinator.Handlers.Settings;
using Server.Application.Coordinator.Handlers.Roles;
using Server.Application.Coordinator.Roles;
using Server.Application.Coordinator.Handlers.Users.Security;
using Server.Application.Orchestrator.Security;



namespace Server.Asp.Factories;

public class CoordinatorInstanceFactory : ICoordinatorInstanceFactory
{
    private readonly IServiceProvider _provider;

    public CoordinatorInstanceFactory(IServiceProvider provider)
    {
        _provider = provider;
    }

    public ICoordinatorInstance CreateCoordinatorInstance(string serverVersion,
        CoordinatorInstanceModel coordinatorInstanceModel)
    {
        var loggerFactory = _provider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger(
            $"Coordinator {coordinatorInstanceModel.Id}");
        
        var connectionsContext = CreateConnectionsContext();
        var messageSender = CreateMessageSender(logger, connectionsContext);
        var panelEvents = new CoordinatorPanelEvents(coordinatorInstanceModel.Id, _provider.GetRequiredService<IPanelNotifier>());
        var pushNotifier = ActivatorUtilities.CreateInstance<PushNotifier>(_provider, logger, coordinatorInstanceModel.Id);
        var readService = ActivatorUtilities.CreateInstance<ChatReadService>(_provider, logger, messageSender);
        var visibility = new SessionVisibility(messageSender);
        var coordinatorEventBus = CreateEventBus(logger, messageSender, visibility, panelEvents, pushNotifier,
            readService);

        var coordinatorInstanceContext =
            CreateCoordinatorInstanceContext(serverVersion, coordinatorInstanceModel);
        
        var messageParser = CreateMessageParser(logger);
        var rateLimits = new RequestRateLimits(coordinatorInstanceModel.RateLimits);
        var mediaDirectory = _provider.GetRequiredService<IOptions<StorageOptions>>().Value.MediaDirectory;
        var mediaStorage = new TusDiskMediaStorage(Path.Combine(mediaDirectory, coordinatorInstanceModel.Id));
        var sfuPool = CreateSfuPool(coordinatorInstanceModel);
        var chatSessionManager = CreateChatSessionManager(logger, coordinatorInstanceModel.Id, coordinatorEventBus, mediaStorage);
        var userManager = CreateUserManager(logger, coordinatorInstanceModel.Id, coordinatorInstanceContext,
            connectionsContext);
        var mediaService = ActivatorUtilities.CreateInstance<MediaService>(_provider, logger, mediaStorage,
            chatSessionManager, coordinatorInstanceModel.Uploads, coordinatorInstanceModel.Media, coordinatorInstanceModel.Id,
            userManager);
        readService.Watch(chatSessionManager);
        var pushReplyService = ActivatorUtilities.CreateInstance<PushReplyService>(_provider, logger, userManager,
            chatSessionManager, readService, rateLimits, coordinatorInstanceModel.Id);
        var gatewayLogger = loggerFactory.CreateLogger($"Coordinator {coordinatorInstanceModel.Id} Gateway");
        var gatewayService = CreateGatewayService(gatewayLogger, coordinatorInstanceModel, messageParser,
            new MediaHttpEndpoints(gatewayLogger, mediaService, mediaStorage), new PushHttpEndpoints(pushReplyService),
            rateLimits);
        var hybridSessionManager = CreateHybridSessionManager(logger, coordinatorInstanceModel.Id, coordinatorEventBus, sfuPool);
        var sessionLayout = ActivatorUtilities.CreateInstance<SessionLayoutService>(_provider, logger, messageSender,
            coordinatorInstanceModel.Id);
        var sessionManager = CreateSessionManager(chatSessionManager, hybridSessionManager, sessionLayout,
            coordinatorInstanceModel.Id);
        var roles = ActivatorUtilities.CreateInstance<CoordinatorRoles>(_provider, logger, userManager, messageSender,
            coordinatorInstanceModel.Id);
        panelEvents.Watch(userManager);
        pushNotifier.Watch(userManager, chatSessionManager, roles);
        visibility.Watch(roles, sessionManager, userManager);
        mediaService.Watch(roles);
        pushReplyService.Watch(roles);
        var authenticator = CreateAuthenticator(logger, coordinatorInstanceContext);
        var heartbeatManager = CreateHeartbeatManager(logger, connectionsContext, new HeartbeatSettings(
            coordinatorInstanceModel.Heartbeat.Interval,
            coordinatorInstanceModel.Heartbeat.Timeout));
        var connectionManager = CreateConnectionManager(logger, connectionsContext, heartbeatManager,
            new ClientConnectionFactory(rateLimits));

        var messagesHandlers = CreateMessageHandlers(logger, 
            coordinatorInstanceContext, coordinatorInstanceModel.Id, userManager, sessionManager, sessionLayout,
            chatSessionManager, hybridSessionManager, messageSender, heartbeatManager, connectionManager, authenticator,
            mediaService, readService, roles, visibility);
        
        var messageRouter = CreateMessageRouter(logger, messagesHandlers, connectionManager, rateLimits,
            new RequestAuthorizer(roles, hybridSessionManager, sessionManager));
        var settingsApplier = new CoordinatorSettingsApplier(logger, coordinatorInstanceContext, heartbeatManager,
            sfuPool, messageSender, userManager, rateLimits, mediaService);
        
        var coordinatorInstance = ActivatorUtilities.CreateInstance<CoordinatorInstance>(_provider, 
            coordinatorInstanceContext,
            logger,
            sessionManager,
            hybridSessionManager,
            userManager,
            messageSender,
            messageRouter,
            gatewayService,
            connectionManager,
            heartbeatManager,
            pushNotifier,
            mediaService,
            settingsApplier,
            roles);

        return coordinatorInstance;
    }
    
#pragma warning disable CA1859
    private ICoordinatorInstanceContext CreateCoordinatorInstanceContext(string serverVersion, CoordinatorInstanceModel coordinatorInstanceModel) =>
        ActivatorUtilities.CreateInstance<CoordinatorInstanceContext>(_provider, serverVersion, coordinatorInstanceModel);

    private IConnectionManager CreateConnectionManager(ILogger logger, IConnectionsContext connectionsContext,
        IHeartbeatManager heartbeatManager, IClientConnectionFactory clientConnectionFactory) =>
        ActivatorUtilities.CreateInstance<ConnectionManager>(_provider, logger, connectionsContext, heartbeatManager,
            clientConnectionFactory);
    private IConnectionsContext CreateConnectionsContext() =>
        ActivatorUtilities.CreateInstance<ConnectionsContext>(_provider);

    private IAuthenticator CreateAuthenticator(ILogger logger, ICoordinatorInstanceContext coordinatorInstanceContext) =>
        ActivatorUtilities.CreateInstance<Authenticator>(_provider, logger, coordinatorInstanceContext);
    
    private IGatewayService CreateGatewayService(ILogger logger, CoordinatorInstanceModel coordinatorInstanceModel,
        IMessageParser parser, IMediaHttpEndpoints mediaEndpoints, IPushHttpEndpoints pushEndpoints,
        RequestRateLimits rateLimits) =>
        ActivatorUtilities.CreateInstance<GatewayService>(
            _provider, 
            logger,
            parser,
            coordinatorInstanceModel.Id, 
            coordinatorInstanceModel.Ip, 
            coordinatorInstanceModel.Port,
            mediaEndpoints,
            rateLimits,
            pushEndpoints);

    private IUserManager CreateUserManager(ILogger logger, string coordinatorInstanceId,
        ICoordinatorInstanceContext coordinatorInstanceContext, IConnectionsContext connectionsContext) =>
        ActivatorUtilities.CreateInstance<UserManager>(_provider, coordinatorInstanceId, logger, CreateUserManagerContext(),
            coordinatorInstanceContext, connectionsContext);
    private IUserManagerContext CreateUserManagerContext() =>
        ActivatorUtilities.CreateInstance<UserManagerContext>(_provider);

    private ISessionManager CreateSessionManager(IChatSessionManager chatSessionManager,
        IHybridSessionManager hybridSessionManager, ISessionLayoutService sessionLayout, string coordinatorInstanceId) =>
        ActivatorUtilities.CreateInstance<SessionManager>(_provider, chatSessionManager, hybridSessionManager,
            sessionLayout, coordinatorInstanceId);

    private IChatSessionManager CreateChatSessionManager(ILogger logger, string coordinatorInstanceId,
        ICoordinatorEventBus coordinatorEventBus, IMediaStorage mediaStorage) =>
        ActivatorUtilities.CreateInstance<ChatSessionManager>(_provider, logger, coordinatorEventBus, mediaStorage,
            coordinatorInstanceId);

    private IHybridSessionManager CreateHybridSessionManager(ILogger logger, string coordinatorInstanceId,
        ICoordinatorEventBus coordinatorEventBus, ISFUNodeSelector sfuPool) =>
        ActivatorUtilities.CreateInstance<HybridSessionManager>(_provider, logger, coordinatorEventBus, sfuPool,
            coordinatorInstanceId);

    private CoordinatorSfuPool CreateSfuPool(CoordinatorInstanceModel coordinatorInstanceModel) =>
        ActivatorUtilities.CreateInstance<CoordinatorSfuPool>(_provider, coordinatorInstanceModel.SfuNodeIds);

    private IMessageSender CreateMessageSender(ILogger logger, IConnectionsContext connectionsContext) =>
        ActivatorUtilities.CreateInstance<MessageSender>(_provider, logger, connectionsContext);
    
    private IMessageParser CreateMessageParser(ILogger logger) =>
        ActivatorUtilities.CreateInstance<MessageParser>(_provider, logger);
        
    private IMessageRouter CreateMessageRouter(ILogger logger, IEnumerable<IMessageHandler> handlers,
        IConnectionManager connectionManager, RequestRateLimits limits, RequestAuthorizer authorizer) =>
        ActivatorUtilities.CreateInstance<MessageRouter>(_provider, logger, handlers, connectionManager, limits,
            authorizer);
    
    private IHeartbeatManager CreateHeartbeatManager(ILogger logger, IConnectionsContext connectionsContext,
        HeartbeatSettings settings) =>
        ActivatorUtilities.CreateInstance<HeartbeatManager>(_provider, logger, connectionsContext, settings);
    
    private static ICoordinatorEventBus CreateEventBus(ILogger logger, IMessageSender messageSender,
        SessionVisibility visibility, CoordinatorPanelEvents panelEvents, PushNotifier pushNotifier,
        ChatReadService readService) =>
        new CoordinatorEventBus(logger,
            [new ChatSessionNotifier(messageSender, visibility), new HybridSessionNotifier(messageSender, visibility),
                panelEvents, pushNotifier, readService]);
    
    private IEnumerable<IMessageHandler> CreateMessageHandlers(
        ILogger logger,
        ICoordinatorInstanceContext coordinatorInstanceContext,
        string coordinatorInstanceId,
        IUserManager userManager,
        ISessionManager sessionManager,
        ISessionLayoutService sessionLayout,
        IChatSessionManager chatSessionManager,
        IHybridSessionManager hybridSessionManager,
        IMessageSender messageSender,
        IHeartbeatManager heartbeatManager,
        IConnectionManager connectionManager,
        IAuthenticator authenticator,
        IMediaService mediaService,
        ChatReadService readService,
        ICoordinatorPermissions permissions,
        SessionVisibility visibility)
    {
        var userSessions = new UserSessions(_provider.GetRequiredService<IUserSessionRepository>(), connectionManager,
            coordinatorInstanceId, _provider.GetRequiredService<TimeProvider>());
        var userLogin = new UserLogin(userManager, userSessions);
        var pendingLogins = new PendingLogins();
        var secondStage = new SecondStageAuthentication(logger, messageSender, connectionManager, userLogin, pendingLogins);
        yield return new AuthenticationActionHandler(logger, messageSender, authenticator, connectionManager, userLogin,
            pendingLogins);
        yield return new TwoFactorAuthenticationHandler(authenticator, secondStage);
        yield return new Fido2AuthenticationHandler(authenticator, secondStage);

        var securityReplies = new UserSecurityReplies(_provider.GetRequiredService<IUserSecurityService>(),
            coordinatorInstanceId);
        yield return new UserSecurityInfoHandler(messageSender, securityReplies);
        yield return new UserSetPasswordHandler(messageSender, securityReplies);
        yield return new UserTwoFactorSetupHandler(messageSender, securityReplies);
        yield return new UserTwoFactorEnableHandler(messageSender, securityReplies);
        yield return new UserTwoFactorDisableHandler(messageSender, securityReplies);
        yield return new UserPasskeyOptionsHandler(messageSender, securityReplies);
        yield return new UserPasskeyRegisterHandler(messageSender, securityReplies);
        yield return new UserPasskeyDeleteHandler(messageSender, securityReplies);
        yield return new UserSessionsHandler(messageSender, connectionManager, userSessions);
        yield return new UserSessionRevokeHandler(messageSender, connectionManager, userSessions);
        yield return new UserIdentityResetHandler(messageSender, connectionManager, securityReplies,
            _provider.GetRequiredService<IUserRepository>(), userSessions);
        
        yield return ActivatorUtilities.CreateInstance<CreateSessionHandler>(_provider, sessionManager, messageSender);
        yield return ActivatorUtilities.CreateInstance<LeaveSessionHandler>(_provider, hybridSessionManager, messageSender);
        yield return ActivatorUtilities.CreateInstance<DeleteSessionHandler>(_provider, sessionManager, messageSender);
        yield return new ChangeHybridSessionSettingsHandler(hybridSessionManager, messageSender);
        yield return new ChangeSessionPermissionsHandler(sessionManager, permissions, visibility, messageSender);
        yield return new CreateSessionCategoryHandler(sessionLayout, messageSender);
        yield return new RenameSessionCategoryHandler(sessionLayout, messageSender);
        yield return new DeleteSessionCategoryHandler(sessionLayout, messageSender);
        yield return new MoveSessionHandler(sessionLayout, messageSender);
        yield return new MoveSessionCategoryHandler(sessionLayout, messageSender);
        yield return ActivatorUtilities.CreateInstance<KickFromSessionHandler>(_provider, hybridSessionManager, messageSender);
        yield return ActivatorUtilities.CreateInstance<BanInSessionHandler>(_provider, hybridSessionManager, messageSender);
        yield return ActivatorUtilities.CreateInstance<ApproveInSessionHandler>(_provider, hybridSessionManager, messageSender);
        yield return ActivatorUtilities.CreateInstance<RejectInSessionHandler>(_provider, hybridSessionManager, messageSender);
        
        yield return ActivatorUtilities.CreateInstance<HybridSessionJoinHandler>(_provider, hybridSessionManager, messageSender);
        yield return ActivatorUtilities.CreateInstance<HybridSessionConnectHandler>(_provider, hybridSessionManager, messageSender);
        yield return ActivatorUtilities.CreateInstance<HybridSessionPeerListRequestHandler>(_provider, hybridSessionManager, messageSender);
        yield return ActivatorUtilities.CreateInstance<HybridSessionStartVideoShareHandler>(_provider, hybridSessionManager, messageSender);
        yield return ActivatorUtilities.CreateInstance<HybridSessionStopVideoShareHandler>(_provider, hybridSessionManager, messageSender);
        yield return ActivatorUtilities.CreateInstance<HybridSessionStartScreenShareHandler>(_provider, hybridSessionManager, messageSender);
        yield return ActivatorUtilities.CreateInstance<HybridSessionStopScreenShareHandler>(_provider, hybridSessionManager, messageSender);
        yield return ActivatorUtilities.CreateInstance<HybridSessionSetScreenShareWatchHandler>(_provider, hybridSessionManager, messageSender);
        yield return ActivatorUtilities.CreateInstance<HybridSessionResumeInboundsHandler>(_provider, hybridSessionManager, messageSender);
        yield return ActivatorUtilities.CreateInstance<HybridSessionStartVoiceShareHandler>(_provider, hybridSessionManager, messageSender);
        yield return ActivatorUtilities.CreateInstance<HybridSessionStopVoiceShareHandler>(_provider, hybridSessionManager, messageSender);
        yield return ActivatorUtilities.CreateInstance<HybridSessionSetMicrophoneMuteHandler>(_provider, hybridSessionManager, messageSender);
        yield return ActivatorUtilities.CreateInstance<HybridSessionSetSoundMuteHandler>(_provider, hybridSessionManager, messageSender);
        yield return ActivatorUtilities.CreateInstance<HybridSessionSetAfkHandler>(_provider, hybridSessionManager, messageSender);
        yield return ActivatorUtilities.CreateInstance<HybridSessionPokeHandler>(_provider, hybridSessionManager, messageSender);
        
        yield return ActivatorUtilities.CreateInstance<SendMessageHandler>(_provider, chatSessionManager, messageSender, userManager);
        yield return ActivatorUtilities.CreateInstance<EditTextMessageHandler>(_provider, chatSessionManager, messageSender);
        yield return new DeleteTextMessageHandler(chatSessionManager, messageSender, permissions);
        yield return ActivatorUtilities.CreateInstance<ChatSessionHistoryHandler>(_provider, chatSessionManager, messageSender);
        yield return ActivatorUtilities.CreateInstance<ChatSessionMessageHandler>(_provider, chatSessionManager, messageSender);
        yield return ActivatorUtilities.CreateInstance<ChatSessionHistoryPageHandler>(_provider, chatSessionManager, messageSender);
        yield return ActivatorUtilities.CreateInstance<ChatSessionChangesHandler>(_provider, chatSessionManager, messageSender);
        yield return new ChatReadStatesHandler(readService, messageSender);
        yield return new ChatMarkReadHandler(readService, messageSender);

        yield return new SessionInfoHandler(visibility, sessionLayout, messageSender);
        yield return ActivatorUtilities.CreateInstance<UserActionHandler>(_provider, messageSender, userManager, logger);
        yield return new UserSearchHandler(messageSender, userManager);
        yield return new UserListHandler(messageSender, userManager);
        yield return ActivatorUtilities.CreateInstance<ChangeUserNameHandler>(_provider, messageSender, userManager, logger);
        yield return new SetAvatarHandler(logger, messageSender, userManager, mediaService);
        yield return new SetUserStatusHandler(logger, messageSender, userManager);
        yield return new SetUserStateHandler(logger, messageSender, userManager, connectionManager, hybridSessionManager);
        yield return new DeleteUserHandler(logger, messageSender, userManager, connectionManager);
        yield return ActivatorUtilities.CreateInstance<RegisterPushTokenHandler>(_provider, logger, messageSender, coordinatorInstanceId);
        yield return ActivatorUtilities.CreateInstance<UnregisterPushTokenHandler>(_provider, logger, messageSender, coordinatorInstanceId);
        yield return new MediaTokenHandler(messageSender, mediaService);
        yield return ActivatorUtilities.CreateInstance<CoordinatorSettingsHandler>(_provider, messageSender, coordinatorInstanceId);
        yield return ActivatorUtilities.CreateInstance<CoordinatorUpdateSettingsHandler>(_provider, logger, messageSender, coordinatorInstanceId);
        yield return new CoordinatorRolesHandler(messageSender, permissions);
        yield return ActivatorUtilities.CreateInstance<CreateRoleHandler>(_provider, messageSender, coordinatorInstanceId);
        yield return ActivatorUtilities.CreateInstance<UpdateRoleHandler>(_provider, messageSender, coordinatorInstanceId);
        yield return ActivatorUtilities.CreateInstance<DeleteRoleHandler>(_provider, messageSender, coordinatorInstanceId);
        yield return ActivatorUtilities.CreateInstance<ReorderRolesHandler>(_provider, messageSender, coordinatorInstanceId);
        yield return ActivatorUtilities.CreateInstance<AssignRoleHandler>(_provider, messageSender, coordinatorInstanceId);
        yield return ActivatorUtilities.CreateInstance<UnassignRoleHandler>(_provider, messageSender, coordinatorInstanceId);
        yield return ActivatorUtilities.CreateInstance<HeartbeatActionHandler>(_provider, heartbeatManager, messageSender);
        yield return ActivatorUtilities.CreateInstance<CoordinatorInfoActionHandler>(_provider, logger, coordinatorInstanceContext, messageSender, userManager);
    }

#pragma warning restore CA1859
}