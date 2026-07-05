using System.Collections.ObjectModel;
using System.Buffers.Binary;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DaemonCapture.Telemetry;
using DaemonMC.Network;

namespace DaemonCapture;

public partial class MainWindow : Window
{
    private const int MaxVisiblePackets = 20;
    private const int MaxBatchSize = 256;

    private readonly ObservableCollection<PacketSnapshotViewModel> _packets = new();
    private readonly ObservableCollection<PacketStatViewModel> _packetStats = new();
    private readonly ObservableCollection<BlockedPacketViewModel> _blockedPackets = new();
    private readonly Dictionary<string, PacketStatViewModel> _statsByPacket = new();
    private readonly HashSet<int> _blockedPacketIds = new();
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private PacketTelemetryServer? _packetTelemetryServer;
    private bool _isInitialized;
    private volatile bool _isFrozen;
    private int _nextIndex = 1;

    public MainWindow()
    {
        InitializeComponent();
        PacketsView = CollectionViewSource.GetDefaultView(_packets);
        PacketsView.Filter = FilterPacket;
        DataContext = this;
        _isInitialized = true;
    }

    public ICollectionView PacketsView { get; }

    private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        PacketStatsList.ItemsSource = _packetStats;
        BlockedPacketsList.ItemsSource = _blockedPackets;

        _packetTelemetryServer = new PacketTelemetryServer();
        _packetTelemetryServer.ConnectionChanged += connected =>
        {
            Dispatcher.Invoke(() =>
            {
                StatusDot.Fill = connected ? new SolidColorBrush(Color.FromRgb(39, 132, 83)) : new SolidColorBrush(Color.FromRgb(183, 58, 58));
                StatusText.Text = connected ? "DaemonMC verbunden" : "Pipe wartet";
            });
        };

