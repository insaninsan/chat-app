using System.Net.Sockets;
using ChatApp.Shared;

namespace ChatApp.Client.Services;

/// <summary>
/// İstemci tarafı bağlantı yönetimi: sunucuya bağlanır, kullanıcı adıyla odaya katılır,
/// gelen mesajları arka planda dinler ve mesaj gönderir.
/// Arayüzden bağımsızdır; pencere MessageReceived ve Disconnected olaylarını dinler.
/// </summary>
public class ChatClientService
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    private MessageConnection? connection;
    private CancellationTokenSource? receiveCancellation;

    /// <summary>Sunucudan bir mesaj geldiğinde tetiklenir.</summary>
    public event EventHandler<ChatMessage>? MessageReceived;

    /// <summary>Bağlantı kullanıcı istemeden koptuğunda tetiklenir. Parametre: sebep.</summary>
    public event EventHandler<string>? Disconnected;

    public bool IsConnected => connection is not null;
    public string? Username { get; private set; }

    /// <summary>
    /// Sunucuya bağlanır ve odaya katılır.
    /// Başarısız olursa açıklayıcı bir ChatConnectionException fırlatır.
    /// </summary>
    public async Task ConnectAsync(string host, int port, string username)
    {
        if (IsConnected)
            throw new InvalidOperationException("Zaten bağlı.");

        using var timeout = new CancellationTokenSource(ConnectTimeout);
        var tcpClient = new TcpClient();

        try
        {
            await tcpClient.ConnectAsync(host, port, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            tcpClient.Dispose();
            throw new ChatConnectionException($"{host}:{port} adresine bağlanma zaman aşımına uğradı.");
        }
        catch (SocketException ex)
        {
            tcpClient.Dispose();
            throw new ChatConnectionException(
                $"{host}:{port} adresine bağlanılamadı. Sunucu çalışıyor mu? ({ex.Message})", ex);
        }

        var newConnection = new MessageConnection(tcpClient);
        try
        {
            await newConnection.SendAsync(ChatMessage.Join(username), timeout.Token);
            ChatMessage? reply = await newConnection.ReceiveAsync(timeout.Token);

            if (reply is null)
                throw new ChatConnectionException("Sunucu bağlantıyı kapattı.");
            if (reply.Type == MessageType.JoinRejected)
                throw new ChatConnectionException($"Sunucu bağlantıyı reddetti: {reply.Text}");
            if (reply.Type != MessageType.JoinAccepted)
                throw new ChatConnectionException("Sunucudan beklenmeyen bir yanıt geldi.");
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException)
        {
            newConnection.Dispose();
            throw new ChatConnectionException($"Sunucuyla el sıkışma başarısız: {ex.Message}", ex);
        }
        catch
        {
            newConnection.Dispose();
            throw;
        }

        connection = newConnection;
        Username = username;
        receiveCancellation = new CancellationTokenSource();
        _ = ReceiveLoopAsync(newConnection, receiveCancellation.Token);
    }

    public Task SendChatAsync(string text) => SendAsync(ChatMessage.ChatRequest(text));

    public Task SendPrivateAsync(string to, string text) => SendAsync(ChatMessage.PrivateRequest(to, text));

    /// <summary>Kullanıcının isteğiyle bağlantıyı kapatır (Disconnected olayı tetiklenmez).</summary>
    public async Task DisconnectAsync()
    {
        MessageConnection? current = connection;
        if (current is null)
            return;

        try
        {
            await current.SendAsync(ChatMessage.Leave());
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            // Bağlantı zaten kopmuş olabilir; yine de kapatıyoruz.
        }

        receiveCancellation?.Cancel();
        CloseConnection(current);
    }

    // ===================== İç işleyiş =====================

    private async Task SendAsync(ChatMessage message)
    {
        MessageConnection current = connection
            ?? throw new InvalidOperationException("Sunucuya bağlı değilsiniz.");

        try
        {
            await current.SendAsync(message);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            HandleConnectionLost(current, $"Mesaj gönderilemedi, bağlantı koptu: {ex.Message}");
        }
    }

    /// <summary>Sunucudan gelen mesajları bağlantı açık olduğu sürece dinler.</summary>
    private async Task ReceiveLoopAsync(MessageConnection current, CancellationToken token)
    {
        string reason = "Sunucu bağlantıyı kapattı.";
        try
        {
            while (true)
            {
                ChatMessage? message = await current.ReceiveAsync(token);
                if (message is null)
                    break;

                MessageReceived?.Invoke(this, message);
            }
        }
        catch (OperationCanceledException)
        {
            return;  // Kullanıcı kendisi bağlantıyı kesti
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            if (token.IsCancellationRequested)
                return;
            reason = $"Sunucuyla bağlantı koptu: {ex.Message}";
        }

        HandleConnectionLost(current, reason);
    }

    private void HandleConnectionLost(MessageConnection current, string reason)
    {
        // Aynı bağlantı için yalnızca bir kez bildir
        if (connection != current)
            return;

        CloseConnection(current);
        Disconnected?.Invoke(this, reason);
    }

    private void CloseConnection(MessageConnection current)
    {
        if (connection == current)
        {
            connection = null;
            Username = null;
        }
        current.Dispose();
    }
}
