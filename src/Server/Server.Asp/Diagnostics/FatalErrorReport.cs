using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace Server.Asp.Diagnostics;

/// <summary>
/// Turns an exception that stopped the server into a short report: what went wrong, how to fix it,
/// and the full exception only when the cause is not one of the known startup problems.
/// </summary>
public static class FatalErrorReport
{
    private const int Width = 64;

    private sealed record Diagnosis(string Reason, IReadOnlyList<string> Details, string? Hint, bool ShowException);

    public static void Write(Exception exception)
    {
        var diagnosis = Diagnose(exception);
        var output = Console.Error;
        var colored = !Console.IsErrorRedirected;

        output.WriteLine();
        WithColor(colored, ConsoleColor.Red, () => output.WriteLine(Banner(" SERVER STOPPED: FATAL ERROR ")));
        output.WriteLine();
        WithColor(colored, ConsoleColor.Red, () => output.WriteLine($"  {diagnosis.Reason}"));

        foreach (var detail in diagnosis.Details)
            output.WriteLine($"    - {detail}");

        if (diagnosis.Hint != null)
        {
            output.WriteLine();
            WithColor(colored, ConsoleColor.Yellow, () => output.WriteLine($"  How to fix: {diagnosis.Hint}"));
        }

        if (diagnosis.ShowException)
        {
            output.WriteLine();
            WithColor(colored, ConsoleColor.DarkGray, () => output.WriteLine(exception));
        }

        output.WriteLine();
        WithColor(colored, ConsoleColor.Red, () => output.WriteLine(new string('=', Width)));
        output.WriteLine();
    }

    private static Diagnosis Diagnose(Exception exception)
    {
        var chain = Unwrap(exception).ToList();

        if (chain.OfType<OptionsValidationException>().FirstOrDefault() is { } invalidOptions)
            return new Diagnosis("The configuration is invalid:", invalidOptions.Failures.ToList(),
                "correct these values in appsettings.json or override them with environment variables " +
                "(Section__Key, e.g. Push__ServiceAccountPath).", ShowException: false);

        if (chain.OfType<SocketException>().FirstOrDefault(s => s.SocketErrorCode == SocketError.AddressAlreadyInUse) != null)
            return new Diagnosis("A port the server listens on is already taken:", [FirstOf(chain, typeof(IOException)).Message],
                "stop the other process (or another copy of this server), or change the port in the settings.",
                ShowException: false);

        if (chain.OfType<SocketException>().FirstOrDefault(s => s.SocketErrorCode == SocketError.AddressNotAvailable) != null)
            return new Diagnosis("The server cannot listen on the configured address:", [FirstOf(chain, typeof(IOException)).Message],
                "use an IP that belongs to this machine, or 0.0.0.0 to listen on all of them.", ShowException: false);

        if (chain.OfType<SocketException>().FirstOrDefault(s => s.SocketErrorCode == SocketError.AccessDenied) != null)
            return new Diagnosis("The server is not allowed to listen on the configured port:", [FirstOf(chain, typeof(IOException)).Message],
                "ports below 1024 need root or the CAP_NET_BIND_SERVICE capability; use a port above 1024.",
                ShowException: false);

        if (chain.OfType<UnauthorizedAccessException>().FirstOrDefault() is { } denied)
            return new Diagnosis("Access to a file or directory was denied:", [denied.Message],
                "check the permissions of the data, media, certificate and log directories " +
                "(in Docker, the volumes must be writable by the 'app' user).", ShowException: false);

        if (chain.FirstOrDefault(e => e.GetType().Name == "SqliteException") is { } database)
            return new Diagnosis("The database could not be opened or updated:", [database.Message],
                "check Database:ConnectionString and that its directory exists and is writable.", ShowException: true);

        if (chain.FirstOrDefault(IsFromSerilogConfiguration) is { } logging)
            return new Diagnosis("The logging configuration (Serilog section) is invalid:", [logging.Message],
                "check the Serilog section in appsettings.json and any Serilog__* environment variables; " +
                "array indexes must match the sinks defined there.", ShowException: false);

        return new Diagnosis("Unexpected exception:", [$"{exception.GetType().Name}: {exception.Message}"], null,
            ShowException: true);
    }

    private static IEnumerable<Exception> Unwrap(Exception exception)
    {
        var pending = new Stack<Exception>([exception]);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            yield return current;

            if (current is AggregateException aggregate)
                foreach (var inner in aggregate.InnerExceptions.Reverse())
                    pending.Push(inner);
            else if (current.InnerException != null)
                pending.Push(current.InnerException);
        }
    }

    // Kestrel's own message names the address and port, the socket's does not
    private static Exception FirstOf(List<Exception> chain, Type preferred) =>
        chain.FirstOrDefault(preferred.IsInstanceOfType) ?? chain[^1];

    private static bool IsFromSerilogConfiguration(Exception exception) =>
        exception.TargetSite?.DeclaringType?.Namespace?.StartsWith("Serilog.Settings.Configuration") == true
        || exception.StackTrace?.Contains("Serilog.Settings.Configuration.ConfigurationReader") == true;

    private static string Banner(string title)
    {
        var side = Math.Max(2, (Width - title.Length) / 2);
        return new string('=', side) + title + new string('=', Math.Max(2, Width - side - title.Length));
    }

    private static void WithColor(bool colored, ConsoleColor color, Action write)
    {
        if (!colored)
        {
            write();
            return;
        }

        Console.ForegroundColor = color;
        try { write(); }
        finally { Console.ResetColor(); }
    }
}
