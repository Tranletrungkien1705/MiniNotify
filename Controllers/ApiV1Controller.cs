using Microsoft.AspNetCore.Mvc;
using MiniNotify.Data;
using MiniNotify.Models;
using MiniNotify.Services;

namespace MiniNotify.Controllers;

/// <summary>
/// API JSON cho SPA React. DTO phẳng. Dashboard cache Redis 30s theo tenant (X-Cache).
/// Hub thông báo đa kênh (Email/SMS/Zalo/Push): mẫu {{placeholder}} + gửi + trạng thái Queued→Sent→Delivered/Failed + retry.
/// </summary>
[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public class ApiV1Controller(INotifyService svc, ICache cache, ITenantContext tenant) : ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        var key = $"noti:dash:{tenant.OrgId}";
        var hit = await cache.GetAsync<DashDto>(key);
        if (hit != null) { Response.Headers["X-Cache"] = "HIT"; return Ok(hit); }
        var d = await svc.DashboardAsync();
        var dto = new DashDto(d.Total, d.Sent, d.Delivered, d.Failed,
            d.ByChannel.Select(kv => new ByChannelDto(Ui.Chan(kv.Key).text, kv.Value)).ToList());
        await cache.SetAsync(key, dto, TimeSpan.FromSeconds(30));
        Response.Headers["X-Cache"] = "MISS";
        return Ok(dto);
    }

    [HttpGet("templates")]
    public async Task<IActionResult> Templates()
        => Ok((await svc.TemplatesAsync()).Select(t => new { t.Id, t.Code, t.Name, channel = (int)t.Channel, channelText = Ui.Chan(t.Channel).text, t.Subject, t.Body, t.Active }));

    [HttpPost("templates")]
    public async Task<IActionResult> SaveTemplate([FromBody] TemplateReq r)
    {
        var (ok, msg, id) = await svc.SaveTemplateAsync(new Template
        {
            Id = r.Id, Code = r.Code ?? "", Name = r.Name, Channel = (Channel)r.Channel, Subject = r.Subject ?? "", Body = r.Body ?? "", Active = r.Active
        });
        return ok ? Ok(new { id }) : BadRequest(new { error = msg });
    }

    [HttpGet("notifications")]
    public async Task<IActionResult> Notifications([FromQuery] NotiStatus? status, [FromQuery] Channel? channel)
        => Ok((await svc.NotificationsAsync(status, channel)).Select(ToDto));

    [HttpGet("notifications/{id:int}")]
    public async Task<IActionResult> Notification(int id)
    {
        var n = await svc.GetNotificationAsync(id);
        return n == null ? NotFound(new { error = "Không tìm thấy." }) : Ok(ToDto(n));
    }

    [HttpPost("send")]
    public async Task<IActionResult> Send([FromBody] SendReq r)
    {
        var res = await svc.SendDirectAsync((Channel)r.Channel, r.To ?? "", r.Subject ?? "", r.Body ?? "");
        return res.ok ? Ok(new { id = res.id, status = res.status, msg = res.msg }) : BadRequest(new { error = res.msg });
    }

    [HttpPost("send-template")]
    public async Task<IActionResult> SendTemplate([FromBody] SendTemplateReq r)
    {
        var res = await svc.SendByTemplateAsync(r.Code ?? "", r.To ?? "", r.Data ?? new());
        return res.ok ? Ok(new { id = res.id, status = res.status, msg = res.msg }) : BadRequest(new { error = res.msg });
    }

    [HttpPost("notifications/{id:int}/retry")]
    public async Task<IActionResult> Retry(int id)
    {
        var (ok, msg) = await svc.RetryAsync(id);
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }

    [HttpPost("notifications/{id:int}/delivered")]
    public async Task<IActionResult> Delivered(int id)
    {
        var (ok, msg) = await svc.MarkDeliveredAsync(id);
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }

    private static object ToDto(Notification n) => new
    {
        n.Id, channel = (int)n.Channel, channelText = Ui.Chan(n.Channel).text, n.ToAddress, n.Subject, n.Body,
        status = (int)n.Status, statusText = Ui.Stat(n.Status).text, statusCss = Ui.Stat(n.Status).css,
        n.Provider, n.ResultCode, n.Error, n.RetryCount, n.SentAt, n.DeliveredAt, n.CreatedAt, template = n.TemplateName
    };
}

public record DashDto(int Total, int Sent, int Delivered, int Failed, List<ByChannelDto> ByChannel);
public record ByChannelDto(string Channel, int Count);

public class TemplateReq { public int Id { get; set; } public string? Code { get; set; } public string Name { get; set; } = ""; public int Channel { get; set; } public string? Subject { get; set; } public string? Body { get; set; } public bool Active { get; set; } = true; }
public class SendReq { public int Channel { get; set; } public string? To { get; set; } public string? Subject { get; set; } public string? Body { get; set; } }
public class SendTemplateReq { public string? Code { get; set; } public string? To { get; set; } public Dictionary<string, string>? Data { get; set; } }