        _packetTelemetryServer.Listener(_cancellationTokenSource.Token);
        _ = ReadSnapshotsAsync(_packetTelemetryServer, _cancellationTokenSource.Token);
    }

    private async Task ReadSnapshotsAsync(PacketTelemetryServer server, CancellationToken cancellationToken)
    {
        List<PacketSnapshot> batch = new(MaxBatchSize);

        while (await server.Snapshots.WaitToReadAsync(cancellationToken))
        {
            batch.Clear();

            while (batch.Count < MaxBatchSize && server.Snapshots.TryRead(out PacketSnapshot? snapshot))
            {
                batch.Add(snapshot);
            }

            if (batch.Count == 0 || _isFrozen)
                continue;

            PacketSnapshot[] snapshotBatch = batch.ToArray();
            await Dispatcher.InvokeAsync(() => AddSnapshots(snapshotBatch), System.Windows.Threading.DispatcherPriority.Background, cancellationToken);
        }
    }

    private void AddSnapshots(IReadOnlyList<PacketSnapshot> snapshots)
    {
        if (_isFrozen)
            return;

        Queue<PendingPacket> latestPackets = new(MaxVisiblePackets);

        foreach (PacketSnapshot snapshot in snapshots)
        {
            if (_blockedPacketIds.Contains(snapshot.PacketId))
                continue;

            int index = _nextIndex++;
            UpdateStat(snapshot.PacketId);

            if (latestPackets.Count == MaxVisiblePackets)
                latestPackets.Dequeue();

            latestPackets.Enqueue(new PendingPacket(index, snapshot));
        }

        if (latestPackets.Count == 0)
            return;

        PacketSnapshotViewModel? latestModel = null;

        if (latestPackets.Count == MaxVisiblePackets)
        {
            _packets.Clear();
        }

        foreach (PendingPacket pendingPacket in latestPackets)
        {
            PacketSnapshotViewModel model = new(pendingPacket.Index, pendingPacket.Snapshot);
            _packets.Add(model);
            latestModel = model;
        }

        while (_packets.Count > MaxVisiblePackets)
        {
            _packets.RemoveAt(0);
        }

        TotalPacketsText.Text = _packets.Count.ToString();

        if (latestModel != null && IsPacketVisible(latestModel))
            ScrollToLatestVisiblePacket(latestModel);

        UpdateFooter();
    }

    private void UpdateStat(int packetId)
    {
        string packetName = PacketSnapshotViewModel.ResolvePacketName(packetId);
        string statKey = $"{packetName} ({packetId})";

        if (!_statsByPacket.TryGetValue(statKey, out PacketStatViewModel? stat))
        {
            stat = new PacketStatViewModel(statKey);
            _statsByPacket.Add(statKey, stat);
            _packetStats.Add(stat);
        }

        stat.Increment();
    }

    private void Freeze_OnChanged(object sender, RoutedEventArgs e)
    {
        _isFrozen = FreezeCheckBox.IsChecked == true;
    }

    private void BlockSelectedPacket_OnClick(object sender, RoutedEventArgs e)
    {
        if (PacketGrid.SelectedItem is not PacketSnapshotViewModel packet)
            return;

        AddBlockedPacket(packet.PacketId, packet.PacketName);
    }

    private void AddBlockedPacketId_OnClick(object sender, RoutedEventArgs e)
    {
        AddBlockedPacketFromInput();
    }

    private void BlockPacketIdBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        AddBlockedPacketFromInput();
    }

    private void RemoveBlockedPacket_OnClick(object sender, RoutedEventArgs e)
    {
        if (BlockedPacketsList.SelectedItem is not BlockedPacketViewModel blockedPacket)
            return;

        _blockedPacketIds.Remove(blockedPacket.PacketId);
        _blockedPackets.Remove(blockedPacket);
    }

    private void ClearBlockedPackets_OnClick(object sender, RoutedEventArgs e)
    {
        _blockedPacketIds.Clear();
        _blockedPackets.Clear();
    }

    private void AddBlockedPacketFromInput()
    {
        string input = BlockPacketIdBox.Text.Trim();
        if (!TryParsePacketId(input, out int packetId))
        {
            BlockPacketIdBox.SelectAll();
            return;
        }

        AddBlockedPacket(packetId, PacketSnapshotViewModel.ResolvePacketName(packetId));
        BlockPacketIdBox.Clear();
    }

    private void AddBlockedPacket(int packetId, string packetName)
    {
        if (!_blockedPacketIds.Add(packetId))
            return;

        _blockedPackets.Add(new BlockedPacketViewModel(packetId, packetName));

        for (int i = _packets.Count - 1; i >= 0; i--)
        {
            if (_packets[i].PacketId == packetId)
                _packets.RemoveAt(i);
        }

        if (PacketGrid.SelectedItem is PacketSnapshotViewModel selectedPacket && selectedPacket.PacketId == packetId)
            PacketGrid.SelectedItem = null;

        TotalPacketsText.Text = _packets.Count.ToString();
        UpdateFooter();
    }

    private static bool TryParsePacketId(string input, out int packetId)
    {
        packetId = 0;

        if (string.IsNullOrWhiteSpace(input))
            return false;

        if (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(input[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out packetId);

        return int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out packetId);
    }

    private bool FilterPacket(object item)
    {
        if (item is not PacketSnapshotViewModel packet)
            return false;

        if (DirectionFilter?.SelectedItem is ComboBoxItem directionItem && directionItem.Content?.ToString() is string direction && direction != "Alle Richtungen")
        {
            if (!string.Equals(packet.Direction, direction, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        string query = SearchBox?.Text?.Trim() ?? string.Empty;
        if (query.Length == 0)
            return true;

        return packet.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void Filter_OnChanged(object sender, EventArgs e)
    {
        if (!_isInitialized)
            return;

        PacketsView.Refresh();
        UpdateFooter();
    }

    private void PacketGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PacketGrid.SelectedItem is not PacketSnapshotViewModel packet)
        {
            SelectedPacketText.Text = "-";
            DetailTitle.Text = "Kein Packet ausgewaehlt";
            DetailSubtitle.Text = "-";
            SummaryText.Text = string.Empty;
            HexText.Text = string.Empty;
            TraceList.ItemsSource = null;
            TraceValueText.Text = string.Empty;
            return;
        }

        if (!_isFrozen)
        {
            _isFrozen = true;
            FreezeCheckBox.IsChecked = true;
        }

        SelectedPacketText.Text = $"#{packet.Index}";
        DetailTitle.Text = packet.PacketName;
        DetailSubtitle.Text = $"{packet.Direction} | {packet.LocalTime} | {packet.Size:N0} bytes";
        SummaryText.Text = packet.Summary;
        HexText.Text = packet.HexDump;
        TraceList.ItemsSource = packet.TraceRows;
        TraceValueText.Text = string.Empty;
    }

    private void Clear_OnClick(object sender, RoutedEventArgs e)
    {
        _packets.Clear();
        _packetStats.Clear();
        _statsByPacket.Clear();
        _nextIndex = 1;
        TotalPacketsText.Text = "0";
        PacketGrid.SelectedItem = null;
        UpdateFooter();
    }

    private void TraceList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        TraceValueText.Text = TraceList.SelectedItem is PacketTraceRowViewModel row ? row.Value : string.Empty;
    }

    private void UpdateFooter()
    {
        FooterText.Text = $"{PacketsView.Cast<object>().Count():N0} sichtbar";
    }

    private void ScrollToLatestVisiblePacket(PacketSnapshotViewModel latestPacket)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (IsPacketVisible(latestPacket))
                PacketGrid.ScrollIntoView(latestPacket);
        }, DispatcherPriority.Background);
    }

    private bool IsPacketVisible(PacketSnapshotViewModel packet)
    {
        return FilterPacket(packet);
    }

    private void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        _cancellationTokenSource.Cancel();
        _packetTelemetryServer?.Stop();
    }
}

