namespace DaemonCapture.Telemetry;

public class PacketSnapshot
{
    public long Timestamp { get; set; }
    
    public PacketType PacketType { get; set; }
    
    public int PacketId { get; set; } = 0;
    
    public PacketDirection Direction { get; set; }

    public byte[]? Buffer { get; set; }
}