using System.Buffers;
using System.IO.Pipelines;
using System.IO.Pipes;
using System.Threading.Channels;
using PipeOptions = System.IO.Pipes.PipeOptions;

namespace DaemonCapture.Telemetry;

public class PacketTelemetryServer
{
    public const string PipeName = "DaemonMC_PacketTelemetryPipe";
    
    private readonly Channel<PacketSnapshot> _channel;

    public PacketTelemetryServer()
    {
        _channel = Channel.CreateBounded<PacketSnapshot>(new BoundedChannelOptions(32768)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    public async Task Listener()
    {
        while (true)
        {
            await using var pipe = new NamedPipeServerStream(pipeName: PipeName, PipeDirection.In,
                maxNumberOfServerInstances: 1, PipeTransmissionMode.Byte,
                options: PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            
            await pipe.WaitForConnectionAsync();
            
            PipeReader reader = PipeReader.Create(pipe, new StreamPipeReaderOptions(leaveOpen: true));

            while (true)
            {
                ReadResult result = await reader.ReadAsync();
                ReadOnlySequence<byte> buffer = result.Buffer;

                SequenceReader<byte> seq = new(buffer);

                while (seq.Remaining >= 6)
                {
                    seq.TryRead(out byte packetType);
                    seq.TryReadLittleEndian(out int packetId);
                    seq.TryRead(out byte direction);

                    Console.WriteLine($"Received: {(PacketType)packetType}, {packetId}, {(PacketDirection)direction}");
                }

                reader.AdvanceTo(seq.Position, buffer.End);

                if (result.IsCompleted)
                    break;
            }
            

            /*
            SequenceReader<byte> readerSeq = new SequenceReader<byte>(buffer);
            readerSeq.TryRead(out byte packetType);
            readerSeq.TryReadLittleEndian(out int packetId);
            readerSeq.TryRead(out byte direction);
            
            var snapshot = new PacketSnapshot
            {
                PacketType = (PacketType)packetType,
                PacketId = packetId,
                Direction = (PacketDirection)direction
            };
            
            Console.WriteLine($"[{DateTime.Now}] Received packet snapshot: Type={snapshot.PacketType}, Id={snapshot.PacketId}, Direction={snapshot.Direction}");
        */
        }
    }
}