namespace ChatApp.Shared;

/// <summary>Sunucu ve istemcinin ortak kullandığı sabitler ve doğrulama kuralları.</summary>
public static class ChatDefaults
{
    /// <summary>Varsayılan port. (macOS'ta 5000 portu AirPlay tarafından kullanıldığı için 9000 seçildi.)</summary>
    public const int DefaultPort = 9000;

    public const int MaxUsernameLength = 20;
    public const int MaxMessageLength = 1000;

    /// <summary>Kullanıcı adı geçerliyse null, değilse hata mesajı döndürür.</summary>
    public static string? ValidateUsername(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return "Kullanıcı adı boş olamaz.";

        if (username.Trim().Length > MaxUsernameLength)
            return $"Kullanıcı adı en fazla {MaxUsernameLength} karakter olabilir.";

        return null;
    }

    /// <summary>Port geçerliyse true döndürür ve değeri port parametresine yazar.</summary>
    public static bool TryParsePort(string? text, out int port) =>
        int.TryParse(text, out port) && port is >= 1 and <= 65535;
}
