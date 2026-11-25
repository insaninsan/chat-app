using System.Text;
using ChatApp.Client.Models;

namespace ChatApp.Client.Services;

/// <summary>Sohbet geçmişini UTF-8 metin dosyasına yazar.</summary>
public static class ChatHistoryWriter
{
    public static async Task WriteAsync(Stream stream, string username, IEnumerable<ChatLine> lines)
    {
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));

        await writer.WriteLineAsync("# YMH219 Chat - sohbet geçmişi");
        await writer.WriteLineAsync($"# Kullanıcı: {username}");
        await writer.WriteLineAsync($"# Kaydedilme: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        await writer.WriteLineAsync();

        foreach (ChatLine line in lines)
            await writer.WriteLineAsync($"{line.Timestamp:yyyy-MM-dd} {line.DisplayText}");
    }
}
