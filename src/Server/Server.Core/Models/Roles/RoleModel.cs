using Shared.Enums;
using Shared.Validation;

namespace Server.Core.Models.Roles;

/// <summary>
/// A coordinator role. Roles rank by <see cref="Position"/>, higher above lower; the everyone role is always at 0
/// and every user has it.
/// </summary>
public sealed class RoleModel
{
    public const string EveryoneName = "@everyone";
    public const int EveryonePosition = 0;

    // Assigned by the database after the first save
    public int Id { get; set; }
    public string CoordinatorInstanceId { get; }
    public string Name { get; private set; }
    public int? Color { get; private set; }
    public int Position { get; private set; }
    public Permission Permissions { get; private set; }
    public bool IsEveryone { get; }

    /// <summary>Grows with every change of name, color or permissions.</summary>
    public long Revision { get; private set; }

    public RoleModel(int id, string coordinatorInstanceId, string name, int? color, int position,
        Permission permissions, bool isEveryone, long revision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(coordinatorInstanceId);
        if (isEveryone != (position == EveryonePosition))
            throw new ArgumentOutOfRangeException(nameof(position), position,
                "Only the everyone role is at the lowest position.");

        Id = id;
        CoordinatorInstanceId = coordinatorInstanceId;
        Name = isEveryone ? EveryoneName : ValidateName(name);
        Color = ValidateColor(color);
        Position = position;
        Permissions = ValidatePermissions(permissions);
        IsEveryone = isEveryone;
        Revision = revision;
    }

    public static RoleModel CreateEveryone(string coordinatorInstanceId, Permission permissions) =>
        new(0, coordinatorInstanceId, EveryoneName, null, EveryonePosition, permissions, true, 0);

    /// <summary>A new role; not saved yet.</summary>
    public static RoleModel Create(string coordinatorInstanceId, string name, int? color, int position,
        Permission permissions) =>
        new(0, coordinatorInstanceId, name, color, position, permissions, false, 0);

    public static bool IsValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= Limits.RoleNameMaxLength
                                         && name.Trim() != EveryoneName;

    public static bool IsValidColor(int? color) => color is null or >= 0 and <= Limits.RoleColorMax;

    public static bool IsValidPermissions(Permission permissions) => (permissions & ~Permission.All) == 0;

    /// <summary>Changes name, color and permissions; the everyone role keeps its name and has no color.</summary>
    public void Change(string name, int? color, Permission permissions)
    {
        if (!IsEveryone)
        {
            Name = ValidateName(name);
            Color = ValidateColor(color);
        }

        Permissions = ValidatePermissions(permissions);
        Revision++;
    }

    public void MoveTo(int position)
    {
        if (IsEveryone || position <= EveryonePosition)
            throw new InvalidOperationException("The everyone role stays at the lowest position, alone.");
        Position = position;
    }

    private static string ValidateName(string name)
    {
        if (!IsValidName(name))
            throw new ArgumentException($"Role name must be 1 to {Limits.RoleNameMaxLength} characters " +
                                        $"and not {EveryoneName}.", nameof(name));
        return name.Trim();
    }

    private static int? ValidateColor(int? color) => IsValidColor(color)
        ? color
        : throw new ArgumentOutOfRangeException(nameof(color), color, "Color must be 0xRRGGBB.");

    private static Permission ValidatePermissions(Permission permissions) => IsValidPermissions(permissions)
        ? permissions
        : throw new ArgumentOutOfRangeException(nameof(permissions), permissions, "Unknown permissions.");
}