public sealed class PacketSnapshotViewModel
{
    private const int MaxHexDumpBytes = 4096;

    public PacketSnapshotViewModel(int index, PacketSnapshot snapshot)
    {
        Index = index;
        PacketId = snapshot.PacketId;
        PacketName = ResolvePacketName(snapshot.PacketId);
        Direction = snapshot.Direction.ToString();
        Timestamp = snapshot.Timestamp;
        Buffer = snapshot.Buffer ?? Array.Empty<byte>();
        TraceOperations = snapshot.TraceOperations;
    }

    public int Index { get; }

    public int PacketId { get; }

    public string PacketName { get; }

    public string Direction { get; }

    public long Timestamp { get; }

    public byte[] Buffer { get; }

    public IReadOnlyList<PacketTraceOperation> TraceOperations { get; }

    public int Size => Buffer.Length;

    public string LocalTime
    {
        get
        {
            if (Timestamp <= 0)
                return "-";

            return DateTimeOffset.FromUnixTimeMilliseconds(Timestamp).LocalDateTime.ToString("HH:mm:ss.fff");
        }
    }

    public string HexPreview => Buffer.Length == 0 ? "-" : ToHex(Buffer, Math.Min(Buffer.Length, 16)).Replace(Environment.NewLine, " ");

    public string HexDump => Buffer.Length == 0 ? "No payload captured." : BuildHexDump(Buffer, MaxHexDumpBytes);

    public IReadOnlyList<PacketTraceRowViewModel> TraceRows => BuildTraceRows(Buffer, TraceOperations);

    public string SearchText => $"{Index} {PacketId} {PacketName} {Direction} {Size} {HexPreview}";

    public string Summary =>
        $"Index:      {Index}{Environment.NewLine}" +
        $"Time:       {LocalTime}{Environment.NewLine}" +
        $"Timestamp:  {Timestamp}{Environment.NewLine}" +
        $"Direction:  {Direction}{Environment.NewLine}" +
        $"Packet ID:  {PacketId}{Environment.NewLine}" +
        $"Name:       {PacketName}{Environment.NewLine}" +
        $"Payload:    {Size:N0} bytes";

    public static string ResolvePacketName(int packetId)
    {
        return Enum.IsDefined(typeof(Info.Bedrock), packetId)
            ? Enum.GetName(typeof(Info.Bedrock), packetId) ?? $"Unknown_{packetId}"
            : $"Unknown_{packetId}";
    }

