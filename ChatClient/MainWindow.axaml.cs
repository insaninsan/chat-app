using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ChatApp.Client.Models;
using ChatApp.Client.Services;
using ChatApp.Shared;

namespace ChatApp.Client;

/// <summary>
/// İstemci penceresi: sunucuya bağlanma/bağlantıyı kesme, mesaj gönderme,
/// gelen mesajları ve çevrimiçi kullanıcıları gösterme.
/// </summary>
public partial class MainWindow : Window
{
    private const string EveryoneLabel = "Herkes";

    private static readonly IBrush ConnectedBrush = new SolidColorBrush(Color.FromRgb(22, 163, 74));
    private static readonly IBrush ConnectedBackground = new SolidColorBrush(Color.FromRgb(220, 252, 231));
    private static readonly IBrush DisconnectedBrush = new SolidColorBrush(Color.FromRgb(107, 114, 128));
    private static readonly IBrush DisconnectedBackground = new SolidColorBrush(Color.FromRgb(229, 231, 235));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.FromRgb(185, 28, 28));

    private readonly ChatClientService chat = new();
    private readonly ObservableCollection<ChatLine> lines = new();
    private readonly ObservableCollection<string> users = new();
    private readonly ObservableCollection<string> recipients = new() { EveryoneLabel };

    private bool isConnecting;

    public MainWindow()
    {
        InitializeComponent();

        MessagesList.ItemsSource = lines;
        UsersList.ItemsSource = users;
        RecipientBox.ItemsSource = recipients;
        RecipientBox.SelectedIndex = 0;

        HostBox.Text = "127.0.0.1";
        PortBox.Text = ChatDefaults.DefaultPort.ToString();

        // Bu olaylar arka plandaki dinleme görevinden gelir; arayüz UI iş parçacığında güncellenir.
        chat.MessageReceived += (_, message) => Dispatcher.UIThread.Post(() => HandleMessage(message));
        chat.Disconnected += (_, reason) => Dispatcher.UIThread.Post(() => HandleDisconnected(reason));

        // Enter tuşu mesajı göndersin
        MessageInput.AddHandler(KeyDownEvent, MessageInput_KeyDown, RoutingStrategies.Tunnel);

        Closing += (_, _) => _ = chat.DisconnectAsync();

        UpdateConnectionUi();
        UpdateUsersHeader();
        SetStatus("Sunucu adresini ve kullanıcı adınızı girip Bağlan'a basın.");
    }

    // ===================== Bağlantı =====================

    private async void ConnectButton_Click(object? sender, RoutedEventArgs e)
    {
        string host = (HostBox.Text ?? string.Empty).Trim();
        string username = (UsernameBox.Text ?? string.Empty).Trim();

        if (host.Length == 0)
        {
            SetStatus("Sunucu adresi boş olamaz.", isError: true);
            return;
        }
        if (!ChatDefaults.TryParsePort(PortBox.Text, out int port))
        {
            SetStatus("Geçerli bir port girin (1-65535).", isError: true);
            return;
        }
        if (ChatDefaults.ValidateUsername(username) is { } usernameError)
        {
            SetStatus(usernameError, isError: true);
            return;
        }

        isConnecting = true;
        UpdateConnectionUi();
        SetStatus($"{host}:{port} adresine bağlanılıyor...");

        try
        {
            await chat.ConnectAsync(host, port, username);
            AddLine(ChatLineKind.System, $"{host}:{port} sunucusuna \"{username}\" olarak bağlanıldı.");
            SetStatus("Bağlı. Mesajınızı yazıp Enter'a basın.");
            MessageInput.Focus();
        }
        catch (ChatConnectionException ex)
        {
            AddLine(ChatLineKind.Error, ex.Message);
            SetStatus(ex.Message, isError: true);
        }
        finally
        {
            isConnecting = false;
            UpdateConnectionUi();
        }
    }

    private async void DisconnectButton_Click(object? sender, RoutedEventArgs e)
    {
        await chat.DisconnectAsync();
        AddLine(ChatLineKind.System, "Bağlantı kesildi.");
        SetStatus("Bağlantı kesildi.");
        ClearUsers();
        UpdateConnectionUi();
    }

    private void HandleDisconnected(string reason)
    {
        AddLine(ChatLineKind.Error, reason);
        SetStatus(reason, isError: true);
        ClearUsers();
        UpdateConnectionUi();
    }

    // ===================== Mesaj gönderme =====================

    private void MessageInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            SendCurrentMessage();
        }
    }

    private void SendButton_Click(object? sender, RoutedEventArgs e) => SendCurrentMessage();

    private async void SendCurrentMessage()
    {
        if (!chat.IsConnected)
            return;

        string text = (MessageInput.Text ?? string.Empty).Trim();
        if (text.Length == 0)
            return;

        if (text.Length > ChatDefaults.MaxMessageLength)
        {
            SetStatus($"Mesaj en fazla {ChatDefaults.MaxMessageLength} karakter olabilir.", isError: true);
            return;
        }

        string? recipient = RecipientBox.SelectedItem as string;
        MessageInput.Text = string.Empty;

        // Gönderilen mesaj sunucudan geri geldiğinde listeye eklenir;
        // böylece herkes mesajları aynı sırada ve sunucu saatine göre görür.
        if (recipient is null || recipient == EveryoneLabel)
            await chat.SendChatAsync(text);
        else
            await chat.SendPrivateAsync(recipient, text);
    }

    private void UsersList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (UsersList.SelectedItem is string user && recipients.Contains(user))
        {
            RecipientBox.SelectedItem = user;
            MessageInput.Focus();
        }
    }

    // ===================== Gelen mesajlar =====================

    private void HandleMessage(ChatMessage message)
    {
        string me = chat.Username ?? string.Empty;

        switch (message.Type)
        {
            case MessageType.Chat:
                bool mine = message.From == me;
                AddLine(mine ? ChatLineKind.Outgoing : ChatLineKind.Incoming,
                        $"{(mine ? "Sen" : message.From)}: {message.Text}",
                        message.Timestamp);
                break;

            case MessageType.Private:
                if (message.From == me)
                    AddLine(ChatLineKind.PrivateOutgoing, $"(özel) Sen → {message.To}: {message.Text}", message.Timestamp);
                else
                    AddLine(ChatLineKind.PrivateIncoming, $"(özel) {message.From} → Sen: {message.Text}", message.Timestamp);
                break;

            case MessageType.System:
                AddLine(ChatLineKind.System, $"* {message.Text}", message.Timestamp);
                break;

            case MessageType.UserList:
                UpdateUsers(message.Users ?? new List<string>());
                break;
        }
    }

    // ===================== Geçmişi kaydetme =====================

    private async void SaveHistoryButton_Click(object? sender, RoutedEventArgs e)
    {
        if (lines.Count == 0)
        {
            SetStatus("Kaydedilecek mesaj yok.", isError: true);
            return;
        }

        try
        {
            IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Sohbet geçmişini kaydet",
                SuggestedFileName = $"sohbet_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
                DefaultExtension = "txt",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Metin dosyası") { Patterns = new[] { "*.txt" } },
                },
            });

            if (file is null)
                return;

            await using (Stream stream = await file.OpenWriteAsync())
            {
                string username = chat.Username ?? (UsernameBox.Text ?? string.Empty).Trim();
                await ChatHistoryWriter.WriteAsync(stream, username, lines.ToList());
            }

            SetStatus($"{lines.Count} satır kaydedildi: {file.Name}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus($"Geçmiş kaydedilemedi: {ex.Message}", isError: true);
        }
    }

    // ===================== Arayüz güncelleme =====================

    private void AddLine(ChatLineKind kind, string text, DateTime? timestamp = null)
    {
        var line = new ChatLine(timestamp ?? DateTime.Now, kind, text);
        lines.Add(line);
        MessagesList.ScrollIntoView(line);
    }

    private void UpdateUsers(List<string> names)
    {
        string? selectedRecipient = RecipientBox.SelectedItem as string;
        string me = chat.Username ?? string.Empty;

        users.Clear();
        foreach (string name in names)
            users.Add(name == me ? $"{name} (sen)" : name);

        // Alıcı listesi: "Herkes" + kendim hariç diğer kullanıcılar
        recipients.Clear();
        recipients.Add(EveryoneLabel);
        foreach (string name in names.Where(n => n != me))
            recipients.Add(name);

        // Seçili kişi hâlâ çevrimiçiyse seçimi koru, değilse "Herkes"e dön
        RecipientBox.SelectedItem = selectedRecipient is not null && recipients.Contains(selectedRecipient)
            ? selectedRecipient
            : EveryoneLabel;

        UpdateUsersHeader();
    }

    private void ClearUsers()
    {
        users.Clear();
        recipients.Clear();
        recipients.Add(EveryoneLabel);
        RecipientBox.SelectedIndex = 0;
        UpdateUsersHeader();
    }

    private void UpdateUsersHeader() => UsersHeader.Text = $"Çevrimiçi ({users.Count})";

    private void UpdateConnectionUi()
    {
        bool connected = chat.IsConnected;
        bool canEditConnection = !connected && !isConnecting;

        ConnectButton.IsEnabled = canEditConnection;
        DisconnectButton.IsEnabled = connected;
        HostBox.IsEnabled = canEditConnection;
        PortBox.IsEnabled = canEditConnection;
        UsernameBox.IsEnabled = canEditConnection;

        MessageInput.IsEnabled = connected;
        SendButton.IsEnabled = connected;
        RecipientBox.IsEnabled = connected;

        IndicatorDot.Fill = connected ? ConnectedBrush : DisconnectedBrush;
        IndicatorText.Foreground = connected ? ConnectedBrush : DisconnectedBrush;
        IndicatorBorder.Background = connected ? ConnectedBackground : DisconnectedBackground;
        IndicatorText.Text = connected ? $"BAĞLI · {chat.Username}"
                           : isConnecting ? "BAĞLANIYOR..."
                           : "BAĞLI DEĞİL";

        Title = connected ? $"YMH219 - Chat İstemcisi ({chat.Username})" : "YMH219 - Chat İstemcisi";
    }

    private void SetStatus(string message, bool isError = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = isError ? ErrorBrush : Brushes.Black;
    }
}
