using System.ComponentModel.DataAnnotations;

namespace Server.Infrastructure.Database.Entities;

public class SessionCategoryEntity
{
    public int Id { get; set; }

    [MaxLength(36)]
    public required string CoordinatorInstanceId { get; set; }

    [MaxLength(50)]
    public required string Name { get; set; }

    public int Position { get; set; }
}

public class SessionPlacementEntity
{
    public int SessionId { get; set; }

    [MaxLength(36)]
    public required string CoordinatorInstanceId { get; set; }

    public int? CategoryId { get; set; }
    public int Position { get; set; }
}
