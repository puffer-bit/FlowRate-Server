namespace Server.Core.Models.Layout;

public sealed class SessionLayoutModel
{
    private readonly List<int> _rootSessionIds;
    private readonly List<CategorySessions> _categories;

    public IReadOnlyList<int> RootSessionIds => _rootSessionIds;
    public IEnumerable<(SessionCategoryModel Category, IReadOnlyList<int> SessionIds)> Categories =>
        _categories.Select(entry => (entry.Category, (IReadOnlyList<int>)entry.SessionIds));
    public int CategoriesCount => _categories.Count;

    public SessionLayoutModel()
        : this([], [])
    {
    }

    public SessionLayoutModel(IEnumerable<int> rootSessionIds,
        IEnumerable<(SessionCategoryModel Category, IEnumerable<int> SessionIds)> categories)
    {
        _rootSessionIds = [];
        _categories = [];
        var placed = new HashSet<int>();

        foreach (var sessionId in rootSessionIds)
        {
            if (placed.Add(sessionId))
                _rootSessionIds.Add(sessionId);
        }

        foreach (var (category, sessionIds) in categories)
        {
            if (_categories.Any(entry => entry.Category.Id == category.Id))
                continue;

            var entry = new CategorySessions(category, []);
            foreach (var sessionId in sessionIds)
            {
                if (placed.Add(sessionId))
                    entry.SessionIds.Add(sessionId);
            }
            _categories.Add(entry);
        }
    }

    public SessionLayoutModel Clone() =>
        new(_rootSessionIds,
            _categories.Select(entry => (new SessionCategoryModel(entry.Category.Id, entry.Category.Name),
                (IEnumerable<int>)entry.SessionIds)));

    public bool ContainsSession(int sessionId) => FindContainer(sessionId) != null;

    public bool ContainsCategory(int categoryId) => FindCategory(categoryId) != null;

    public IReadOnlyList<int> OrderedSessionIds() =>
        _rootSessionIds.Concat(_categories.SelectMany(entry => entry.SessionIds)).ToList();

    public void Reconcile(IEnumerable<int> existingSessionIds)
    {
        var ordered = existingSessionIds.Distinct().ToList();
        var existing = ordered.ToHashSet();

        _rootSessionIds.RemoveAll(sessionId => !existing.Contains(sessionId));
        foreach (var entry in _categories)
            entry.SessionIds.RemoveAll(sessionId => !existing.Contains(sessionId));

        foreach (var sessionId in ordered)
        {
            if (!ContainsSession(sessionId))
                _rootSessionIds.Add(sessionId);
        }
    }

    public void AddSession(int sessionId, int? categoryId)
    {
        if (ContainsSession(sessionId))
            return;

        var container = categoryId is { } id && FindCategory(id) is { } entry ? entry.SessionIds : _rootSessionIds;
        container.Add(sessionId);
    }

    public bool RemoveSession(int sessionId) =>
        FindContainer(sessionId) is { } container && container.Remove(sessionId);

    public SessionLayoutChange MoveSession(int sessionId, int? categoryId, int index)
    {
        if (FindContainer(sessionId) is not { } source)
            return SessionLayoutChange.SessionNotExists;

        List<int> target;
        if (categoryId is { } id)
        {
            if (FindCategory(id) is not { } entry)
                return SessionLayoutChange.CategoryNotExists;
            target = entry.SessionIds;
        }
        else
        {
            target = _rootSessionIds;
        }

        var oldIndex = source.IndexOf(sessionId);
        source.RemoveAt(oldIndex);
        var newIndex = Math.Clamp(index, 0, target.Count);
        target.Insert(newIndex, sessionId);

        return ReferenceEquals(source, target) && oldIndex == newIndex
            ? SessionLayoutChange.Unchanged
            : SessionLayoutChange.Changed;
    }

    public SessionLayoutChange AddCategory(SessionCategoryModel category)
    {
        if (ContainsCategory(category.Id))
            return SessionLayoutChange.Unchanged;

        _categories.Add(new CategorySessions(category, []));
        return SessionLayoutChange.Changed;
    }

    public SessionLayoutChange RenameCategory(int categoryId, string name)
    {
        if (FindCategory(categoryId) is not { } entry)
            return SessionLayoutChange.CategoryNotExists;

        if (entry.Category.Name == name)
            return SessionLayoutChange.Unchanged;

        entry.Category.Rename(name);
        return SessionLayoutChange.Changed;
    }

    public SessionLayoutChange RemoveCategory(int categoryId)
    {
        if (FindCategory(categoryId) is not { } entry)
            return SessionLayoutChange.CategoryNotExists;

        _categories.Remove(entry);
        _rootSessionIds.AddRange(entry.SessionIds);
        return SessionLayoutChange.Changed;
    }

    public SessionLayoutChange MoveCategory(int categoryId, int index)
    {
        if (FindCategory(categoryId) is not { } entry)
            return SessionLayoutChange.CategoryNotExists;

        var oldIndex = _categories.IndexOf(entry);
        _categories.RemoveAt(oldIndex);
        var newIndex = Math.Clamp(index, 0, _categories.Count);
        _categories.Insert(newIndex, entry);

        return oldIndex == newIndex ? SessionLayoutChange.Unchanged : SessionLayoutChange.Changed;
    }

    private CategorySessions? FindCategory(int categoryId) =>
        _categories.FirstOrDefault(entry => entry.Category.Id == categoryId);

    private List<int>? FindContainer(int sessionId)
    {
        if (_rootSessionIds.Contains(sessionId))
            return _rootSessionIds;

        return _categories.FirstOrDefault(entry => entry.SessionIds.Contains(sessionId))?.SessionIds;
    }

    private sealed record CategorySessions(SessionCategoryModel Category, List<int> SessionIds);
}

public enum SessionLayoutChange
{
    Changed,
    Unchanged,
    SessionNotExists,
    CategoryNotExists
}
