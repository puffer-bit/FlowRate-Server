namespace Server.Infrastructure.Database.Entities;

public class SfuNodeEntity
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Host { get; set; }
    public int Port { get; set; }
    public required string Username { get; set; }
    public required string Key { get; set; }
    public bool IsSecured { get; set; }
    public bool IsEnabled { get; set; } = true;

    public List<CoordinatorInstanceEntity> Coordinators { get; set; } = new();
}
