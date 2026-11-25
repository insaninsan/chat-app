namespace ChatApp.Shared;

/// <summary>Sunucu ile istemci arasında gidip gelen mesaj türleri.</summary>
public enum MessageType
{
    // İstemci -> Sunucu
    Join,          // Kullanıcı adıyla odaya katılma isteği
    Leave,         // Bağlantıyı kapatma bildirimi

    // Sunucu -> İstemci
    JoinAccepted,  // Katılma kabul edildi
    JoinRejected,  // Katılma reddedildi (ör. kullanıcı adı kullanımda)
    System,        // Sunucu bildirimi (ör. "ali sohbete katıldı")
    UserList,      // Odadaki kullanıcıların güncel listesi

    // Her iki yönde
    Chat,          // Herkese açık mesaj
    Private,       // Tek bir kullanıcıya özel mesaj
}
