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

    Task<(ErrorCode? Result, int? CategoryId)> CreateCategoryAsync(string name);
    Task<ErrorCode?> RenameCategoryAsync(int categoryId, string name);
    Task<ErrorCode?> DeleteCategoryAsync(int categoryId);
    Task<ErrorCode?> MoveSessionAsync(int sessionId, int? categoryId, int index);
    Task<ErrorCode?> MoveCategoryAsync(int categoryId, int index);
}
