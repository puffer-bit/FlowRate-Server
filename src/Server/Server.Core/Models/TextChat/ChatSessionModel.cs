using Server.Core.Models.TextChat.Messages;
using Server.Core.Models.TextChat.User;
using Shared.Enums;

namespace Server.Core.Models.TextChat;

public sealed class ChatSessionModel : SessionModel
{
    private readonly Dictionary<int, ChatSessionUserModel> _users = new();

    public ChatKind Kind { get; private set; }
    public IReadOnlySet<int> MemberIds { get; private set; } = new HashSet<int>();
    public bool IsPrivate => Kind != ChatKind.ServerChannel;
    public ChatMessageModel? LastMessage { get; private set; }

    public void SetLastMessage(ChatMessageModel? message)
    {
        if (message != null && message.ChatId != Id)
            throw new ArgumentException("The last message must belong to this chat.", nameof(message));
        LastMessage = message;
    }

    public void SetAudience(ChatKind kind, IEnumerable<int> memberIds)
    {
        var members = memberIds.ToHashSet();
        if (!Enum.IsDefined(kind) || (kind != ChatKind.ServerChannel && members.Any(id => id <= 0))
            || (kind == ChatKind.Direct && members.Count != 2)
            || (kind == ChatKind.Group && members.Count is < 1 or > 100))
            throw new ArgumentException("Invalid chat audience.");
        Kind = kind;
        MemberIds = members;
    }

    public int CreatorUserId { get; }
    public int OwnerUserId { get; private set; }
    public string? Description { get; private set; }
    /// <summary>The picture of a group (an avatar address); null shows the first letter of its name.</summary>
    public string? AvatarUrl { get; private set; }
    public ChatSessionOptions Options { get; private set; }
    public bool IsArchived { get; private set; }

    public IReadOnlyDictionary<int, ChatSessionUserModel> Users => _users;

    public override SessionType SessionType => SessionType.Chat;

    public ChatSessionModel(int id, string coordinatorInstanceId, string name,
        int creatorUserId, int adminUserId, ChatSessionOptions options,
        string? description = null, string? avatarUrl = null, bool isArchived = false)
        : base(id, coordinatorInstanceId, name)
    {
        CreatorUserId = creatorUserId;
        OwnerUserId = adminUserId;
        Options = options;
        Description = description;
        AvatarUrl = avatarUrl;
        IsArchived = isArchived;
    }

    /// <summary>A new chat that is not saved yet. The creator becomes its admin.</summary>
    public static ChatSessionModel Create(string coordinatorInstanceId, string name, int creatorUserId,
        ChatSessionOptions? options = null) =>
        new(0, coordinatorInstanceId, name, creatorUserId, creatorUserId,
            options ?? ChatSessionOptions.Default);

    public bool IsAdmin(int userId) =>
        Kind != ChatKind.Direct && (userId == OwnerUserId || (_users.TryGetValue(userId, out var user) && user.IsAdmin));

    public bool CanDeleteMessage(ChatMessageModel message, int userId) =>
        message.IsSentBy(userId) || IsAdmin(userId);

    public bool CanPost => !IsArchived && !Options.IsReadOnly;

    public bool AddUser(int userId) => _users.TryAdd(userId, new ChatSessionUserModel(userId));

    public bool RemoveUser(int userId) => _users.Remove(userId);

    public void ChangeAdmin(int userId) => OwnerUserId = userId;

    public void Describe(string? description) => Description = description;

    public void SetAvatar(string? avatarUrl) => AvatarUrl = avatarUrl;

    public void Configure(ChatSessionOptions options) => Options = options;

    public void Archive() => IsArchived = true;

    public void Unarchive() => IsArchived = false;
}
