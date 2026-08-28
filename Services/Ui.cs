using MiniNotify.Models;
namespace MiniNotify.Services;

public static class Ui
{
    public static (string text, string css, string icon) Chan(Channel c) => c switch
    {
        Channel.Email => ("Email", "primary", "bi-envelope"),
        Channel.Sms   => ("SMS", "success", "bi-chat-dots"),
        Channel.Zalo  => ("Zalo", "info", "bi-chat-heart"),
        Channel.Push  => ("Push", "warning", "bi-bell"),
        _ => (c.ToString(), "secondary", "bi-question")
    };

    public static (string text, string css) Stat(NotiStatus s) => s switch
    {
        NotiStatus.Queued    => ("Chờ gửi", "secondary"),
        NotiStatus.Sent      => ("Đã gửi", "info"),
        NotiStatus.Delivered => ("Đã nhận", "success"),
        NotiStatus.Failed    => ("Thất bại", "danger"),
        _ => (s.ToString(), "secondary")
    };
}
