using Microsoft.Extensions.Logging;
using Server.Application.Abstractions.Persistence;
using Server.Application.Coordinator.Messaging;
using Server.Application.Mappers;
using Server.Core.Models.Layout;
using Shared.Enums;
using Shared.Models.DataTransferObjects;
using Shared.Models.Notifications.SessionInfo;
using Shared.Validation;

namespace Server.Application.Coordinator.Sessions.Layout;

public sealed class SessionLayoutService : ISessionLayoutService
{
    private readonly ILogger _logger;
    private readonly ISessionLayoutRepository _repository;
    private readonly IMessageSender _messageSender;
    private readonly string _coordinatorInstanceId;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private volatile LayoutState _state = new(new SessionLayoutModel(), DateTime.UtcNow.Ticks);

    public SessionLayoutService(ILogger logger, ISessionLayoutRepository repository, IMessageSender messageSender,
        string coordinatorInstanceId)
    {
        _logger = logger;
        _repository = repository;
        _messageSender = messageSender;
        _coordinatorInstanceId = coordinatorInstanceId;
    }

    public async Task LoadAsync(IEnumerable<int> sessionIds)
    {
        await _lock.WaitAsync();
        try
        {
            var layout = await _repository.GetAsync(_coordinatorInstanceId);
            var before = layout.OrderedSessionIds();
            layout.Reconcile(sessionIds);
            _state = new LayoutState(layout, _state.Revision + 1);

            if (!before.SequenceEqual(layout.OrderedSessionIds()))
                await _repository.SaveAsync(_coordinatorInstanceId, layout);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Session layout of coordinator {CoordinatorId} could not be loaded.",
                _coordinatorInstanceId);
        }
        finally
        {
            _lock.Release();
        }
    }

    public SessionLayoutDTO GetLayout()
    {
        var state = _state;
        return state.Layout.ToDto(state.Revision);
    }

    public IReadOnlyList<int> GetOrder() => _state.Layout.OrderedSessionIds();

    public async Task AddSessionAsync(int sessionId, int? categoryId)
    {
        await ChangeAsync(layout =>
        {
            if (layout.ContainsSession(sessionId))
                return SessionLayoutChange.Unchanged;

            layout.AddSession(sessionId, categoryId);
            return SessionLayoutChange.Changed;
        });
    }

    public async Task RemoveSessionAsync(int sessionId)
    {
        await ChangeAsync(layout => layout.RemoveSession(sessionId)
            ? SessionLayoutChange.Changed
            : SessionLayoutChange.Unchanged);
    }

    public async Task<(SessionLayoutResult Result, int? CategoryId)> CreateCategoryAsync(string name)
    {
        await _lock.WaitAsync();
        try
        {
            if (_state.Layout.CategoriesCount >= Limits.SessionCategoriesMaxCount)
                return (SessionLayoutResult.TooManyCategories, null);

            var categoryId = await _repository.AddCategoryAsync(_coordinatorInstanceId, name);
            var result = await ApplyAsync(layout => layout.AddCategory(new SessionCategoryModel(categoryId, name)));
            return (result, result == SessionLayoutResult.NoError ? categoryId : null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Session category {Name} could not be created.", name);
            return (SessionLayoutResult.InternalError, null);
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task<SessionLayoutResult> RenameCategoryAsync(int categoryId, string name) =>
        ChangeAsync(layout => layout.RenameCategory(categoryId, name));

    public Task<SessionLayoutResult> DeleteCategoryAsync(int categoryId) =>
        ChangeAsync(layout => layout.RemoveCategory(categoryId));

    public Task<SessionLayoutResult> MoveSessionAsync(int sessionId, int? categoryId, int index) =>
        ChangeAsync(layout => layout.MoveSession(sessionId, categoryId, index));

    public Task<SessionLayoutResult> MoveCategoryAsync(int categoryId, int index) =>
        ChangeAsync(layout => layout.MoveCategory(categoryId, index));

    private async Task<SessionLayoutResult> ChangeAsync(Func<SessionLayoutModel, SessionLayoutChange> change)
    {
        await _lock.WaitAsync();
        try
        {
            return await ApplyAsync(change);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Session layout of coordinator {CoordinatorId} could not be saved.",
                _coordinatorInstanceId);
            return SessionLayoutResult.InternalError;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<SessionLayoutResult> ApplyAsync(Func<SessionLayoutModel, SessionLayoutChange> change)
    {
        var layout = _state.Layout.Clone();

        switch (change(layout))
        {
            case SessionLayoutChange.SessionNotExists:
                return SessionLayoutResult.SessionNotExists;
            case SessionLayoutChange.CategoryNotExists:
                return SessionLayoutResult.CategoryNotExists;
            case SessionLayoutChange.Unchanged:
                return SessionLayoutResult.NoError;
        }

        await _repository.SaveAsync(_coordinatorInstanceId, layout);
        var state = new LayoutState(layout, _state.Revision + 1);
        _state = state;
        _messageSender.BroadcastMessage(new SessionLayoutChangedNotification(state.Layout.ToDto(state.Revision)));
        return SessionLayoutResult.NoError;
    }

    private sealed record LayoutState(SessionLayoutModel Layout, long Revision);
}
