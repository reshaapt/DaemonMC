namespace DaemonMC.Telemetry;

public sealed class PacketTraceOperation
{
    public string Operation { get; set; } = string.Empty;

    public string Property { get; set; } = string.Empty;

    public int Offset { get; set; }

    public int Length { get; set; }
}
