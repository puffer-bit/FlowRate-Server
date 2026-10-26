using Server.Core.Models.Layout;
using Shared.Models.DataTransferObjects;

namespace Server.Application.Mappers;

public static class SessionLayoutMapper
{
    public static SessionLayoutDTO ToDto(this SessionLayoutModel layout, long revision) => new()
    {
        Revision = revision,
        RootSessionIds = layout.RootSessionIds.ToList(),
        Categories = layout.Categories
            .Select(entry => new SessionCategoryDTO
            {
                Id = entry.Category.Id,
                Name = entry.Category.Name,
                SessionIds = entry.SessionIds.ToList()
            })
            .ToList()
    };
}
