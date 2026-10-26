namespace Server.Application.Orchestrator.SFUPool.Models;

using System;
using System.ComponentModel.DataAnnotations.Schema;

public record SFUCredentials
{
    public string Username { get; init; }
    public string Key { get; init; }
    
    /// <summary>Both may be empty for a node that takes no credentials.</summary>
    public SFUCredentials(string username, string key)
    {
        Username = username.Trim();
        Key = key;
    }
}
