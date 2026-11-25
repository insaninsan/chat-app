using System.Net;
using System.Net.Sockets;
using ChatApp.Server.Models;
using ChatApp.Shared;

namespace ChatApp.Server.Services;

/// <summary>
/// Sohbet sunucusu. TcpListener ile bağlantı kabul eder, her istemciyi ayrı bir
/// asenkron görevde dinler ve mesajları ilgili istemcilere iletir.
/// Arayüzden bağımsızdır; pencere Log ve ClientsChanged olaylarını dinler.
/// </summary>
public class ChatServerHost
{
    private static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(10);

    private readonly List<ConnectedClient> clients = new();
    private readonly object clientsLock = new();

    private TcpListener? listener;
    private CancellationTokenSource? cancellation;

    /// <summary>Sunucuda olan bir şeyin metinsel kaydı (bağlantı, mesaj, hata...).</summary>
    public event EventHandler<string>? Log;

    /// <summary>Bağlı istemci listesi değiştiğinde tetiklenir.</summary>
    public event EventHandler? ClientsChanged;

    public bool IsRunning => listener is not null;
    public int Port { get; private set; }

    public IReadOnlyList<ConnectedClient> GetClients()
    {
        lock (clientsLock)
            return clients.ToList();
    }

    /// <summary>Sunucuyu başlatır. Port kullanımdaysa SocketException fırlatır.</summary>
    public void Start(int port)
    {
        if (IsRunning)
            return;

        var newListener = new TcpListener(IPAddress.Any, port);
        newListener.Start();  // Port doluysa burada SocketException oluşur

        listener = newListener;
        Port = port;
        cancellation = new CancellationTokenSource();
        _ = AcceptClientsAsync(newListener, cancellation.Token);

        Log?.Invoke(this, $"Sunucu başlatıldı, {port} portu dinleniyor.");
    }

    public async Task StopAsync()
    {
        if (listener is null || cancellation is null)
            return;

        cancellation.Cancel();
        listener.Stop();
        listener = null;

        List<ConnectedClient> toClose;
        lock (clientsLock)
        {
            toClose = clients.ToList();
            clients.Clear();
        }

        // İstemcilere haber verip bağlantıları kapat
        foreach (ConnectedClient client in toClose)
        {
            await TrySendAsync(client, ChatMessage.System("Sunucu kapatıldı."));
            client.Connection.Dispose();
        }

        ClientsChanged?.Invoke(this, EventArgs.Empty);
        Log?.Invoke(this, "Sunucu durduruldu.");
    }

    // ===================== Bağlantı kabulü =====================

