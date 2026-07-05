using System.Threading.Channels;

namespace DaemonMC.Telemetry;

public static class PacketTelemetryProducer
{
    private static ChannelWriter<PacketSnapshot> _writer;

    public static void SetWriter(ChannelWriter<PacketSnapshot> writer)
    {
        _writer = writer;
    }

    public static void Produce(PacketDirection direction, PacketType packetType, int packetId, byte[]? buffer = null, IReadOnlyList<PacketTraceOperation>? traceOperations = null)
    {
        var snap = new PacketSnapshot()
        {
            Direction = direction,
            PacketType = packetType,
            PacketId = packetId,
            Buffer = buffer,
            TraceOperations = traceOperations?.Select(operation => new PacketTraceOperation
            {
                Operation = operation.Operation,
                Offset = operation.Offset,
                Length = operation.Length
            }).ToList() ?? new List<PacketTraceOperation>(),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        _writer.TryWrite(snap);
    }
}
