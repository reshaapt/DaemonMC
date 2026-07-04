using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.IO.Pipes;
using System.Threading.Channels;
using PipeOptions = System.IO.Pipes.PipeOptions;

namespace DaemonCapture.Telemetry;

public class PacketTelemetryServer
{
    public const string PipeName = "DaemonMC_PacketTelemetryPipe";

    private readonly CancellationTokenSource _cancellationTokenSource = new();
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

    public ChannelReader<PacketSnapshot> Snapshots => _channel.Reader;

    public event Action<bool>? ConnectionChanged;

    public Task Listener(CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ListenAsync(cancellationToken), cancellationToken);
    }

    public void Stop()
    {
        _cancellationTokenSource.Cancel();
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource linkedTokenSource =
            CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, cancellationToken);

        while (!linkedTokenSource.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(pipeName: PipeName, PipeDirection.In,
                maxNumberOfServerInstances: 1, PipeTransmissionMode.Byte,
                options: PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

            await pipe.WaitForConnectionAsync(linkedTokenSource.Token);
            ConnectionChanged?.Invoke(true);

            PipeReader reader = PipeReader.Create(pipe, new StreamPipeReaderOptions(leaveOpen: true));

            try
            {
                while (!linkedTokenSource.IsCancellationRequested)
                {
                    ReadResult result = await reader.ReadAsync(linkedTokenSource.Token);
                    ReadOnlySequence<byte> buffer = result.Buffer;

                    SequencePosition consumed = buffer.Start;
                    SequencePosition examined = buffer.End;

                    while (TryReadSnapshot(buffer.Slice(consumed), out PacketSnapshot? snapshot, out SequencePosition next))
                    {
                        consumed = next;
                        await _channel.Writer.WriteAsync(snapshot!, linkedTokenSource.Token);
                    }

                    reader.AdvanceTo(consumed, examined);

                    if (result.IsCompleted)
                        break;
                }
            }
            finally
            {
                ConnectionChanged?.Invoke(false);
            }
        }
    }

    private static bool TryReadSnapshot(ReadOnlySequence<byte> buffer, out PacketSnapshot? snapshot, out SequencePosition next)
    {
        snapshot = null;
        next = buffer.Start;

        if (buffer.Length < 4)
            return false;

        SequenceReader<byte> reader = new(buffer);
        if (!reader.TryReadLittleEndian(out int frameLength))
            return false;

        if (frameLength < 17)
            throw new InvalidDataException($"Invalid telemetry frame length {frameLength}.");

        if (reader.Remaining < frameLength)
            return false;

        ReadOnlySequence<byte> frame = buffer.Slice(reader.Position, frameLength);
        SequenceReader<byte> frameReader = new(frame);

        frameReader.TryReadLittleEndian(out int packetId);
        frameReader.TryRead(out byte direction);
        frameReader.TryReadLittleEndian(out long timestamp);
        frameReader.TryReadLittleEndian(out int payloadLength);

        if (payloadLength < 0 || frameReader.Remaining < payloadLength)
            throw new InvalidDataException($"Invalid telemetry payload length {payloadLength}.");

        byte[] payload = Array.Empty<byte>();
        if (payloadLength > 0)
            payload = frame.Slice(frameLength - payloadLength, payloadLength).ToArray();

        snapshot = new PacketSnapshot
        {
            PacketType = PacketType.Bedrock,
            PacketId = packetId,
            Direction = (PacketDirection)direction,
            Timestamp = timestamp,
            Buffer = payload
        };

        next = buffer.GetPosition(4 + frameLength);
        return true;
    }
}