    private async Task AcceptClientsAsync(TcpListener activeListener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient tcpClient;
            try
            {
                tcpClient = await activeListener.AcceptTcpClientAsync(token);
            }
            catch (OperationCanceledException)
            {
                break;  // Sunucu durduruldu
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
                if (!token.IsCancellationRequested)
                    Log?.Invoke(this, $"Bağlantı kabul edilemedi: {ex.Message}");
                break;
            }

            // Her istemci kendi görevinde çalışır; sunucu bir sonraki bağlantıyı beklemeye devam eder.
            _ = HandleClientAsync(tcpClient, token);
        }
    }

    // ===================== Tek bir istemcinin yaşam döngüsü =====================

    private async Task HandleClientAsync(TcpClient tcpClient, CancellationToken token)
    {
        using var connection = new MessageConnection(tcpClient);
        ConnectedClient? client = null;

        try
        {
            client = await TryJoinAsync(connection, token);
            if (client is null)
                return;

            while (!token.IsCancellationRequested)
            {
                ChatMessage? message = await connection.ReceiveAsync(token);
                if (message is null || message.Type == MessageType.Leave)
                    break;

                await HandleMessageAsync(client, message);
            }
        }
        catch (OperationCanceledException)
        {
            // Sunucu durduruluyor
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            // İstemci bağlantısı beklenmedik şekilde koptu; aşağıda temizlenir.
        }
        finally
        {
            if (client is not null)
                await RemoveClientAsync(client, token);
        }
    }

    /// <summary>
    /// İlk mesajın geçerli bir Join olmasını bekler, kullanıcı adını doğrular ve istemciyi odaya ekler.
    /// Katılım reddedilirse null döner.
    /// </summary>
    private async Task<ConnectedClient?> TryJoinAsync(MessageConnection connection, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(JoinTimeout);

        ChatMessage? first;
        try
        {
            first = await connection.ReceiveAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            Log?.Invoke(this, $"{connection.RemoteEndPoint} zamanında kullanıcı adı göndermedi, bağlantı kapatıldı.");
            return null;
        }

        if (first is null || first.Type != MessageType.Join)
        {
            Log?.Invoke(this, $"{connection.RemoteEndPoint} geçersiz bir bağlantı isteği gönderdi.");
            await connection.SendAsync(ChatMessage.JoinRejected("Geçersiz bağlantı isteği."), token);
            return null;
        }

        string? error = ChatDefaults.ValidateUsername(first.From);
        if (error is not null)
        {
            await connection.SendAsync(ChatMessage.JoinRejected(error), token);
            return null;
        }

        string username = first.From!.Trim();
        ConnectedClient? client = null;

        // Ad kontrolü ve listeye ekleme aynı kilit içinde yapılır; böylece aynı anda
        // aynı adla gelen iki istemciden yalnızca biri kabul edilir.
        lock (clientsLock)
        {
            bool nameTaken = clients.Any(c =>
                string.Equals(c.Username, username, StringComparison.OrdinalIgnoreCase));

            if (!nameTaken)
            {
                client = new ConnectedClient(username, connection);
                clients.Add(client);
            }
        }

        if (client is null)
        {
            Log?.Invoke(this, $"{connection.RemoteEndPoint} \"{username}\" adıyla katılmak istedi; ad kullanımda.");
            await connection.SendAsync(ChatMessage.JoinRejected($"\"{username}\" kullanıcı adı kullanımda."), token);
            return null;
        }

        await connection.SendAsync(ChatMessage.JoinAccepted(username), token);

        Log?.Invoke(this, $"{username} bağlandı ({client.RemoteEndPoint}).");
        ClientsChanged?.Invoke(this, EventArgs.Empty);

        await BroadcastAsync(ChatMessage.System($"{username} sohbete katıldı."));
        await BroadcastUserListAsync();
        return client;
    }

    private async Task HandleMessageAsync(ConnectedClient sender, ChatMessage message)
    {
        string text = (message.Text ?? string.Empty).Trim();
        if (text.Length == 0)
            return;

        if (text.Length > ChatDefaults.MaxMessageLength)
            text = text[..ChatDefaults.MaxMessageLength];

        switch (message.Type)
        {
            case MessageType.Chat:
                Log?.Invoke(this, $"{sender.Username}: {text}");
                await BroadcastAsync(ChatMessage.Chat(sender.Username, text));
                break;

            case MessageType.Private:
                await SendPrivateAsync(sender, message.To, text);
                break;
        }
    }

    private async Task SendPrivateAsync(ConnectedClient sender, string? targetName, string text)
    {
        ConnectedClient? target;
        lock (clientsLock)
        {
            target = clients.FirstOrDefault(c =>
                string.Equals(c.Username, targetName, StringComparison.OrdinalIgnoreCase));
        }

        if (target is null)
        {
            await TrySendAsync(sender, ChatMessage.System($"\"{targetName}\" adlı kullanıcı bulunamadı."));
            return;
        }

        // Özel mesajın içeriği sunucu kaydına yazılmaz, yalnızca kimin kime gönderdiği yazılır.
        Log?.Invoke(this, $"{sender.Username} → {target.Username} özel mesaj gönderdi.");

        ChatMessage privateMessage = ChatMessage.Private(sender.Username, target.Username, text);
        await TrySendAsync(target, privateMessage);

        if (target != sender)
            await TrySendAsync(sender, privateMessage);  // Gönderen de kendi mesajını görsün
    }

    private async Task RemoveClientAsync(ConnectedClient client, CancellationToken token)
    {
        bool removed;
        lock (clientsLock)
            removed = clients.Remove(client);

        // Sunucu durdurulurken StopAsync listeyi zaten temizler.
        if (!removed || token.IsCancellationRequested)
            return;

        Log?.Invoke(this, $"{client.Username} ayrıldı.");
        ClientsChanged?.Invoke(this, EventArgs.Empty);

        await BroadcastAsync(ChatMessage.System($"{client.Username} sohbetten ayrıldı."));
        await BroadcastUserListAsync();
    }

    // ===================== Gönderim yardımcıları =====================

    private Task BroadcastUserListAsync()
    {
        IEnumerable<string> names = GetClients()
            .Select(c => c.Username)
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase);
        return BroadcastAsync(ChatMessage.UserList(names));
    }

    private async Task BroadcastAsync(ChatMessage message)
    {
        foreach (ConnectedClient client in GetClients())
            await TrySendAsync(client, message);
    }

    /// <summary>
    /// Mesajı gönderir; bağlantısı kopmuş bir istemci yüzünden diğerlerine gönderim aksamasın diye
    /// hata yutulur. Kopan istemci kendi dinleme döngüsünde listeden çıkarılır.
    /// </summary>
    private static async Task TrySendAsync(ConnectedClient client, ChatMessage message)
    {
        try
        {
            await client.Connection.SendAsync(message);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
        }
    }
}
