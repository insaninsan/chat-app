using System.Collections.ObjectModel;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ChatApp.Server.Models;
using ChatApp.Server.Services;
using ChatApp.Shared;

namespace ChatApp.Server;

/// <summary>
/// Sunucu penceresi: sunucuyu başlatır/durdurur, bağlı istemcileri ve sunucu kayıtlarını gösterir.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly IBrush RunningBrush = new SolidColorBrush(Color.FromRgb(22, 163, 74));
    private static readonly IBrush RunningBackground = new SolidColorBrush(Color.FromRgb(220, 252, 231));
    private static readonly IBrush StoppedBrush = new SolidColorBrush(Color.FromRgb(107, 114, 128));
    private static readonly IBrush StoppedBackground = new SolidColorBrush(Color.FromRgb(229, 231, 235));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.FromRgb(185, 28, 28));

    private readonly ChatServerHost server = new();
    private readonly ObservableCollection<ConnectedClient> clients = new();
    private readonly ObservableCollection<string> logLines = new();

    public MainWindow()
    {
        InitializeComponent();

        ClientsList.ItemsSource = clients;
        LogList.ItemsSource = logLines;
        PortBox.Text = ChatDefaults.DefaultPort.ToString();

        // Sunucu olayları arka plandaki görevlerden gelebilir; arayüz yalnızca
        // UI iş parçacığından güncellenebildiği için Dispatcher.UIThread.Post kullanılır.
        server.Log += (_, text) => Dispatcher.UIThread.Post(() => AddLog(text));
        server.ClientsChanged += (_, _) => Dispatcher.UIThread.Post(RefreshClients);

        Closing += (_, _) => _ = server.StopAsync();

        UpdateServerUi();
        RefreshClients();
        SetStatus($"Sunucuyu başlatmak için ▶ Sunucuyu Başlat'a basın. Bu bilgisayarın adresleri: {GetLocalAddresses()}");
    }

    // ===================== Butonlar =====================

    private void StartButton_Click(object? sender, RoutedEventArgs e)
    {
        if (!ChatDefaults.TryParsePort(PortBox.Text, out int port))
        {
            SetStatus("Geçerli bir port girin (1-65535).", isError: true);
            return;
        }

        try
        {
            server.Start(port);
            SetStatus($"Sunucu çalışıyor. İstemciler şu adreslerden biriyle bağlanabilir: " +
                      $"{GetLocalAddresses()} — port {port}");
        }
        catch (SocketException ex)
        {
            SetStatus($"Sunucu başlatılamadı ({port} portu kullanımda olabilir): {ex.Message}", isError: true);
        }

        UpdateServerUi();
    }

    private async void StopButton_Click(object? sender, RoutedEventArgs e)
    {
        StopButton.IsEnabled = false;
        await server.StopAsync();
        UpdateServerUi();
        SetStatus("Sunucu durduruldu.");
    }

    private async void KickButton_Click(object? sender, RoutedEventArgs e)
    {
        if (ClientsList.SelectedItem is not ConnectedClient client)
            return;

        KickButton.IsEnabled = false;
        await server.KickAsync(client);
        SetStatus($"{client.Username} ({client.RemoteEndPoint}) sohbetten çıkarıldı.");
    }

    private void ClientsList_SelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        KickButton.IsEnabled = ClientsList.SelectedItem is ConnectedClient;

    // ===================== Arayüz güncelleme =====================

    private void AddLog(string text)
    {
        logLines.Add($"[{DateTime.Now:HH:mm:ss}] {text}");
        LogList.ScrollIntoView(logLines[^1]);
    }

    private void RefreshClients()
    {
        clients.Clear();
        foreach (ConnectedClient client in server.GetClients().OrderBy(c => c.ConnectedAt))
            clients.Add(client);

        ClientsHeader.Text = $"Bağlı istemciler ({clients.Count})";
        KickButton.IsEnabled = ClientsList.SelectedItem is ConnectedClient;
    }

    private void UpdateServerUi()
    {
        bool running = server.IsRunning;

        StartButton.IsEnabled = !running;
        StopButton.IsEnabled = running;
        PortBox.IsEnabled = !running;

        IndicatorDot.Fill = running ? RunningBrush : StoppedBrush;
        IndicatorText.Foreground = running ? RunningBrush : StoppedBrush;
        IndicatorBorder.Background = running ? RunningBackground : StoppedBackground;
        IndicatorText.Text = running ? $"ÇALIŞIYOR · port {server.Port}" : "DURDURULDU";
    }

    private void SetStatus(string message, bool isError = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = isError ? ErrorBrush : Brushes.Black;
    }

    /// <summary>Bu bilgisayarın yerel ağdaki IPv4 adresleri (istemcilerin bağlanması için).</summary>
    private static string GetLocalAddresses()
    {
        try
        {
            var addresses = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.Address.ToString())
                .Distinct()
                .OrderBy(a => a == "127.0.0.1" ? 0 : 1)
                .ToList();

            return addresses.Count > 0 ? string.Join(", ", addresses) : "127.0.0.1";
        }
        catch (NetworkInformationException)
        {
            return "127.0.0.1";
        }
    }
}
