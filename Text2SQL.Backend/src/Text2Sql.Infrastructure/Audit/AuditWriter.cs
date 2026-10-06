using Microsoft.AspNetCore.Http;
using Text2Sql.Application.Contracts;
using Text2Sql.Domain.Entities;

namespace Text2Sql.Infrastructure.Audit
{
    public sealed class AuditWriter : IAuditWriter
    {
        private const int MaxSummaryLength = 500;

        private readonly IAppDbContext _context;
        private readonly ICurrentUserContext _currentUser;
        private readonly IHttpContextAccessor _http;

        public AuditWriter(
            IAppDbContext context,
            ICurrentUserContext currentUser,
            IHttpContextAccessor http)
        {
            _context = context;
            _currentUser = currentUser;
            _http = http;
        }

        public void Write(AuditEvent auditEvent) => _context.AuditLogs.Add(Build(auditEvent));

        public async Task WriteAndSaveAsync(AuditEvent auditEvent, CancellationToken ct = default)
        {
            _context.AuditLogs.Add(Build(auditEvent));
            await _context.SaveChangesAsync(ct);
        }

        private AuditLog Build(AuditEvent e)
        {
            var httpContext = _http.HttpContext;

            // Kiracı/aktör bağlamı isteğe göre otomatik doldurulur; anonim akışlar
            // (başarısız giriş) Override* alanlarıyla açıkça belirtir.
            var companyId = e.OverrideCompanyId
                            ?? (_currentUser.HasTenant ? _currentUser.TenantId : 0);

            var actorUserId = e.OverrideActorUserId
                              ?? (_currentUser.HasTenant ? _currentUser.UserId : (int?)null);

            return new AuditLog
            {
                CompanyId   = companyId,
                ActorUserId = actorUserId,
                ActorEmail  = e.OverrideActorEmail,
                Action      = e.Action,
                TargetType  = e.TargetType,
                TargetId    = e.TargetId,
                Summary     = e.Summary is { Length: > MaxSummaryLength }
                                ? e.Summary[..MaxSummaryLength]
                                : e.Summary,
                IpAddress   = httpContext?.Connection.RemoteIpAddress?.ToString(),
                UserAgent   = httpContext?.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua
                                ? (ua.Length > 256 ? ua[..256] : ua)
                                : null,
                OccurredAt  = DateTime.UtcNow
            };
        }
    }
}
