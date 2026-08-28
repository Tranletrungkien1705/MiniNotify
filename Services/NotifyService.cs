using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MiniNotify.Data;
using MiniNotify.Models;

namespace MiniNotify.Services;

public record SendResult(bool ok, string msg, int id, string status);
public record NotifyDash(int Total, int Sent, int Delivered, int Failed, Dictionary<Channel, int> ByChannel, List<Notification> Recent);

public interface INotifyService
{
    Task<List<Template>> TemplatesAsync();
    Task<Template?> GetTemplateAsync(int id);
    Task<(bool ok, string msg, int id)> SaveTemplateAsync(Template t);
    Task<List<Notification>> NotificationsAsync(NotiStatus? status, Channel? channel);
    Task<Notification?> GetNotificationAsync(int id);
    Task<SendResult> SendByTemplateAsync(string code, string to, Dictionary<string, string> data);
    Task<SendResult> SendDirectAsync(Channel channel, string to, string subject, string body);
    Task<(bool ok, string msg)> RetryAsync(int id);
    Task<(bool ok, string msg)> MarkDeliveredAsync(int id);
    Task<NotifyDash> DashboardAsync();
    string Render(string tpl, Dictionary<string, string> data);
}

public class NotifyService(AppDbContext db) : INotifyService
{
    private static readonly Regex Ph = new(@"\{\{\s*(\w+)\s*\}\}", RegexOptions.Compiled);
    private static readonly Regex EmailRx = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    public Task<List<Template>> TemplatesAsync() => db.Templates.OrderBy(t => t.Channel).ThenBy(t => t.Name).ToListAsync();
    public Task<Template?> GetTemplateAsync(int id) => db.Templates.FirstOrDefaultAsync(t => t.Id == id);

    public async Task<(bool ok, string msg, int id)> SaveTemplateAsync(Template t)
    {
        if (string.IsNullOrWhiteSpace(t.Name)) return (false, "Cần tên mẫu.", 0);
        if (string.IsNullOrWhiteSpace(t.Code)) t.Code = "TPL" + (await db.Templates.CountAsync() + 1).ToString("D3");
        if (t.Id == 0)
        {
            if (await db.Templates.AnyAsync(x => x.Code == t.Code)) return (false, "Mã mẫu đã tồn tại.", 0);
            db.Templates.Add(t);
        }
        else
        {
            var ex = await db.Templates.FirstOrDefaultAsync(x => x.Id == t.Id);
            if (ex == null) return (false, "Không tìm thấy mẫu.", 0);
            ex.Name = t.Name; ex.Channel = t.Channel; ex.Subject = t.Subject; ex.Body = t.Body; ex.Active = t.Active;
        }
        await db.SaveChangesAsync();
        return (true, "Đã lưu mẫu.", t.Id);
    }

    public Task<List<Notification>> NotificationsAsync(NotiStatus? status, Channel? channel)
    {
        var q = db.Notifications.AsQueryable();
        if (status.HasValue) q = q.Where(n => n.Status == status.Value);
        if (channel.HasValue) q = q.Where(n => n.Channel == channel.Value);
        return q.OrderByDescending(n => n.Id).Take(500).ToListAsync();
    }

    public Task<Notification?> GetNotificationAsync(int id) => db.Notifications.FirstOrDefaultAsync(n => n.Id == id);

    public async Task<SendResult> SendByTemplateAsync(string code, string to, Dictionary<string, string> data)
    {
        var t = await db.Templates.FirstOrDefaultAsync(x => x.Code == (code ?? "").Trim());
        if (t == null) return new(false, "Không tìm thấy mẫu.", 0, "");
        if (!t.Active) return new(false, "Mẫu đang tắt.", 0, "");
        var subject = Render(t.Subject, data);
        var body = Render(t.Body, data);
        return await CreateAndSendAsync(t.Channel, to, subject, body, t.Id, t.Name);
    }

