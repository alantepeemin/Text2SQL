using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Text2Sql.Application.Common.Exceptions;
using Text2Sql.Application.Contracts;
using Text2Sql.Application.DTOs.Gdpr;

namespace Text2Sql.Application.Services
{
    public class TenantDataService : ITenantDataService
    {
        private readonly IAppDbContext _context;
        private readonly ICurrentUserContext _currentUser;
        private readonly IAuditWriter _audit;
        private readonly IFileStorage _fileStorage;
        private readonly ILogger<TenantDataService> _logger;

        public TenantDataService(
            IAppDbContext context,
            ICurrentUserContext currentUser,
            IAuditWriter audit,
            IFileStorage fileStorage,
            ILogger<TenantDataService> logger)
        {
            _context = context;
            _currentUser = currentUser;
            _audit = audit;
            _fileStorage = fileStorage;
            _logger = logger;
        }

        public async Task<TenantExportDto> ExportAsync(CancellationToken ct = default)
        {
            var tenantId = _currentUser.TenantId;

            // Global kiracı filtresi sayesinde tüm sorgular otomatik olarak
            // yalnızca bu organizasyonun verisini döndürür.
            var company = await _context.Companies
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == tenantId, ct)
                ?? throw new NotFoundException("Organizasyon bulunamadı.");

            var planCode = await _context.Subscriptions
                .AsNoTracking()
                .Include(s => s.Plan)
                .Where(s => s.Status != "canceled")
                .Select(s => s.Plan.Code)
                .FirstOrDefaultAsync(ct);

            var export = new TenantExportDto
            {
                Organization = new OrganizationExport
                {
                    Name      = company.Name,
                    Domain    = company.Domain,
                    PlanCode  = planCode,
                    CreatedAt = company.CreatedAt
                },

                Members = await _context.Memberships
                    .AsNoTracking()
                    .Include(m => m.User)
                    .Select(m => new MemberExport
                    {
                        Username    = m.User.Username,
                        Email       = m.User.Email,
                        Role        = m.Role,
                        Status      = m.Status,
                        JoinedAt    = m.JoinedAt,
                        LastLoginAt = m.User.LastLoginAt
                    })
                    .ToListAsync(ct),

                Projects = await _context.Projects
                    .AsNoTracking()
                    .Select(p => new ProjectExport
                    {
                        Name        = p.Name,
                        Description = p.Description,
                        CreatedAt   = p.CreatedAt,
                        Members     = p.AccessList.Select(a => a.User.Email).ToList()
                    })
                    .ToListAsync(ct),

                DataSources = await _context.ProjectDatabases
                    .AsNoTracking()
                    .Select(d => new DataSourceExport
                    {
                        Name         = d.ConnectionName,
                        DbType       = d.DbType,
                        Host         = d.Host,
                        DatabaseName = d.DatabaseName,
                        CreatedAt    = d.CreatedAt
                    })
                    .ToListAsync(ct),

                // Sorgu geçmişi büyük olabilir; ihraçta son 5000 kayıt
                // (daha fazlası için destek talebi — bellek koruması)
                Queries = await _context.QueryHistories
                    .AsNoTracking()
                    .OrderByDescending(h => h.CreatedAt)
                    .Take(5000)
                    .Select(h => new QueryExport
                    {
                        Question     = h.Question,
                        Sql          = h.SqlQuery,
                        IsSuccessful = h.IsSuccessful,
                        CreatedAt    = h.CreatedAt,
                        User         = h.User.Email
                    })
                    .ToListAsync(ct),

                AuditLogs = await _context.AuditLogs
                    .AsNoTracking()
                    .OrderByDescending(a => a.OccurredAt)
                    .Take(5000)
                    .Select(a => new AuditExport
                    {
                        Action     = a.Action,
                        Actor      = a.ActorEmail,
                        Summary    = a.Summary,
                        OccurredAt = a.OccurredAt
                    })
                    .ToListAsync(ct)
            };

            var usage = await _context.UsageRecords.AsNoTracking()
                .GroupBy(u => 1)
                .Select(g => new UsageExport
                {
                    TotalTokens           = g.Sum(u => (long)u.PromptTokens + u.CompletionTokens),
                    TotalEstimatedCostUsd = g.Sum(u => u.EstimatedCostUsd),
                    TotalQueries          = g.Count()
                })
                .FirstOrDefaultAsync(ct);

            export.Usage = usage ?? new UsageExport();

