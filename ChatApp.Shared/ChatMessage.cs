using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChatApp.Shared;

/// <summary>
/// Ağ üzerinden gönderilen tek bir mesaj. Her mesaj JSON'a çevrilir ve
/// tek satır olarak gönderilir (satır sonu = mesaj sonu).
/// Nesneler doğrudan oluşturulmaz; aşağıdaki fabrika metotları kullanılır.
/// </summary>
public sealed class ChatMessage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public MessageType Type { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public string? Text { get; init; }
    public DateTime Timestamp { get; init; }
    public List<string>? Users { get; init; }

    // ---------- İstemcinin gönderdiği mesajlar ----------

    public static ChatMessage Join(string username) => new() { Type = MessageType.Join, From = username };

    public static ChatMessage Leave() => new() { Type = MessageType.Leave };

    /// <summary>Herkese mesaj isteği. Gönderen ve zaman bilgisini sunucu ekler.</summary>
    public static ChatMessage ChatRequest(string text) => new() { Type = MessageType.Chat, Text = text };

    /// <summary>Özel mesaj isteği. Gönderen ve zaman bilgisini sunucu ekler.</summary>
    public static ChatMessage PrivateRequest(string to, string text) =>
        new() { Type = MessageType.Private, To = to, Text = text };

    // ---------- Sunucunun gönderdiği mesajlar ----------

    public static ChatMessage JoinAccepted(string username) =>
        new() { Type = MessageType.JoinAccepted, To = username, Timestamp = DateTime.Now };

    public static ChatMessage JoinRejected(string reason) =>
        new() { Type = MessageType.JoinRejected, Text = reason, Timestamp = DateTime.Now };

    public static ChatMessage Chat(string from, string text) =>
        new() { Type = MessageType.Chat, From = from, Text = text, Timestamp = DateTime.Now };

    public static ChatMessage Private(string from, string to, string text) =>
        new() { Type = MessageType.Private, From = from, To = to, Text = text, Timestamp = DateTime.Now };

    public static ChatMessage System(string text) =>
        new() { Type = MessageType.System, Text = text, Timestamp = DateTime.Now };

    public static ChatMessage UserList(IEnumerable<string> users) =>
        new() { Type = MessageType.UserList, Users = users.ToList(), Timestamp = DateTime.Now };

    // ---------- JSON dönüşümü ----------

    public string Serialize() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Satırı mesaja çevirir. Bozuk veya tanınmayan bir satırda null döner.</summary>
    public static ChatMessage? Deserialize(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<ChatMessage>(line, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
