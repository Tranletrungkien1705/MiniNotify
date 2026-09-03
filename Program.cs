using Microsoft.EntityFrameworkCore;
using MiniNotify.Data;
using MiniNotify.Models;
using MiniNotify.Services;
using Serilog;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
FleetObs.ConfigureLogger("mininotify");

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();
builder.WebHost.UseUrls($"http://0.0.0.0:{Environment.GetEnvironmentVariable("PORT") ?? "8080"}");

var conn = Environment.GetEnvironmentVariable("CONNECTION_STRING")
    ?? builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=mininotify.db";
builder.Services.AddDbContext<AppDbContext>(o =>
{
    if (DbUtil.IsPostgres(conn)) o.UseNpgsql(DbUtil.ToNpgsql(conn));
    else o.UseSqlite(conn);
});
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<INotifyService, NotifyService>();
builder.Services.AddFleetObs();
builder.Services.AddControllersWithViews();

var app = builder.Build();
using (var scope = app.Services.CreateScope())
    await Seeder.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>());

app.UseFleetObs();

app.Use(async (ctx, next) =>
{
    var key = ctx.Request.Headers["X-Api-Key"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(key)) ctx.Request.Cookies.TryGetValue(TenantContext.CookieName, out key);
    if (!string.IsNullOrWhiteSpace(key))
    {
        using var lookup = app.Services.CreateScope();
        var ldb = lookup.ServiceProvider.GetRequiredService<AppDbContext>();
        var org = await ldb.Orgs.FirstOrDefaultAsync(o => o.ApiKey == key);
        if (org != null) ctx.RequestServices.GetRequiredService<ITenantContext>().OrgId = org.Id;
    }
    await next();
});

app.UseStaticFiles();
app.MapGet("/healthz", () => "ok");
app.MapGet("/api/summary", async (INotifyService svc) =>
{
    var d = await svc.DashboardAsync();
    return Results.Ok(new { total = d.Total, sent = d.Sent, delivered = d.Delivered, failed = d.Failed });
});

// API gửi thông báo (cần X-Api-Key) — theo mẫu + data, hoặc trực tiếp.
app.MapPost("/api/send", async (SendDto dto, INotifyService svc) =>
{
    SendResult r;
    if (!string.IsNullOrWhiteSpace(dto.TemplateCode))
        r = await svc.SendByTemplateAsync(dto.TemplateCode!, dto.To ?? "", dto.Data ?? new());
    else if (Enum.TryParse<Channel>(dto.Channel, true, out var ch))
        r = await svc.SendDirectAsync(ch, dto.To ?? "", dto.Subject ?? "", dto.Body ?? "");
    else return Results.BadRequest(new { error = "Cần templateCode hoặc channel hợp lệ." });
    return r.ok ? Results.Ok(new { id = r.id, status = r.status }) : Results.BadRequest(new { id = r.id, status = r.status, error = r.msg });
});

// Webhook nhà cung cấp báo đã đến người nhận.
app.MapPost("/api/delivery/callback", async (DeliveryDto dto, INotifyService svc) =>
{
    var (ok, msg) = await svc.MarkDeliveredAsync(dto.Id);
    return ok ? Results.Ok(new { delivered = true }) : Results.BadRequest(new { error = msg });
});

app.MapPost("/api/orgs/register", async (RegisterOrgDto dto, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(dto.Name)) return Results.BadRequest(new { error = "Cần Name." });
    var org = new Org { Name = dto.Name.Trim(), ApiKey = "ntf_" + Guid.NewGuid().ToString("N") };
    db.Orgs.Add(org); await db.SaveChangesAsync();
    return Results.Ok(new { orgId = org.Id, apiKey = org.ApiKey });
});

// Import mẫu thông báo thật từ nghiệp vụ HTC (dedupe theo Code)
app.MapPost("/api/import/templates", async (List<ImportTemplateDto> rows, AppDbContext db, ITenantContext tc) =>
{
    if (rows == null || rows.Count == 0) return Results.BadRequest(new { error = "Không có dữ liệu." });
    int added = 0, skipped = 0;
    var orgId = tc.OrgId;
    var existCodes = db.Templates.Where(t => t.OrgId == orgId).Select(t => t.Code).ToHashSet();
    foreach (var row in rows)
    {
        if (string.IsNullOrWhiteSpace(row.Code)) { skipped++; continue; }
        if (existCodes.Contains(row.Code.Trim())) { skipped++; continue; }
        db.Templates.Add(new Template
        {
            OrgId = orgId, Code = row.Code.Trim(), Name = row.Name?.Trim() ?? row.Code.Trim(),
            Channel = (Channel)Math.Clamp(row.Channel, 0, 3),
            Subject = row.Subject?.Trim() ?? "", Body = row.Body?.Trim() ?? "", Active = true
        });
        existCodes.Add(row.Code.Trim()); added++;
    }
    await db.SaveChangesAsync();
    return Results.Ok(new { added, skipped, total = added + skipped });
});

// Import lịch sử thông báo thật (dedupe theo ToAddress+Subject, lấy sent/delivered)
app.MapPost("/api/import/notifications", async (List<ImportNotifDto> rows, AppDbContext db, ITenantContext tc) =>
{
    if (rows == null || rows.Count == 0) return Results.BadRequest(new { error = "Không có dữ liệu." });
    int added = 0;
    var orgId = tc.OrgId;
    foreach (var row in rows)
    {
        if (string.IsNullOrWhiteSpace(row.To)) { continue; }
        db.Notifications.Add(new Notification
        {
            OrgId = orgId, Channel = (Channel)Math.Clamp(row.Channel, 0, 3),
            ToAddress = row.To.Trim(), Subject = row.Subject?.Trim() ?? "",
            Body = row.Body?.Trim() ?? "", Status = NotiStatus.Sent,
            Provider = row.Provider ?? "import", SentAt = row.SentAt ?? DateTime.Now,
            DeliveredAt = row.SentAt ?? DateTime.Now
        });
        added++;
    }
    await db.SaveChangesAsync();
    return Results.Ok(new { added, total = added });
});

app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");
app.Run();

record SendDto(string? TemplateCode, string? Channel, string? To, string? Subject, string? Body, Dictionary<string, string>? Data);
record DeliveryDto(int Id);
record RegisterOrgDto(string Name);
record ImportTemplateDto(string? Code, string? Name, int Channel, string? Subject, string? Body);
record ImportNotifDto(string? To, int Channel, string? Subject, string? Body, string? Provider, DateTime? SentAt);
