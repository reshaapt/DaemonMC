using System.Buffers.Binary;
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
            byte[] payload = snap.Buffer ?? Array.Empty<byte>();
            int frameLength = 4 + 1 + 8 + 4 + payload.Length;
            Span<byte> buffer = writer.GetSpan(4 + frameLength);

            BinaryPrimitives.WriteInt32LittleEndian(buffer[..4], frameLength);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(4, 4), snap.PacketId);
            buffer[8] = (byte)snap.Direction;
            BinaryPrimitives.WriteInt64LittleEndian(buffer.Slice(9, 8), snap.Timestamp);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(17, 4), payload.Length);
            payload.CopyTo(buffer[21..]);

            writer.Advance(4 + frameLength);
            FlushResult flush = await writer.FlushAsync();

            if (flush.IsCompleted || flush.IsCanceled)
                break;
        }
    }
}