    private static string BuildHexDump(byte[] data, int maxBytes)
    {
        StringBuilder builder = new();
        int byteCount = Math.Min(data.Length, maxBytes);

        for (int offset = 0; offset < byteCount; offset += 16)
        {
            int count = Math.Min(16, byteCount - offset);
            builder.Append(offset.ToString("X8"));
            builder.Append("  ");

            for (int i = 0; i < 16; i++)
            {
                if (i < count)
                    builder.Append(data[offset + i].ToString("X2"));
                else
                    builder.Append("  ");

                builder.Append(i == 7 ? "  " : " ");
            }

            builder.Append(' ');

            for (int i = 0; i < count; i++)
            {
                byte value = data[offset + i];
                builder.Append(value >= 32 && value <= 126 ? (char)value : '.');
            }

            builder.AppendLine();
        }

        if (data.Length > byteCount)
            builder.AppendLine($"... truncated, showing {byteCount:N0} of {data.Length:N0} bytes");

        return builder.ToString();
    }

    private static IReadOnlyList<PacketTraceRowViewModel> BuildTraceRows(byte[] data, IReadOnlyList<PacketTraceOperation> operations)
    {
        if (operations.Count == 0)
            return new[] { new PacketTraceRowViewModel("-", "No trace captured.", string.Empty, string.Empty) };

        List<PacketTraceRowViewModel> rows = new(operations.Count);

        foreach (PacketTraceOperation operation in operations)
        {
            int start = Math.Max(operation.Offset, 0);
            int length = Math.Max(operation.Length, 0);
            int end = length == 0 ? start : start + length - 1;
            string bytes = start < data.Length && length > 0
                ? ToHex(data.Skip(start).Take(Math.Min(length, data.Length - start)).ToArray(), length)
                : string.Empty;

            rows.Add(new PacketTraceRowViewModel(
                $"{start:X4}-{end:X4}",
                operation.Operation,
                bytes,
                DecodeTraceValue(operation.Operation, data.Skip(start).Take(Math.Min(length, Math.Max(data.Length - start, 0))).ToArray())));
        }

        return rows;
    }

    private static string DecodeTraceValue(string operation, byte[] bytes)
    {
        try
        {
            string value = operation switch
            {
                "ReadBool" or "WriteBool" when bytes.Length >= 1 => bytes[0] == 1 ? "true" : "false",
                "ReadByte" or "WriteByte" when bytes.Length >= 1 => bytes[0].ToString(CultureInfo.InvariantCulture),
                "ReadInt" or "WriteInt" when bytes.Length >= 4 => BinaryPrimitives.ReadInt32LittleEndian(bytes).ToString(CultureInfo.InvariantCulture),
                "ReadIntBE" or "WriteIntBE" when bytes.Length >= 4 => BinaryPrimitives.ReadInt32BigEndian(bytes).ToString(CultureInfo.InvariantCulture),
                "ReadFloat" or "WriteFloat" when bytes.Length >= 4 => BitConverter.ToSingle(bytes, 0).ToString("R", CultureInfo.InvariantCulture),
                "ReadShort" or "WriteShort" when bytes.Length >= 2 => BinaryPrimitives.ReadInt16LittleEndian(bytes).ToString(CultureInfo.InvariantCulture),
                "ReadShortBE" or "ReadSignedShort" or "WriteShortBE" when bytes.Length >= 2 => BinaryPrimitives.ReadInt16BigEndian(bytes).ToString(CultureInfo.InvariantCulture),
                "ReadLong" or "WriteLong" when bytes.Length >= 8 => BinaryPrimitives.ReadInt64LittleEndian(bytes).ToString(CultureInfo.InvariantCulture),
                "ReadLongLE" or "WriteLongLE" when bytes.Length >= 8 => BinaryPrimitives.ReadInt64BigEndian(bytes).ToString(CultureInfo.InvariantCulture),
                "ReadUInt24LE" or "WriteUInt24LE" when bytes.Length >= 3 => (bytes[0] | (bytes[1] << 8) | (bytes[2] << 16)).ToString(CultureInfo.InvariantCulture),
                "ReadVarInt" or "WriteVarInt" or "WriteVarInt_Signed" => ReadVarInt(bytes).ToString(CultureInfo.InvariantCulture),
                "ReadSignedVarInt" or "WriteSignedVarInt" => DecodeSignedVarInt(ReadVarInt(bytes)).ToString(CultureInfo.InvariantCulture),
                "ReadVarLong" or "WriteVarLong" => ReadVarLong(bytes).ToString(CultureInfo.InvariantCulture),
                "ReadSignedVarLong" or "WriteSignedVarLong" => DecodeSignedVarLong(ReadVarLong(bytes)).ToString(CultureInfo.InvariantCulture),
                "ReadString" or "WriteString" => DecodeString(bytes),
                "ReadRakString" or "WriteRakString" => DecodeRakString(bytes),
                "ReadMagic" or "WriteMagic" => ToHex(bytes, bytes.Length),
                "ReadBytes" or "WriteBytes" => $"{bytes.Length:N0} bytes",
                "ReadMTU" or "WriteMTU" => $"{bytes.Length:N0} padding bytes",
                "ReadAddress" or "WriteAddress" => DecodeAddress(bytes),
                _ => $"{bytes.Length:N0} bytes"
            };

            return $"Value: {value}";
        }
        catch (Exception ex)
        {
            return $"Value could not be decoded as {operation}: {ex.Message}";
        }
    }

