using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MiniNotify.Data;
using MiniNotify.Models;
using MiniNotify.Services;
using Xunit;

namespace MiniNotify.Tests;

/// <summary>Test hub thông báo: gửi tạo bản ghi, render {{placeholder}} theo mẫu, retry, đánh dấu đã nhận, dashboard theo kênh.</summary>
public class NotifyServiceTests
{
    private static (AppDbContext db, INotifyService svc, SqliteConnection conn) NewSvc()
    {
        var conn = new SqliteConnection("DataSource=:memory:"); conn.Open();
        var opt = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var db = new AppDbContext(opt, new TenantContext { OrgId = TenantContext.DefaultOrgId });
        db.Database.EnsureCreated();
        return (db, new NotifyService(db), conn);
    }

    [Fact]
    public async Task SendDirect_CreatesNotification()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var r = await svc.SendDirectAsync(Channel.Email, "a@b.com", "Chào", "Nội dung");
            Assert.True(r.ok);
            var n = await svc.GetNotificationAsync(r.id);
            Assert.Equal("a@b.com", n!.ToAddress);
            Assert.NotEqual(NotiStatus.Queued, n.Status);  // đã gửi (Sent/Failed)
        }
    }

    [Fact]
    public async Task SendByTemplate_RendersPlaceholders()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await svc.SaveTemplateAsync(new Template { Code = "WELCOME", Name = "Chào mừng", Channel = Channel.Email, Subject = "Xin chào {{ten}}", Body = "Cảm ơn {{ten}} đã đăng ký." });
            var r = await svc.SendByTemplateAsync("WELCOME", "a@b.com", new() { ["ten"] = "Anh Nam" });
            Assert.True(r.ok);
            var n = await svc.GetNotificationAsync(r.id);
            Assert.Contains("Anh Nam", n!.Body);
            Assert.DoesNotContain("{{", n.Body);   // đã thay placeholder
        }
    }

    [Fact]
    public async Task SendByTemplate_UnknownCode_Fails()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var r = await svc.SendByTemplateAsync("KHONGCO", "a@b.com", new());
            Assert.False(r.ok);
        }
    }

    [Fact]
    public async Task Retry_IncrementsRetryCount()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var r = await svc.SendDirectAsync(Channel.Sms, "0900", "", "hi");
            // ép Failed để test retry
            var n = await db.Notifications.FirstAsync(x => x.Id == r.id);
            n.Status = NotiStatus.Failed; await db.SaveChangesAsync();
            await svc.RetryAsync(r.id);   // Dispatch ngẫu nhiên Sent/Failed — chỉ kiểm RetryCount tăng
            var after = await svc.GetNotificationAsync(r.id);
            Assert.Equal(1, after!.RetryCount);
        }
    }

    [Fact]
    public async Task MarkDelivered_SetsDelivered()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var r = await svc.SendDirectAsync(Channel.Push, "token", "t", "b");
            var n = await db.Notifications.FirstAsync(x => x.Id == r.id);
            n.Status = NotiStatus.Sent; await db.SaveChangesAsync();
            var (ok, _) = await svc.MarkDeliveredAsync(r.id);
            Assert.True(ok);
            Assert.Equal(NotiStatus.Delivered, (await svc.GetNotificationAsync(r.id))!.Status);
        }
    }

    [Fact]
    public async Task Dashboard_CountsByChannel()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await svc.SendDirectAsync(Channel.Email, "a@b.com", "", "x");
            await svc.SendDirectAsync(Channel.Sms, "0900", "", "y");
            var d = await svc.DashboardAsync();
            Assert.Equal(2, d.Total);
            Assert.True(d.ByChannel.ContainsKey(Channel.Email));
        }
    }
}