    public Task<SendResult> SendDirectAsync(Channel channel, string to, string subject, string body) =>
        CreateAndSendAsync(channel, to, subject, body, null, null);

    private async Task<SendResult> CreateAndSendAsync(Channel channel, string to, string subject, string body, int? tplId, string? tplName)
    {
        var n = new Notification { Channel = channel, ToAddress = (to ?? "").Trim(), Subject = subject ?? "", Body = body ?? "", TemplateId = tplId, TemplateName = tplName, Status = NotiStatus.Queued };
        db.Notifications.Add(n);
        await db.SaveChangesAsync();
        Dispatch(n);
        await db.SaveChangesAsync();
        return new(n.Status != NotiStatus.Failed, n.Status == NotiStatus.Failed ? n.Error ?? "Gửi thất bại" : "Đã gửi.", n.Id, n.Status.ToString());
    }

    // Mô phỏng nhà cung cấp: validate địa chỉ theo kênh → Sent / Failed.
    private void Dispatch(Notification n)
    {
        n.Provider = n.Channel switch { Channel.Email => "SMTP", Channel.Sms => "SMSBrand", Channel.Zalo => "ZaloOA", Channel.Push => "FCM", _ => "?" };
        string? err = n.Channel switch
        {
            Channel.Email => EmailRx.IsMatch(n.ToAddress) ? null : "Địa chỉ email không hợp lệ",
            Channel.Sms or Channel.Zalo => IsPhone(n.ToAddress) ? null : "Số điện thoại không hợp lệ",
            Channel.Push => string.IsNullOrWhiteSpace(n.ToAddress) ? "Thiếu device token" : null,
            _ => "Kênh không hỗ trợ"
        };
        if (string.IsNullOrEmpty(n.Body)) err ??= "Nội dung rỗng";
        if (err == null) { n.Status = NotiStatus.Sent; n.ResultCode = "200"; n.SentAt = DateTime.UtcNow; n.Error = null; }
        else { n.Status = NotiStatus.Failed; n.ResultCode = "400"; n.Error = err; }
    }

    public async Task<(bool ok, string msg)> RetryAsync(int id)
    {
        var n = await db.Notifications.FirstOrDefaultAsync(x => x.Id == id);
        if (n == null) return (false, "Không tìm thấy.");
        if (n.Status != NotiStatus.Failed) return (false, "Chỉ gửi lại được thông báo thất bại.");
        n.RetryCount++;
        Dispatch(n);
        await db.SaveChangesAsync();
        return n.Status != NotiStatus.Failed ? (true, "Đã gửi lại thành công.") : (false, "Gửi lại vẫn thất bại: " + n.Error);
    }

    public async Task<(bool ok, string msg)> MarkDeliveredAsync(int id)
    {
        var n = await db.Notifications.FirstOrDefaultAsync(x => x.Id == id);
        if (n == null) return (false, "Không tìm thấy.");
        if (n.Status != NotiStatus.Sent) return (false, "Chỉ xác nhận nhận với thông báo đã gửi.");
        n.Status = NotiStatus.Delivered; n.DeliveredAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return (true, "Đã xác nhận đến người nhận.");
    }

    public async Task<NotifyDash> DashboardAsync()
    {
        var all = await db.Notifications.ToListAsync();
        return new NotifyDash(
            all.Count,
            all.Count(n => n.Status == NotiStatus.Sent),
            all.Count(n => n.Status == NotiStatus.Delivered),
            all.Count(n => n.Status == NotiStatus.Failed),
            all.GroupBy(n => n.Channel).ToDictionary(g => g.Key, g => g.Count()),
            all.OrderByDescending(n => n.Id).Take(8).ToList());
    }

    public string Render(string tpl, Dictionary<string, string> data) =>
        Ph.Replace(tpl ?? "", m => data != null && data.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);

    private static bool IsPhone(string s)
    {
        var d = new string((s ?? "").Where(char.IsDigit).ToArray());
        return d.Length is >= 9 and <= 11;
    }
}
