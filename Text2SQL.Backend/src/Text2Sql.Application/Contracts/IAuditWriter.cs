namespace Text2Sql.Application.Contracts
{
    /// <summary>Denetim olayı — hassas veri (parola, bağlantı dizesi, sorgu sonucu) İÇERMEZ.</summary>
    public sealed record AuditEvent(
        string Action,
        string? TargetType = null,
        string? TargetId = null,
        string? Summary = null,
        int? OverrideCompanyId = null,
        int? OverrideActorUserId = null,
        string? OverrideActorEmail = null);

    /// <summary>
    /// SaaS-3: Denetim kaydı portu.
    ///
    /// İki yazım modu bilinçli olarak ayrıldı:
    /// - <see cref="Write"/>: kaydı mevcut DbContext'e EKLER, kaydetmez. Çağıranın
    ///   SaveChanges/transaction'ı ile ATOMİK ilerler — iş başarısız olursa denetim
    ///   kaydı da yazılmaz (yanıltıcı kayıt oluşmaz).
    /// - <see cref="WriteAndSaveAsync"/>: bağımsız kaydeder. Başarısız giriş gibi
    ///   "iş işlemi olmayan" güvenlik olayları için.
    /// </summary>
    public interface IAuditWriter
    {
        void Write(AuditEvent auditEvent);
        Task WriteAndSaveAsync(AuditEvent auditEvent, CancellationToken ct = default);
    }

    public static class AuditActions
    {
        public const string LoginSucceeded      = "auth.login.succeeded";
        public const string LoginFailed         = "auth.login.failed";
        public const string OrganizationSwitched = "auth.organization.switched";
        public const string OrganizationCreated = "organization.created";
        public const string MemberInvited       = "member.invited";
        public const string MemberJoined        = "member.joined";
        public const string MemberApproved      = "member.approved";
        public const string MemberRejected      = "member.rejected";
        public const string MemberSuspended     = "member.suspended";
        public const string MemberRoleChanged   = "member.role_changed";
        public const string MemberRemoved       = "member.removed";
        public const string DataSourceCreated   = "datasource.created";
        public const string DataSourceDeleted   = "datasource.deleted";
        public const string ProjectDeleted      = "project.deleted";
        public const string ApiKeyCreated      = "apikey.created";
        public const string ApiKeyRevoked      = "apikey.revoked";
        public const string PlanChanged        = "billing.plan_changed";
        public const string LoginBlockedLockout        = "auth.login.blocked_lockout";
        public const string RefreshTokenReuseDetected  = "auth.refresh_token.reuse_detected";
        public const string EmailConfirmed             = "auth.email.confirmed";
        public const string TenantDataExported        = "organization.data_exported";
        public const string TenantDeleted             = "organization.deleted";
    }
}
