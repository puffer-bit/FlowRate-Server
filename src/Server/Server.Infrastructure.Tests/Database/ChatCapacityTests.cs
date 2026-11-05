using Server.Core.Models;
using Server.Infrastructure.Database.Entities.TextChat;
using Server.Infrastructure.Mappers;
using Xunit;

namespace Server.Infrastructure.Tests.Database;

public sealed class ChatCapacityTests
{
    [Theory]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(-1)]
    public void SavedChatsHaveUnlimitedCapacity(int savedCapacity)
    {
        var chat = new ChatSessionEntity
        {
            Id = 7, Name = "General", CoordinatorInstanceId = "coordinator", Capacity = savedCapacity,
            CreatorUserId = 1, AdminUserId = 1
        }.ToModel();

        Assert.Equal(SessionModel.NoCapacityLimit, chat.Capacity);
        Assert.False(chat.IsFull(10_000));
        Assert.Equal(SessionModel.NoCapacityLimit, chat.ToEntity().Capacity);
    }
}
