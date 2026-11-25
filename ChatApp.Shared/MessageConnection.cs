using System.Net.Sockets;
using System.Text;

namespace ChatApp.Shared;

/// <summary>
/// Bir TCP bağlantısı üzerinden ChatMessage gönderip almayı sağlar.
/// NetworkStream, StreamReader/StreamWriter ile sarılır; her mesaj bir satırdır.
/// Hem sunucu (her istemci için bir tane) hem istemci bu sınıfı kullanır.
/// </summary>
public sealed class MessageConnection : IDisposable
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly TcpClient client;
    private readonly StreamReader reader;
    private readonly StreamWriter writer;

    // Aynı anda iki görev yazarsa satırlar birbirine karışmasın diye gönderimler sıraya alınır.
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private bool disposed;

    public MessageConnection(TcpClient client)
    {
        this.client = client;
        RemoteEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "bilinmiyor";

        NetworkStream stream = client.GetStream();
        reader = new StreamReader(stream, Utf8);
        writer = new StreamWriter(stream, Utf8) { AutoFlush = true, NewLine = "\n" };
    }

    /// <summary>Karşı tarafın IP adresi ve portu (ör. "192.168.1.20:53122").</summary>
    public string RemoteEndPoint { get; }

    public async Task SendAsync(ChatMessage message, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        await sendLock.WaitAsync(cancellationToken);
        try
        {
            await writer.WriteLineAsync(message.Serialize().AsMemory(), cancellationToken);
        }
        finally
        {
            sendLock.Release();
        }
    }

    /// <summary>
    /// Sıradaki mesajı bekler. Karşı taraf bağlantıyı düzgünce kapattıysa null döner.
    /// Bozuk satırlar atlanır.
    /// </summary>
    public async Task<ChatMessage?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
                return null;

            ChatMessage? message = ChatMessage.Deserialize(line);
            if (message is not null)
                return message;
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;

        // Bağlantı zaten kopmuşsa kapatırken hata oluşabilir; bunlar önemsizdir.
        try { writer.Dispose(); } catch (IOException) { } catch (ObjectDisposedException) { }
        try { reader.Dispose(); } catch (IOException) { }
        client.Dispose();
    }
}
