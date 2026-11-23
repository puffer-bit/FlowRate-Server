namespace Server.Core.Models.User;

/// <summary>A device a user is connected from, as the client told it at the login. Not saved.</summary>
/// <param name="Platform">"Windows", "Linux", "Android"...</param>
/// <param name="OsVersion">The system and its version; null when the client did not tell.</param>
/// <param name="Architecture">The processor architecture: "x64", "arm64"...; null when the client did not tell.</param>
/// <param name="AppVersion">The version of the app.</param>
public sealed record UserDevice(string Platform, string? OsVersion, string? Architecture, string AppVersion);
