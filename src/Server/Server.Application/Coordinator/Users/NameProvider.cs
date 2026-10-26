namespace Server.Application.Coordinator.Users;

public class NameProvider
{
    public string GenerateBaseName(string namePattern, IReadOnlyCollection<string> existingNames)
    {
        if (!existingNames.Contains(namePattern))
            return namePattern;

        int prefix = 1;
        string newName;

        do
        {
            newName = $"{namePattern}_{prefix}";
            prefix++;
        }
        while (existingNames.Contains(newName));

        return newName;
    }

}