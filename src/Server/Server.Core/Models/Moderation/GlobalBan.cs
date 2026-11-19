using System.Net;
using Shared.Enums.User;

namespace Server.Core.Models.Moderation;

public record GlobalBan(Guid Id, string CoordinatorId, BanKind Kind, int? UserId, string? Address,
    string Reason, int CreatedBy, DateTime CreatedAt, DateTime? ExpiresAt,
    UserState PreviousState = UserState.Normal)
{
    public bool IsActive(DateTime now) => ExpiresAt == null || ExpiresAt > now;

    public static string? NormalizeAddress(string? value)
    {
        if (!IPAddress.TryParse(value, out var address)) return null;
        return (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
    }
}
