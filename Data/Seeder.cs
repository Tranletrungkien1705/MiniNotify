using Microsoft.EntityFrameworkCore;
using MiniNotify.Models;
namespace MiniNotify.Data;

public static class Seeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        await db.Database.EnsureCreatedAsync();
        await MigratePostgresAsync(db);
        if (!await db.Orgs.AnyAsync(o => o.Id == TenantContext.DefaultOrgId))
        { db.Orgs.Add(new Org { Id = TenantContext.DefaultOrgId, Name = "Demo Thông báo", ApiKey = TenantContext.DefaultApiKey }); await db.SaveChangesAsync(); }

        if (!await db.Templates.AnyAsync())
        {
            db.Templates.AddRange(
                new Template { Code = "INV_ISSUED", Name = "HĐĐT đã phát hành", Channel = Channel.Email,
                    Subject = "Hóa đơn {{invoiceNo}} đã phát hành", Body = "Kính gửi {{name}},\nHóa đơn {{invoiceNo}} trị giá {{total}} đã được phát hành. Mã tra cứu: {{code}}." },
                new Template { Code = "OTP", Name = "Mã OTP", Channel = Channel.Sms, Subject = "",
                    Body = "Ma OTP cua ban la {{otp}}, hieu luc 5 phut." },
                new Template { Code = "PROMO", Name = "Khuyến mãi", Channel = Channel.Zalo, Subject = "",
                    Body = "{{name}} ơi, quét mã tem để quay trúng thưởng tại {{link}}!" });
            await db.SaveChangesAsync();

            var t = await db.Templates.FirstAsync(x => x.Code == "INV_ISSUED");
            db.Notifications.Add(new Notification { TemplateId = t.Id, TemplateName = t.Name, Channel = Channel.Email,
                ToAddress = "kh@dongdo.vn", Subject = "Hóa đơn 1C26TAA-00000001 đã phát hành",
                Body = "Kính gửi Nguyễn Văn A,\nHóa đơn 1C26TAA-00000001 trị giá 550.000.000đ đã được phát hành.",
                Status = NotiStatus.Delivered, Provider = "SMTP", ResultCode = "200", SentAt = DateTime.UtcNow.AddMinutes(-30), DeliveredAt = DateTime.UtcNow.AddMinutes(-29) });
            await db.SaveChangesAsync();
        }
    }

    private static async Task MigratePostgresAsync(AppDbContext db)
    {
        if (!db.Database.IsNpgsql()) return;
        var def = TenantContext.DefaultOrgId;
        var tables = new[] { "Templates", "Notifications" };
        var sql = new List<string> {
            "CREATE TABLE IF NOT EXISTS mininotify.\"Orgs\" (\"Id\" uuid PRIMARY KEY, \"Name\" text NOT NULL DEFAULT '', \"ApiKey\" text NOT NULL DEFAULT '', \"CreatedAt\" timestamp NOT NULL DEFAULT now())",
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Orgs_ApiKey\" ON mininotify.\"Orgs\" (\"ApiKey\")" };
        foreach (var t in tables) sql.Add($"ALTER TABLE mininotify.\"{t}\" ADD COLUMN IF NOT EXISTS \"OrgId\" uuid NOT NULL DEFAULT '{def}'");
        foreach (var s in sql) try { await db.Database.ExecuteSqlRawAsync(s); } catch { }
    }
}
