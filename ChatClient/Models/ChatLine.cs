using Avalonia.Media;

namespace ChatApp.Client.Models;

public enum ChatLineKind
{
    Incoming,         // Başkasının herkese gönderdiği mesaj
    Outgoing,         // Benim herkese gönderdiğim mesaj
    PrivateIncoming,  // Bana gelen özel mesaj
    PrivateOutgoing,  // Benim gönderdiğim özel mesaj
    System,           // Sunucu bildirimi
    Error,            // Bağlantı hatası
}

/// <summary>Mesaj listesinde gösterilen tek satır.</summary>
public sealed class ChatLine
{
    private static readonly IBrush OutgoingBrush = new SolidColorBrush(Color.FromRgb(29, 78, 216));
    private static readonly IBrush PrivateBrush = new SolidColorBrush(Color.FromRgb(147, 51, 234));
    private static readonly IBrush SystemBrush = new SolidColorBrush(Color.FromRgb(107, 114, 128));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.FromRgb(185, 28, 28));

    public ChatLine(DateTime timestamp, ChatLineKind kind, string text)
    {
        Timestamp = timestamp;
        Kind = kind;
        Text = text;
    }

    public DateTime Timestamp { get; }
    public ChatLineKind Kind { get; }
    public string Text { get; }

    public string DisplayText => $"[{Timestamp:HH:mm:ss}] {Text}";

    public IBrush Foreground => Kind switch
    {
        ChatLineKind.Outgoing => OutgoingBrush,
        ChatLineKind.PrivateIncoming or ChatLineKind.PrivateOutgoing => PrivateBrush,
        ChatLineKind.System => SystemBrush,
        ChatLineKind.Error => ErrorBrush,
        _ => Brushes.Black,
    };

    public FontStyle FontStyle =>
        Kind is ChatLineKind.System or ChatLineKind.Error ? FontStyle.Italic : FontStyle.Normal;

    public override string ToString() => DisplayText;
}
