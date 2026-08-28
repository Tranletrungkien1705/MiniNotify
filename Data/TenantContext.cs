namespace MiniNotify.Data;
public interface ITenantContext { Guid OrgId { get; set; } }
public sealed class TenantContext : ITenantContext
{
    public static readonly Guid DefaultOrgId = new("eeeeeeee-1111-4000-8000-000000000001");
    public const string DefaultApiKey = "demo-notify";
    public const string CookieName = "org_key";
    public Guid OrgId { get; set; } = DefaultOrgId;
}
