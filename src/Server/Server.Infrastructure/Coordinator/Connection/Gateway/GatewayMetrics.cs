using System.Diagnostics.Metrics;

namespace Server.Infrastructure.Coordinator.Connection.Gateway;

/// <summary>
/// Counters of the client gateway; read them with dotnet-counters or any OpenTelemetry exporter.
/// </summary>
public static class GatewayMetrics
{
    public const string MeterName = "FlowRate.Gateway";

    private static readonly Meter Meter = new(MeterName);

    public static readonly UpDownCounter<long> OpenConnections =
        Meter.CreateUpDownCounter<long>("flowrate.gateway.connections.open", description: "WebSocket connections open now");

    public static readonly Counter<long> AcceptedConnections =
        Meter.CreateCounter<long>("flowrate.gateway.connections.accepted", description: "WebSocket connections accepted");

    public static readonly Counter<long> RejectedConnections =
        Meter.CreateCounter<long>("flowrate.gateway.connections.rejected", description: "Connections turned away for too many from one IP");

    public static readonly Counter<long> SendQueueOverflows =
        Meter.CreateCounter<long>("flowrate.gateway.send_queue.overflows", description: "Connections closed because they did not read fast enough");

    public static readonly Counter<long> OversizedMessages =
        Meter.CreateCounter<long>("flowrate.gateway.messages.oversized", description: "Connections closed for a message over the size limit");

    public static readonly Counter<long> MalformedMessages =
        Meter.CreateCounter<long>("flowrate.gateway.messages.malformed", description: "Messages that could not be parsed");
}
