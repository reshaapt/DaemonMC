using System.Buffers.Binary;
using System.IO.Pipelines;
using System.IO.Pipes;
using System.Text;
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
            List<TraceOperationPayload> traceOperations = BuildTracePayload(snap.TraceOperations);
            int traceLength = 4 + traceOperations.Sum(operation => 16 + operation.OperationBytes.Length + operation.PropertyBytes.Length);
            int frameLength = 4 + 1 + 8 + 4 + payload.Length + traceLength;
            Span<byte> buffer = writer.GetSpan(4 + frameLength);

            BinaryPrimitives.WriteInt32LittleEndian(buffer[..4], frameLength);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(4, 4), snap.PacketId);
            buffer[8] = (byte)snap.Direction;
            BinaryPrimitives.WriteInt64LittleEndian(buffer.Slice(9, 8), snap.Timestamp);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(17, 4), payload.Length);
            payload.CopyTo(buffer[21..]);

            int offset = 21 + payload.Length;
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(offset, 4), traceOperations.Count);
            offset += 4;

            foreach (TraceOperationPayload operation in traceOperations)
            {
                BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(offset, 4), operation.Offset);
                BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(offset + 4, 4), operation.Length);
                BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(offset + 8, 4), operation.OperationBytes.Length);
                operation.OperationBytes.CopyTo(buffer.Slice(offset + 12));
                offset += 12 + operation.OperationBytes.Length;
                BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(offset, 4), operation.PropertyBytes.Length);
                operation.PropertyBytes.CopyTo(buffer.Slice(offset + 4));
                offset += 4 + operation.PropertyBytes.Length;
            }

            writer.Advance(4 + frameLength);
            FlushResult flush = await writer.FlushAsync();

            if (flush.IsCompleted || flush.IsCanceled)
                break;
        }
    }

    private static List<TraceOperationPayload> BuildTracePayload(IReadOnlyList<PacketTraceOperation> traceOperations)
    {
        List<TraceOperationPayload> result = new(traceOperations.Count);

        foreach (PacketTraceOperation operation in traceOperations)
        {
            byte[] operationBytes = Encoding.UTF8.GetBytes(operation.Operation);
            byte[] propertyBytes = Encoding.UTF8.GetBytes(operation.Property);
            result.Add(new TraceOperationPayload(operation.Operation, operation.Offset, operation.Length, operationBytes, propertyBytes));
        }

        return result;
    }

    private readonly record struct TraceOperationPayload(string Operation, int Offset, int Length, byte[] OperationBytes, byte[] PropertyBytes);
}
