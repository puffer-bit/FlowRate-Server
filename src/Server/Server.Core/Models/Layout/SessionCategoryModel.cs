namespace Server.Core.Models.Layout;

public sealed class SessionCategoryModel
{
    public int Id { get; }
    public string Name { get; private set; }

    public SessionCategoryModel(int id, string name)
    {
        Id = id;
        Name = ValidateName(name);
    }

    public void Rename(string name) => Name = ValidateName(name);

    private static string ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name;
    }
}
