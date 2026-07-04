using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using DaemonCapture.Telemetry;
using DaemonMC.Network;

namespace DaemonCapture;

public partial class MainWindow : Window
{
    private const int MaxVisiblePackets = 20;

    private readonly ObservableCollection<PacketSnapshotViewModel> _packets = new();
    private readonly ObservableCollection<PacketStatViewModel> _packetStats = new();
    private readonly Dictionary<string, PacketStatViewModel> _statsByPacket = new();
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private PacketTelemetryServer? _packetTelemetryServer;
    private bool _isInitialized;
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
        await foreach (PacketSnapshot snapshot in server.Snapshots.ReadAllAsync(cancellationToken))
        {
            await Dispatcher.InvokeAsync(() => AddSnapshot(snapshot), System.Windows.Threading.DispatcherPriority.Background, cancellationToken);
        }
    }

    private void AddSnapshot(PacketSnapshot snapshot)
    {
        PacketSnapshotViewModel model = new(_nextIndex++, snapshot);
        _packets.Add(model);

        while (_packets.Count > MaxVisiblePackets)
        {
            _packets.RemoveAt(0);
        }

        TotalPacketsText.Text = _packets.Count.ToString();

        string statKey = $"{model.PacketName} ({model.PacketId})";
        if (!_statsByPacket.TryGetValue(statKey, out PacketStatViewModel? stat))
        {
            stat = new PacketStatViewModel(statKey);
            _statsByPacket.Add(statKey, stat);
            _packetStats.Add(stat);
        }

        stat.Count++;
        PacketStatsList.Items.Refresh();

        PacketsView.Refresh();
        ScrollToLatestVisiblePacket(model);
        UpdateFooter();
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
            return;
        }

        SelectedPacketText.Text = $"#{packet.Index}";
        DetailTitle.Text = packet.PacketName;
        DetailSubtitle.Text = $"{packet.Direction} | {packet.LocalTime} | {packet.Size:N0} bytes";
        SummaryText.Text = packet.Summary;
        HexText.Text = packet.HexDump;
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

    private void UpdateFooter()
    {
        FooterText.Text = $"{PacketsView.Cast<object>().Count():N0} sichtbar";
    }

    private void ScrollToLatestVisiblePacket(PacketSnapshotViewModel latestPacket)
    {
        if (!PacketsView.Contains(latestPacket))
            return;

        PacketGrid.UpdateLayout();
        PacketGrid.ScrollIntoView(latestPacket);
    }

    private void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        _cancellationTokenSource.Cancel();
        _packetTelemetryServer?.Stop();
    }
}

public sealed class PacketSnapshotViewModel
{
    public PacketSnapshotViewModel(int index, PacketSnapshot snapshot)
    {
        Index = index;
        PacketId = snapshot.PacketId;
        PacketName = ResolvePacketName(snapshot.PacketId);
        Direction = snapshot.Direction.ToString();
        Timestamp = snapshot.Timestamp;
        Buffer = snapshot.Buffer ?? Array.Empty<byte>();
    }

    public int Index { get; }

    public int PacketId { get; }

    public string PacketName { get; }

    public string Direction { get; }

    public long Timestamp { get; }

    public byte[] Buffer { get; }

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

    public string HexDump => Buffer.Length == 0 ? "No payload captured." : BuildHexDump(Buffer);

    public string SearchText => $"{Index} {PacketId} {PacketName} {Direction} {Size} {HexPreview}";

    public string Summary =>
        $"Index:      {Index}{Environment.NewLine}" +
        $"Time:       {LocalTime}{Environment.NewLine}" +
        $"Timestamp:  {Timestamp}{Environment.NewLine}" +
        $"Direction:  {Direction}{Environment.NewLine}" +
        $"Packet ID:  {PacketId}{Environment.NewLine}" +
        $"Name:       {PacketName}{Environment.NewLine}" +
        $"Payload:    {Size:N0} bytes";

    private static string ResolvePacketName(int packetId)
    {
        return Enum.IsDefined(typeof(Info.Bedrock), packetId)
            ? Enum.GetName(typeof(Info.Bedrock), packetId) ?? $"Unknown_{packetId}"
            : $"Unknown_{packetId}";
    }

    private static string BuildHexDump(byte[] data)
    {
        StringBuilder builder = new();

        for (int offset = 0; offset < data.Length; offset += 16)
        {
            int count = Math.Min(16, data.Length - offset);
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

        return builder.ToString();
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

public sealed class PacketStatViewModel
{
    public PacketStatViewModel(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public int Count { get; set; }
}
