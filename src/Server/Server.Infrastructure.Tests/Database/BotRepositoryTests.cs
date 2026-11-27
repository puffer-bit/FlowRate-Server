using Microsoft.EntityFrameworkCore;
using Server.Application.Coordinator.Authentication;
using Server.Core.Models.User;
using Server.Infrastructure.Database.Repositories;
using Shared.Enums.User;
using Xunit;

namespace Server.Infrastructure.Tests.Database;

/// <summary>Bot accounts in the users table: saved without recovery keys, counted apart from people.</summary>
[Trait("Category", "DatabaseProvider")]
public sealed class BotRepositoryTests : IDisposable
{
    private const string Coordinator = "coordinator";
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly RepositoryTestDatabase _database = new();
    private readonly UserRepository _users;

    public BotRepositoryTests()
    {
        using var context = _database.CreateDbContext();
        context.Database.Migrate();
        _users = new UserRepository(_database);
    }

    public void Dispose() => _database.Dispose();

    private Task<int> AddPerson(string name) => _users.AddAsync(UserModel.Create(Coordinator, name, null, Now),
        new UserRecoveryKeys(SecretHash.Of(name), [1, 2]));

    private Task<int> AddBot(string name, int ownerId, bool isDebug = false) =>
        _users.AddAsync(UserModel.CreateBot(Coordinator, name, ownerId, isDebug, Now), recoveryKeys: null);

    [Fact]
    public async Task Bot_IsSavedWithItsKindAndOwner_AndWithoutRecoveryKeys()
    {
        var ownerId = await AddPerson("owner");
        var botId = await AddBot("helper", ownerId, isDebug: true);

        var bot = await _users.GetByIdAsync(botId);

        Assert.Equal(UserKind.DebugBot, bot?.Kind);
        Assert.Equal(ownerId, bot?.OwnerUserId);
    }

    [Fact]
    public async Task Bots_AreNotCountedAsPeople_AndDebugBotsNotAsBots()
    {
        var ownerId = await AddPerson("owner");
        await AddPerson("alice");
        await AddBot("helper", ownerId);
        await AddBot("music", ownerId);
        await AddBot("tester", ownerId, isDebug: true);

        Assert.Equal(2, await _users.CountAsync(Coordinator));
        Assert.Equal(2, await _users.CountBotsAsync(Coordinator));
    }

    [Fact]
    public async Task GetBots_ListsBotsOfEitherKind_ButNoPeople()
    {
        var ownerId = await AddPerson("owner");
        var helper = await AddBot("helper", ownerId);
        var tester = await AddBot("tester", ownerId, isDebug: true);

        var bots = await _users.GetBotsAsync(Coordinator);

        Assert.Equal(new[] { helper, tester }, bots.Select(bot => bot.Id));
        Assert.Empty(await _users.GetBotsAsync("another"));
    }

    [Fact]
    public async Task ClearBotOwner_LeavesOnlyTheBotsOfThatOwnerWithoutOne()
    {
        var ownerId = await AddPerson("owner");
        var otherId = await AddPerson("other");
        var mine = await AddBot("helper", ownerId);
        var theirs = await AddBot("music", otherId);

        Assert.Equal(new[] { mine }, await _users.ClearBotOwnerAsync(ownerId));

        Assert.Null((await _users.GetByIdAsync(mine))?.OwnerUserId);
        Assert.Equal(otherId, (await _users.GetByIdAsync(theirs))?.OwnerUserId);
        Assert.Empty(await _users.ClearBotOwnerAsync(ownerId));
    }
}
