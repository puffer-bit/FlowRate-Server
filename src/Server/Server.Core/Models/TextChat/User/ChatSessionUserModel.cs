namespace Server.Core.Models.TextChat.User;

public sealed class ChatSessionUserModel
{
    public int UserId { get; }
    public bool IsAdmin { get; private set; }

    public ChatSessionUserModel(int userId, bool isAdmin = false)
    {
        UserId = userId;
        IsAdmin = isAdmin;
    }

    public void MakeAdmin() => IsAdmin = true;

    public void RemoveAdmin() => IsAdmin = false;
}
