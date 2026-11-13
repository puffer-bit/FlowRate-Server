using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Server.Application.Abstractions.Persistence;
using Server.Application.Orchestrator.Settings;
using Server.Application.Orchestrator.SFUPool;
using Server.Asp.Web.Api;
using Server.Asp.Web.Api.Contracts;
using Server.Core.Models.Orchestrator;

namespace Server.Asp.Tests.Web.Api;

public class SfuNodesControllerTests
{
    [Fact]
    public async Task Create_PersistsNodeAndNotifiesPool()
    {
        var repository = new Mock<ISfuNodeRepository>();
        repository.Setup(r => r.AddAsync(It.IsAny<SfuNodeSettingsModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(42);
        var store = new SfuNodeStore(repository.Object);
        var notified = false;
        store.Changed += () => { notified = true; return Task.CompletedTask; };
        var controller = new SfuNodesController(store, new Mock<ISfuNodeRegistry>().Object);

        var result = await controller.Create(new CreateSfuNodeDto
        {
            Name = " Media ", Host = " sfu.example.com ", Port = 50051, IsSecured = true,
            Username = " user ", Password = " secret "
        }, default);

        var created = Assert.IsType<CreatedResult>(result.Result);
        var node = Assert.IsType<SfuNodeDto>(created.Value);
        Assert.Equal(42, node.Id);
        Assert.Equal("Media", node.Name);
        Assert.Equal("sfu.example.com", node.Host);
        Assert.True(node.IsSecured);
        Assert.True(notified);
        repository.Verify(r => r.AddAsync(It.Is<SfuNodeSettingsModel>(n =>
            n.Name == "Media" && n.Host == "sfu.example.com" && n.Port == 50051 && n.IsEnabled &&
            n.Username == "user" && n.Key == " secret " && n.IsSecured), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("https://example.com", 50051)]
    [InlineData("example.com:50051", 50051)]
    [InlineData("::1", 50051)]
    [InlineData("example.com", 0)]
    [InlineData("example.com", 65536)]
    public void Create_RejectsInvalidEndpoint(string host, int port)
    {
        var request = new CreateSfuNodeDto { Name = "node", Host = host, Port = port };
        var errors = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(request, new ValidationContext(request), errors, validateAllProperties: true));
    }

    [Fact]
    public async Task UpdateCredentials_PreservesNodeAndNotifiesPool()
    {
        var store = new Mock<ISfuNodeStore>();
        store.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([
            new SfuNodeSettingsModel { Id = 7, Name = "node", Host = "localhost", Port = 50051, Username = "old", Key = "old" }
        ]);
        var controller = new SfuNodesController(store.Object, new Mock<ISfuNodeRegistry>().Object);
        var result = await controller.UpdateCredentials(7, new SfuCredentialsDto { Username = "new", Password = "secret" }, default);
        Assert.Equal(7, result.Value!.Id);
        store.Verify(s => s.UpdateAsync(It.Is<SfuNodeSettingsModel>(n => n.Id == 7 && n.Host == "localhost" &&
            n.Username == "new" && n.Key == "secret"), It.IsAny<CancellationToken>()), Times.Once);
        Assert.IsType<NotFoundResult>((await controller.UpdateCredentials(99, new SfuCredentialsDto(), default)).Result);
    }

    [Theory]
    [InlineData("user", "")]
    [InlineData("", "password")]
    [InlineData("bad:name", "password")]
    public void Credentials_RejectIncompleteOrInvalidPairs(string username, string password)
    {
        var request = new SfuCredentialsDto { Username = username, Password = password };
        Assert.False(Validator.TryValidateObject(request, new ValidationContext(request), [], validateAllProperties: true));
    }

    [Fact]
    public void Create_AllowsHostnamesAndOptionalCredentials()
    {
        var request = new CreateSfuNodeDto { Name = "node", Host = "sfu.internal", Port = 50051 };
        Assert.True(Validator.TryValidateObject(request, new ValidationContext(request), [], validateAllProperties: true));
    }
}
