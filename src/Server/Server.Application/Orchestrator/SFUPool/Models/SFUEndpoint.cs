namespace Server.Application.Orchestrator.SFUPool.Models;

public class SFUEndpoint
{
    public string Host { get; init; }
    public ushort Port { get; init; }
    
    public bool IsSecured { get; init; }
    
    public SFUEndpoint(string host, ushort port)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("Host cant be null");
            
        if (port == 0)
            throw new ArgumentException("Port cant be 0");

        Host = host.Trim().ToLowerInvariant();
        Port = port;
    }

    public string Address => $"{(IsSecured ? "https" : "http")}://{Host}:{Port}";
}