namespace MiniNotify.Models;

public interface IOrgOwned { Guid OrgId { get; set; } }

public enum Channel { Email = 0, Sms = 1, Zalo = 2, Push = 3 }
public enum NotiStatus { Queued = 0, Sent = 1, Delivered = 2, Failed = 3 }

public class Org
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// Mẫu thông báo có {{placeholder}}
public class Template : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public Channel Channel { get; set; }
    public string Subject { get; set; } = "";        // (Email/Push có tiêu đề)
    public string Body { get; set; } = "";
    public bool Active { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// Một lần gửi thông báo
public class Notification : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public int? TemplateId { get; set; }
    public string? TemplateName { get; set; }
    public Channel Channel { get; set; }
    public string ToAddress { get; set; } = "";      // email / SĐT / device token
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public NotiStatus Status { get; set; } = NotiStatus.Queued;
    public string? Provider { get; set; }            // SMTP / SMSBrand / ZaloOA / FCM
    public string? ResultCode { get; set; }
    public string? Error { get; set; }
    public int RetryCount { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
