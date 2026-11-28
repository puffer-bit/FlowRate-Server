namespace Server.Application.Abstractions.Sfu;

public enum SFUTransportCreateResult
{
    InternalError = -1,
    NoError = 0,
    SFUNodeError = 1,
    SFUServiceNotAvailable = 2,
    PortAllocationFailed = 3,
    MaxTransportsReached = 4
}

public enum SFUTransportConnectResult
{
    InternalError = -1,
    NoError = 0,
    SFUNodeError = 1,
    SFUServiceNotAvailable = 2,
    PortAllocationFailed = 3,
    MaxTransportsReached = 4
}

public enum SFUOutboundCreateResult
{
    InternalError = -1,
    NoError = 0,
    UnsupportedMediaType = 1,
    SFUNodeError = 2,
    SFUServiceNotAvailable = 3,
    DuplicateOutbound = 4,
    TransportClosed = 5
}

public enum SFUInboundCreateResult
{
    InternalError = -1,
    NoError = 0,
    UnsupportedMediaType = 1,
    SFUNodeError = 2,
    SFUServiceNotAvailable = 3,
    DuplicateInbound = 4,
    TransportClosed = 5
}

public enum SFUOutboundUpgradeResult
{
    InternalError = -1,
    NoError = 0,
    SFUNodeError = 1,
    SFUServiceNotAvailable = 2,
    InvalidSSRC = 3,
    AlreadyUpgraded = 4
}

public enum SFUOutboundDowngradeResult
{
    InternalError = -1,
    NoError = 0,
    SFUNodeError = 1,
    SFUServiceNotAvailable = 2,
    InvalidSSRC = 3,
    AlreadyDowngraded = 4
}
