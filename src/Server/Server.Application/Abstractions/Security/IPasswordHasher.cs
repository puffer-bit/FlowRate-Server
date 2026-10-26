namespace Server.Application.Abstractions.Security;

public interface IPasswordHasher
{
    string? HashPassword(string? rawPassword);

    bool VerifyPassword(string? hashedPassword, string? rawPassword);
}