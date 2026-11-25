namespace ChatApp.Client.Services;

/// <summary>
/// Sunucuya bağlanılamadığında veya sunucu katılımı reddettiğinde fırlatılır.
/// Mesajı doğrudan kullanıcıya gösterilebilecek şekilde Türkçedir.
/// </summary>
public class ChatConnectionException : Exception
{
    public ChatConnectionException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
