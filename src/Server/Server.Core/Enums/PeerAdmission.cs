namespace Server.Core.Enums;

/// <summary>Whether a peer is let into a hybrid session that may require host approval.</summary>
public enum PeerAdmission
{
    NotRequired = 0,
    Pending = 1,
    Approved = 2,
    Rejected = 3
}
