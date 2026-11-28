using Moq;
using Server.Application.Abstractions.Persistence;
using Server.Application.Coordinator.Handlers.Settings;
using Server.Application.Coordinator.Messaging;
using Server.Application.Orchestrator.Settings;
using Server.Application.Orchestrator.SFUPool;
using Server.Core.Models.Coordinator;
using Shared.Models;
using Shared.Models.Requests.Coordinator;
using Shared.Models.Responses.Coordinator;

namespace Server.Application.Tests.Coordinator;

public sealed class CoordinatorSettingsHandlerTests
{
    [Theory]
    [InlineData("SQLite")]
    [InlineData("PostgreSQL")]
    public async Task SettingsResponse_ReportsTheServerDatabaseAlongsideEditableSettings(string provider)
    {
        var model = CoordinatorInstanceModel.Create("Server", "127.0.0.1", 5000, DateTimeOffset.UtcNow);
        var settings = new Mock<ICoordinatorSettingsService>();
        settings.Setup(service => service.GetAsync(model.Id, It.IsAny<CancellationToken>())).ReturnsAsync(model);
        settings.Setup(service => service.GetSfuNodesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var database = new Mock<IDatabaseInfo>();
        database.SetupGet(info => info.Provider).Returns(provider);
        var sender = new Mock<IMessageSender>();
        BaseMessage? sent = null;
        sender.Setup(service => service.SendMessageById("connection", It.IsAny<BaseMessage>()))
            .Callback<string, BaseMessage>((_, message) => sent = message);
        var handler = new CoordinatorSettingsHandler(sender.Object, settings.Object,
            Mock.Of<ISfuNodeRegistry>(), model.Id, database.Object);
        var request = new CoordinatorSettingsRequest();

        await handler.HandleMessage(request, "connection");

        var response = Assert.IsType<CoordinatorSettingsResponse>(sent);
        Assert.Equal(request.RequestId, response.RequestId);
        Assert.Equal(provider, response.DatabaseProvider);
        Assert.Equal(model.Name, response.Settings.Name);
        Assert.Empty(response.SFUNodes);
    }
}
