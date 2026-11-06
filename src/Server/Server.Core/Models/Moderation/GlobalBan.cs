using System.Net;
using Shared.Enums.User;
using Shared.Models.DataTransferObjects;

namespace Server.Core.Models.Moderation;

public record GlobalBan(Guid Id, string CoordinatorId, BanKind Kind, int? UserId, string? Address,
    string Reason, int CreatedBy, DateTime CreatedAt, DateTime? ExpiresAt,
    UserState PreviousState = UserState.Normal, int? RevokedBy = null, DateTime? RevokedAt = null)
{
    public bool IsActive(DateTime now) => RevokedAt == null && (ExpiresAt == null || ExpiresAt > now);
    public BanDTO ToDto() => new(Id, Kind, UserId, Address, Reason, CreatedBy, CreatedAt, ExpiresAt, RevokedBy, RevokedAt);

    public static string? NormalizeAddress(string? value)
    {
        if (!IPAddress.TryParse(value, out var address)) return null;
        return (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
    }
}
