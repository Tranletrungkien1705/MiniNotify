using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiniNotify.Data;
using MiniNotify.Models;
using MiniNotify.Services;

namespace MiniNotify.Controllers;

public class HomeController : Controller
{
    public IActionResult Index() => Redirect("/index.html");   // SPA React ở "/"
}

public class LegacyController(INotifyService svc) : Controller
{
    public async Task<IActionResult> Index() { ViewBag.Dash = await svc.DashboardAsync(); return View("~/Views/Home/Index.cshtml"); }
}

public class TemplateController(INotifyService svc) : Controller
{
    public async Task<IActionResult> Index() => View(await svc.TemplatesAsync());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(int id, string code, string name, Channel channel, string? subject, string body, bool active)
    {
        var (ok, msg, _) = await svc.SaveTemplateAsync(new Template { Id = id, Code = (code ?? "").Trim(), Name = name ?? "", Channel = channel, Subject = subject ?? "", Body = body ?? "", Active = active });
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Index));
    }

    // Gửi thử theo mẫu (data nhập dạng key=value mỗi dòng)
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SendTest(string code, string to, string? data)
    {
        var dict = ParseData(data);
        var r = await svc.SendByTemplateAsync(code, to, dict);
        TempData[r.ok ? "Success" : "Error"] = r.ok ? $"Đã gửi (#{r.id}, {r.status})." : r.msg;
        return RedirectToAction("Index", "Notification");
    }

    private static Dictionary<string, string> ParseData(string? data)
    {
        var d = new Dictionary<string, string>();
        foreach (var line in (data ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = line.IndexOf('=');
            if (i > 0) d[line[..i].Trim()] = line[(i + 1)..].Trim();
        }
        return d;
    }
}

public class NotificationController(INotifyService svc) : Controller
{
    public async Task<IActionResult> Index(NotiStatus? status, Channel? channel)
    {
        ViewBag.Status = status; ViewBag.Channel = channel;
        return View(await svc.NotificationsAsync(status, channel));
    }

    public async Task<IActionResult> Detail(int id)
    {
        var n = await svc.GetNotificationAsync(id);
        return n == null ? NotFound() : View(n);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Retry(int id)
    {
        var (ok, msg) = await svc.RetryAsync(id);
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Deliver(int id)
    {
        var (ok, msg) = await svc.MarkDeliveredAsync(id);
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }
}

public class OrgController(AppDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        Request.Cookies.TryGetValue(TenantContext.CookieName, out var curKey);
        ViewBag.CurrentKey = curKey ?? TenantContext.DefaultApiKey;
        return View(await db.Orgs.IgnoreQueryFilters().OrderBy(o => o.CreatedAt).ToListAsync());
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) { TempData["Error"] = "Cần tên tổ chức."; return RedirectToAction(nameof(Index)); }
        var org = new Org { Name = name.Trim(), ApiKey = "ntf_" + Guid.NewGuid().ToString("N") };
        db.Orgs.Add(org); await db.SaveChangesAsync();
        SetCookies(org.ApiKey, org.Name);
        TempData["Success"] = $"Đã tạo & chuyển sang \"{org.Name}\"."; return RedirectToAction("Index", "Home");
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Switch(string apiKey)
    {
        var org = await db.Orgs.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.ApiKey == apiKey);
        if (org == null) { TempData["Error"] = "Không tìm thấy."; return RedirectToAction(nameof(Index)); }
        SetCookies(org.ApiKey, org.Name); return RedirectToAction("Index", "Home");
    }
    private void SetCookies(string k, string n)
    {
        var o = new CookieOptions { IsEssential = true, Expires = DateTimeOffset.UtcNow.AddDays(30) };
        Response.Cookies.Append(TenantContext.CookieName, k, o); Response.Cookies.Append("org_name", n, o);
    }
}
