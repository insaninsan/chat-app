using ChatApp.Shared;

namespace ChatApp.Server.Models;

/// <summary>Sunucuya bağlı ve odaya katılmış bir istemci.</summary>
public sealed class ConnectedClient
{
    public ConnectedClient(string username, MessageConnection connection)
    {
        Username = username;
        Connection = connection;
        RemoteEndPoint = connection.RemoteEndPoint;
        ConnectedAt = DateTime.Now;
    }

    public string Username { get; }
    public string RemoteEndPoint { get; }
    public DateTime ConnectedAt { get; }
    public MessageConnection Connection { get; }

    /// <summary>
    /// Sunucu yöneticisi bu istemciyi attıysa true. Ayrılma bildiriminde
    /// "ayrıldı" yerine "çıkarıldı" yazılması için kullanılır.
    /// (Farklı iş parçacıklarından okunduğu için volatile.)
    /// </summary>
    public bool WasKicked
    {
        get => Volatile.Read(ref wasKicked);
        set => Volatile.Write(ref wasKicked, value);
    }

    private bool wasKicked;

    /// <summary>Sunucu penceresindeki listede gösterilen satır.</summary>
    public string DisplayText => $"{Username}   ({RemoteEndPoint})   bağlanma: {ConnectedAt:HH:mm:ss}";

    public override string ToString() => DisplayText;
}
