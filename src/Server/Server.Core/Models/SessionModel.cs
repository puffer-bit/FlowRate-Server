using Shared.Enums;
using Shared.Permissions;

namespace Server.Core.Models;

public abstract class SessionModel
{
    // Assigned by the database after the first save
    public int Id { get; set; }
    public string CoordinatorInstanceId { get; }
    public string Name { get; private set; }

    public abstract SessionType SessionType { get; }

    /// <summary>What roles allow and deny in this session, apart from what they allow on the coordinator.</summary>
    public IReadOnlyList<PermissionOverride> PermissionOverrides { get; private set; } = [];

    protected SessionModel(int id, string coordinatorInstanceId, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(coordinatorInstanceId);

        Id = id;
        CoordinatorInstanceId = coordinatorInstanceId;
        Name = ValidateName(name);
    }

    public void Rename(string name) => Name = ValidateName(name);

    public void SetPermissionOverrides(IEnumerable<PermissionOverride> overrides) =>
        PermissionOverrides = overrides.ToList();

    private static string ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name;
    }
}