    private static int ReadVarInt(byte[] bytes)
    {
        int value = 0;
        int size = 0;

        foreach (byte currentByte in bytes)
        {
            value |= (currentByte & 0x7F) << (size * 7);
            if ((currentByte & 0x80) == 0)
                return value;

            size++;
        }

        throw new InvalidDataException("Incomplete VarInt.");
    }

    private static long ReadVarLong(byte[] bytes)
    {
        long value = 0;
        int size = 0;

        foreach (byte currentByte in bytes)
        {
            value |= (long)(currentByte & 0x7F) << (size * 7);
            if ((currentByte & 0x80) == 0)
                return value;

            size++;
        }

        throw new InvalidDataException("Incomplete VarLong.");
    }

    private static int DecodeSignedVarInt(int value)
    {
        return (value >> 1) ^ -(value & 1);
    }

    private static long DecodeSignedVarLong(long value)
    {
        return (value >> 1) ^ -(value & 1);
    }

    private static string DecodeString(byte[] bytes)
    {
        int length = ReadVarInt(bytes);
        int prefixLength = GetVarIntLength(bytes);

        if (length < 0 || prefixLength + length > bytes.Length)
            throw new InvalidDataException($"Invalid string length {length}.");

        return Encoding.UTF8.GetString(bytes, prefixLength, length);
    }

    private static string DecodeRakString(byte[] bytes)
    {
        if (bytes.Length < 2)
            throw new InvalidDataException("RakString is shorter than its length prefix.");

        int length = BinaryPrimitives.ReadUInt16BigEndian(bytes);
        if (2 + length > bytes.Length)
            throw new InvalidDataException($"Invalid RakString length {length}.");

        return Encoding.UTF8.GetString(bytes, 2, length);
    }

    private static int GetVarIntLength(byte[] bytes)
    {
        for (int i = 0; i < bytes.Length; i++)
        {
            if ((bytes[i] & 0x80) == 0)
                return i + 1;
        }

        throw new InvalidDataException("Incomplete VarInt.");
    }

    private static string DecodeAddress(byte[] bytes)
    {
        if (bytes.Length >= 7 && bytes[0] == 4)
        {
            string address = $"{bytes[1]}.{bytes[2]}.{bytes[3]}.{bytes[4]}";
            ushort port = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(5, 2));
            return $"{address}:{port}";
        }

        return $"{bytes.Length:N0} address bytes";
    }

    private static string ToHex(byte[] data, int count)
    {
        StringBuilder builder = new();
        for (int i = 0; i < count; i++)
        {
            if (i > 0)
                builder.Append(' ');

            builder.Append(data[i].ToString("X2"));
        }

        if (data.Length > count)
            builder.Append(" ...");

        return builder.ToString();
    }
}

public sealed class PacketStatViewModel : INotifyPropertyChanged
{
    private int _count;

    public PacketStatViewModel(string name)
    {
        Name = name;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; }

    public int Count
    {
        get => _count;
        private set
        {
            if (_count == value)
                return;

            _count = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
        }
    }

    public void Increment()
    {
        Count++;
    }
}

public sealed class BlockedPacketViewModel
{
    public BlockedPacketViewModel(int packetId, string packetName)
    {
        PacketId = packetId;
        Name = packetName;
    }

    public int PacketId { get; }

    public string Name { get; }
}

internal readonly record struct PendingPacket(int Index, PacketSnapshot Snapshot);

public sealed record PacketTraceRowViewModel(string Range, string Operation, string Hex, string Value);
