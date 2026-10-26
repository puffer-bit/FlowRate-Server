using Shared.Enums;
using Shared.Permissions;

namespace Server.Core.Models;

public abstract class SessionModel
{
    /// <summary>Unlimited capacity.</summary>
    public const int NoCapacityLimit = -1;

    // Assigned by the database after the first save
    public int Id { get; set; }
    public string CoordinatorInstanceId { get; }
    public string Name { get; private set; }
    public int Capacity { get; private set; }

    public abstract SessionType SessionType { get; }

    /// <summary>What roles allow and deny in this session, apart from what they allow on the coordinator.</summary>
    public IReadOnlyList<PermissionOverride> PermissionOverrides { get; private set; } = [];

    protected SessionModel(int id, string coordinatorInstanceId, string name, int capacity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(coordinatorInstanceId);

        Id = id;
        CoordinatorInstanceId = coordinatorInstanceId;
        Name = ValidateName(name);
        Capacity = ValidateCapacity(capacity);
    }

    public void Rename(string name) => Name = ValidateName(name);

    public void SetPermissionOverrides(IEnumerable<PermissionOverride> overrides) =>
        PermissionOverrides = overrides.ToList();

    public void ChangeCapacity(int capacity) => Capacity = ValidateCapacity(capacity);

    public bool IsFull(int membersCount) => Capacity != NoCapacityLimit && membersCount >= Capacity;

    private static string ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name;
    }

    private static int ValidateCapacity(int capacity)
    {
        if (capacity != NoCapacityLimit && capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive or unlimited.");
        return capacity;
    }
}
