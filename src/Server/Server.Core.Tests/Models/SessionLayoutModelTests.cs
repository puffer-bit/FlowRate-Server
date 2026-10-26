using Server.Core.Models.Layout;

namespace Server.Core.Tests.Models;

public class SessionLayoutModelTests
{
    private static SessionLayoutModel Layout() =>
        new([1, 2, 3], [(new SessionCategoryModel(10, "Voice"), [4, 5]), (new SessionCategoryModel(20, "Games"), [])]);

    [Fact]
    public void Constructor_DropsRepeatedSessionsAndCategories()
    {
        var layout = new SessionLayoutModel([1, 1, 2],
            [(new SessionCategoryModel(10, "A"), [2, 3]), (new SessionCategoryModel(10, "B"), [4])]);

        Assert.Equal([1, 2], layout.RootSessionIds);
        var category = Assert.Single(layout.Categories);
        Assert.Equal([3], category.SessionIds);
    }

    [Fact]
    public void MoveSession_WithinRoot_Reorders()
    {
        var layout = Layout();

        Assert.Equal(SessionLayoutChange.Changed, layout.MoveSession(1, null, 2));

        Assert.Equal([2, 3, 1], layout.RootSessionIds);
    }

    [Fact]
    public void MoveSession_IntoCategory_TakesItOutOfRoot()
    {
        var layout = Layout();

        Assert.Equal(SessionLayoutChange.Changed, layout.MoveSession(2, 10, 1));

        Assert.Equal([1, 3], layout.RootSessionIds);
        Assert.Equal([4, 2, 5], layout.Categories.First().SessionIds);
    }

    [Fact]
    public void MoveSession_PastTheEnd_GoesToTheEnd()
    {
        var layout = Layout();

        layout.MoveSession(1, 20, 999);

        Assert.Equal([1], layout.Categories.Last().SessionIds);
    }

    [Fact]
    public void MoveSession_ToItsOwnPlace_IsUnchanged()
    {
        Assert.Equal(SessionLayoutChange.Unchanged, Layout().MoveSession(2, null, 1));
    }

    [Fact]
    public void MoveSession_Unknown_IsReported()
    {
        var layout = Layout();

        Assert.Equal(SessionLayoutChange.SessionNotExists, layout.MoveSession(99, null, 0));
        Assert.Equal(SessionLayoutChange.CategoryNotExists, layout.MoveSession(1, 99, 0));
        Assert.Equal([1, 2, 3], layout.RootSessionIds);
    }

    [Fact]
    public void RemoveCategory_MovesItsSessionsToTheEndOfRoot()
    {
        var layout = Layout();

        Assert.Equal(SessionLayoutChange.Changed, layout.RemoveCategory(10));

        Assert.Equal([1, 2, 3, 4, 5], layout.RootSessionIds);
        Assert.Single(layout.Categories);
    }

    [Fact]
    public void MoveCategory_Reorders()
    {
        var layout = Layout();

        Assert.Equal(SessionLayoutChange.Changed, layout.MoveCategory(20, 0));

        Assert.Equal([20, 10], layout.Categories.Select(c => c.Category.Id));
    }

    [Fact]
    public void AddSession_ToUnknownCategory_GoesToRoot()
    {
        var layout = Layout();

        layout.AddSession(6, 99);
        layout.AddSession(7, 20);

        Assert.Equal([1, 2, 3, 6], layout.RootSessionIds);
        Assert.Equal([7], layout.Categories.Last().SessionIds);
    }

    [Fact]
    public void Reconcile_DropsGoneSessions_AndAppendsUnplacedOnesInTheGivenOrder()
    {
        var layout = Layout();

        layout.Reconcile([9, 1, 3, 5, 8]);

        Assert.Equal([1, 3, 9, 8], layout.RootSessionIds);
        Assert.Equal([5], layout.Categories.First().SessionIds);
    }

    [Fact]
    public void OrderedSessionIds_RootFirstThenCategories()
    {
        Assert.Equal([1, 2, 3, 4, 5], Layout().OrderedSessionIds());
    }

    [Fact]
    public void Clone_IsIndependent()
    {
        var layout = Layout();
        var clone = layout.Clone();

        clone.MoveSession(1, 10, 0);
        clone.RenameCategory(10, "Other");

        Assert.Equal([1, 2, 3], layout.RootSessionIds);
        Assert.Equal("Voice", layout.Categories.First().Category.Name);
    }
}
