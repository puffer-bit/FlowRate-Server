using Shared.Enums;
using Shared.Models.DataTransferObjects;

namespace Server.Application.Coordinator.Sessions.Layout;

public interface ISessionLayoutService
{
    Task LoadAsync(IEnumerable<int> sessionIds);
    SessionLayoutDTO GetLayout();
    IReadOnlyList<int> GetOrder();

    Task AddSessionAsync(int sessionId, int? categoryId);
    Task RemoveSessionAsync(int sessionId);

    Task<(SessionLayoutResult Result, int? CategoryId)> CreateCategoryAsync(string name);
    Task<SessionLayoutResult> RenameCategoryAsync(int categoryId, string name);
    Task<SessionLayoutResult> DeleteCategoryAsync(int categoryId);
    Task<SessionLayoutResult> MoveSessionAsync(int sessionId, int? categoryId, int index);
    Task<SessionLayoutResult> MoveCategoryAsync(int categoryId, int index);
}
