using Server.Core.Models.TextChat.Messages;
using Server.Core.Models.TextChat.User;
using Shared.Enums;

namespace Server.Core.Models.TextChat;

public sealed class ChatSessionModel : SessionModel
{
    private readonly Dictionary<int, ChatSessionUserModel> _users = new();

    public int CreatorUserId { get; }
    public int AdminUserId { get; private set; }
    public string? Description { get; private set; }
    public string? IconPath { get; private set; }
    public ChatSessionOptions Options { get; private set; }
    public bool IsArchived { get; private set; }

    public IReadOnlyDictionary<int, ChatSessionUserModel> Users => _users;

    public override SessionType SessionType => SessionType.Chat;

    public ChatSessionModel(int id, string coordinatorInstanceId, string name, int capacity,
        int creatorUserId, int adminUserId, ChatSessionOptions options,
        string? description = null, string? iconPath = null, bool isArchived = false)
        : base(id, coordinatorInstanceId, name, capacity)
    {
        CreatorUserId = creatorUserId;
        AdminUserId = adminUserId;
        Options = options;
        Description = description;
        IconPath = iconPath;
        IsArchived = isArchived;
    }

    /// <summary>A new chat that is not saved yet. The creator becomes its admin.</summary>
    public static ChatSessionModel Create(string coordinatorInstanceId, string name, int creatorUserId,
        ChatSessionOptions? options = null) =>
        new(0, coordinatorInstanceId, name, NoCapacityLimit, creatorUserId, creatorUserId,
            options ?? ChatSessionOptions.Default);

    public bool IsAdmin(int userId) =>
        userId == AdminUserId || (_users.TryGetValue(userId, out var user) && user.IsAdmin);

    public bool CanDeleteMessage(ChatMessageModel message, int userId) =>
        message.IsSentBy(userId) || IsAdmin(userId);

    public bool CanPost => !IsArchived && !Options.IsReadOnly;

    public bool AddUser(int userId) => _users.TryAdd(userId, new ChatSessionUserModel(userId));

    public bool RemoveUser(int userId) => _users.Remove(userId);

    public void ChangeAdmin(int userId) => AdminUserId = userId;

    public void Describe(string? description, string? iconPath)
    {
        Description = description;
        IconPath = iconPath;
    }

    public void Configure(ChatSessionOptions options) => Options = options;

    public void Archive() => IsArchived = true;

    public void Unarchive() => IsArchived = false;
}