            _audit.Write(new AuditEvent(AuditActions.TenantDataExported,
                TargetType: "Organization", TargetId: tenantId.ToString(),
                Summary: $"Veri ihracı: {export.Members.Count} üye, {export.Projects.Count} proje, {export.Queries.Count} sorgu"));
            await _context.SaveChangesAsync(ct);

            _logger.LogInformation("Kiracı veri ihracı tamamlandı: Tenant={Tenant}", tenantId);
            return export;
        }

        public async Task DeleteTenantAsync(string confirmationText, CancellationToken ct = default)
        {
            var tenantId = _currentUser.TenantId;

            var company = await _context.Companies
                .FirstOrDefaultAsync(c => c.Id == tenantId, ct)
                ?? throw new NotFoundException("Organizasyon bulunamadı.");

            // YANLIŞLIKLA SİLMEYE KARŞI: kullanıcı organizasyon adını birebir yazmalı.
            // Bu, "emin misiniz?" onay kutusundan daha güçlü bir bariyerdir çünkü
            // otomatik/kazara istekle geçilemez.
            if (!string.Equals(confirmationText, company.Name, StringComparison.Ordinal))
                throw new BadRequestException(
                    "Onay metni organizasyon adıyla birebir eşleşmiyor. Silme işlemi iptal edildi.");

            // Denetim kaydı SİLME ÖNCESİ yazılır ve platform düzeyine (CompanyId=0)
            // alınır — aksi halde kaydın kendisi cascade ile silinirdi ve
            // "kim sildi?" sorusu cevapsız kalırdı.
            await _audit.WriteAndSaveAsync(new AuditEvent(AuditActions.TenantDeleted,
                TargetType: "Organization", TargetId: tenantId.ToString(),
                Summary: $"'{company.Name}' organizasyonu ve tüm verisi silindi",
                OverrideCompanyId: 0,
                OverrideActorUserId: _currentUser.UserId), ct);

            // Sonuç dosyaları ve yüklenen SQLite dosyaları (DB cascade bunları bilmez)
            try
            {
                await _fileStorage.DeleteDirectoryAsync(tenantId.ToString(), ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Kiracı dosyaları silinemedi (DB kayıtları silinmeye devam edecek): Tenant={Tenant}", tenantId);
            }

            // ── Kullanıcı hesapları: iki farklı davranış ────────────────────
            // Company silinince Memberships/Projects/DataSources/Subscriptions
            // cascade ile gider. Kullanıcı hesapları için politika:
            //
            //  (a) BAŞKA organizasyonda üyeliği olanlar KORUNUR — başka bir
            //      müşterinin admin'i onların hesabını silemez.
            //  (b) Tek üyeliği bu organizasyonda olanlar SİLİNİR — hesabın
            //      artık hiçbir amacı yok; tutmak "amaçsız veri saklama"
            //      olur (KVKK/GDPR ilkesine aykırı) ve giriş yapamayan
            //      yetim hesaplar birikir.
            var buKiracininUyeIdleri = await _context.Memberships
                .Where(m => m.CompanyId == tenantId)
                .Select(m => m.UserId)
                .ToListAsync(ct);

            var baskaUyeligiOlanlar = await _context.Memberships
                .IgnoreQueryFilters()   // diğer organizasyonlara bakmak zorundayız
                .Where(m => buKiracininUyeIdleri.Contains(m.UserId) && m.CompanyId != tenantId)
                .Select(m => m.UserId)
                .Distinct()
                .ToListAsync(ct);

            var silinecekKullaniciIdleri = buKiracininUyeIdleri
                .Except(baskaUyeligiOlanlar)
                .ToList();

            _context.Companies.Remove(company);
            await _context.SaveChangesAsync(ct);

            if (silinecekKullaniciIdleri.Count > 0)
            {
                // Company silindikten SONRA: cascade zaten üyelikleri temizledi,
                // burada yalnızca amacı kalmayan hesapları kaldırıyoruz.
                var silinen = await _context.Users
                    .Where(u => silinecekKullaniciIdleri.Contains(u.Id))
                    .ExecuteDeleteAsync(ct);

                _logger.LogInformation(
                    "Kiracı silme: {Deleted} yetim kullanıcı hesabı silindi, {Kept} hesap korundu (başka organizasyonda üyelikleri var).",
                    silinen, baskaUyeligiOlanlar.Count);
            }

            _logger.LogWarning("KİRACI SİLİNDİ: Tenant={Tenant}, Ad={Name}", tenantId, company.Name);
        }
    }
}
