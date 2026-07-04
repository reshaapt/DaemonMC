using System.Buffers;
using System.IO.Pipelines;
using System.IO.Pipes;
using System.Threading.Channels;
using PipeOptions = System.IO.Pipes.PipeOptions;

namespace DaemonMC.Telemetry;

public class PacketTelemetryConsumer
{
    public const string PipeName = "DaemonMC_PacketTelemetryPipe";
    
    private readonly ChannelReader<PacketSnapshot> _reader;
    
    public PacketTelemetryConsumer(ChannelReader<PacketSnapshot> reader)
    {
        _reader = reader;
    }

    public async Task StartConsumingAsync()
    {
        while (true)
        {
            await using var pipe = new NamedPipeClientStream(serverName: ".", pipeName: PipeName, direction: PipeDirection.Out, options: PipeOptions.Asynchronous);
            await pipe.ConnectAsync();

            await Consume(pipe);
        }
    }

    private async Task Consume(Stream stream)
    {
        PipeWriter writer = PipeWriter.Create(stream, new StreamPipeWriterOptions(leaveOpen: true));
        
        await foreach (PacketSnapshot snap in _reader.ReadAllAsync())
        {
            Console.WriteLine($"Consumer got packet: {snap.PacketId}");

            Span<byte> buffer = writer.GetSpan(6);

            buffer[0] = (byte)snap.PacketType;
            BitConverter.TryWriteBytes(buffer.Slice(1, 4), snap.PacketId);
            buffer[5] = (byte)snap.Direction;

            writer.Advance(6);

            Console.WriteLine("Before Flush");
            FlushResult flush = await writer.FlushAsync();
            Console.WriteLine("After Flush");

            if (flush.IsCompleted || flush.IsCanceled)
                break;
        }
    }
}