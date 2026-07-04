namespace DaemonMC.Telemetry;

public class PacketSnapshot
{
    public PacketType PacketType { get; set; }
    
    public int PacketId { get; set; } = 0;
    
    public PacketDirection Direction { get; set; }
}