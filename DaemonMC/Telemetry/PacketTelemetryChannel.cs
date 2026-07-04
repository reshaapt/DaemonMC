using System.Threading.Channels;

namespace DaemonMC.Telemetry;

public class PacketTelemetryChannel
{
    private readonly Channel<PacketSnapshot> _channel;

    public PacketTelemetryChannel(int capacity = 16384)
    {
        _channel = Channel.CreateBounded<PacketSnapshot>(new BoundedChannelOptions(capacity)
        {
            SingleWriter = true,
            SingleReader = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    }
    
    public ChannelWriter<PacketSnapshot> Writer => _channel.Writer;
    
    public ChannelReader<PacketSnapshot> Reader => _channel.Reader;
}