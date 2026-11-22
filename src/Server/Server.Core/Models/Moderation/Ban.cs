using System.Net;
using Shared.Enums.User;

namespace Server.Core.Models.Moderation;

public record Ban(Guid Id, string CoordinatorId, BanKind Kind, int? UserId, string? Address,
    string Reason, int CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt,
    UserState PreviousState = UserState.Normal)
{
    public bool IsActive(DateTimeOffset now) => ExpiresAt == null || ExpiresAt > now;

    public static string? NormalizeAddress(string? value)
    {
        if (!IPAddress.TryParse(value, out var address)) return null;
        return (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
    }
}
