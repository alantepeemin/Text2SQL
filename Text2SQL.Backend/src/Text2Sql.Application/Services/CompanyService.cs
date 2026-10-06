using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Text2Sql.Application.Common;
using Text2Sql.Application.Contracts;
using Text2Sql.Domain.Entities;
using Text2Sql.Application.DTOs.Company;

namespace Text2Sql.Application.Services
{
    public class CompanyService : ICompanyService
    {
        private readonly IAppDbContext _context;
        private readonly ICurrentUserContext  _currentUser;
        private readonly ILogger<CompanyService> _logger;

        private readonly IMemoryCache _cache;
        private readonly IEmailSender _emailSender;
        private readonly IAuditWriter _audit;
        private readonly IPlanLimitService _planLimits;
        private readonly IConfiguration _configuration;

        public CompanyService(
            IAppDbContext context,
            ICurrentUserContext currentUser,
            IMemoryCache cache,
            IEmailSender emailSender,
            IAuditWriter audit,
            IPlanLimitService planLimits,
            IConfiguration configuration,
            ILogger<CompanyService> logger)
        {
            _context       = context;
            _currentUser   = currentUser;
            _cache         = cache;
            _emailSender   = emailSender;
            _audit         = audit;
            _planLimits    = planLimits;
            _configuration = configuration;
            _logger        = logger;
        }

        public async Task<bool> InviteUserAsync(InviteUserDto dto)
        {
            var company = await _context.Companies
                .FirstOrDefaultAsync(c => c.Id == _currentUser.TenantId)
                ?? throw new KeyNotFoundException("Şirket bulunamadı.");

            // SaaS-5: Plan üye limiti (organizasyonun MaxUsers alanından bağımsız,
            // plan bazlı gerçek limit). İkisi birlikte çalışır: hangisi daha
            // kısıtlayıcıysa o uygulanır.
            await _planLimits.EnsureCanAddMemberAsync();

            // SaaS-2: Limit ÜYELİK sayısı üzerinden (kullanıcı sayısı değil)
            var activeMemberCount = await _context.Memberships
                .CountAsync(m => m.CompanyId == _currentUser.TenantId && m.IsActive);

            if (activeMemberCount >= company.MaxUsers)
                throw new InvalidOperationException("Organizasyon kullanıcı limiti dolmuş.");

            // SaaS-2: Bu ORGANİZASYONDA üyeliği var mı? (Başka organizasyonda
            // hesabı olması artık engel DEĞİL — çok organizasyonlu üyelik.)
            var zatenUye = await _context.Memberships
                .Include(m => m.User)
                .AnyAsync(m => m.CompanyId == _currentUser.TenantId && m.User.Email == dto.Email);

            if (zatenUye)
                throw new ArgumentException("Bu email bu organizasyonda zaten üye.");

            // Bekleyen aktif davetiye var mı?
            var existingInvite = await _context.CompanyInvitations
                .FirstOrDefaultAsync(i =>
                    i.CompanyId == _currentUser.TenantId &&
                    i.Email     == dto.Email &&
                    !i.IsUsed   &&
                    i.ExpiresAt > DateTime.UtcNow);

            if (existingInvite != null)
                throw new InvalidOperationException("Bu email için bekleyen aktif bir davetiye zaten mevcut.");

            var invitation = new CompanyInvitation
            {
                CompanyId       = _currentUser.TenantId,
                Email           = dto.Email,
                Role            = dto.Role,
                InvitedBy       = _currentUser.UserId,
                InvitationToken = Guid.NewGuid().ToString("N"),
                ExpiresAt       = DateTime.UtcNow.AddDays(7),
                CreatedAt       = DateTime.UtcNow
            };

            _context.CompanyInvitations.Add(invitation);
            await _context.SaveChangesAsync();

            // FAZ 4: Davetiye artık GERÇEKTEN e-posta ile iletilir.
            // Gönderim best-effort: SMTP hatası daveti geçersiz kılmaz
            // (token yine listede görünür, link elle paylaşılabilir).
            try
            {
                var baseUrl = _configuration["Email:InvitationBaseUrl"] ?? "http://localhost:3001";
                var link    = $"{baseUrl.TrimEnd('/')}/accept-invitation?token={invitation.InvitationToken}";

                await _emailSender.SendAsync(new EmailMessage(
                    dto.Email,
                    $"{company.Name} sizi Text2SQL'e davet ediyor",
                    $"""
                    <p>Merhaba,</p>
                    <p><strong>{company.Name}</strong> şirketi sizi Text2SQL platformuna
                    <strong>{dto.Role}</strong> rolüyle davet ediyor.</p>
                    <p><a href="{link}">Daveti kabul etmek için tıklayın</a></p>
                    <p>Bu bağlantı 7 gün geçerlidir.</p>
                    """));
            }
            catch (Exception mailEx)
            {
                _logger.LogError(mailEx,
                    "Davetiye e-postası gönderilemedi (davet kaydı geçerli): {Email}", dto.Email);
            }

            _audit.Write(new AuditEvent(AuditActions.MemberInvited,
                TargetType: "Invitation", TargetId: invitation.Id.ToString(),
                Summary: $"{dto.Email} davet edildi (rol: {dto.Role})"));
            await _context.SaveChangesAsync();

            _logger.LogInformation("Davetiye oluşturuldu: {Email}, Rol: {Role}, Gönderen: {By}",
                dto.Email, dto.Role, _currentUser.UserId);

            return true;
        }

        public async Task<List<PendingUserDto>> GetPendingUsersAsync()
        {
            // SaaS-2: Onay bekleyenler ÜYELİK tablosundan gelir (global kiracı
            // filtresi sayesinde yalnızca bu organizasyonun üyelikleri).
            return await _context.Memberships
                .Include(m => m.User)
                .Where(m => m.Status == "pending")
                .OrderBy(m => m.JoinedAt)
                .Select(m => new PendingUserDto
                {
                    Id             = m.UserId,
                    Username       = m.User.Username,
                    Email          = m.User.Email,
                    Role           = m.Role,
                    Status         = m.Status,
                    InvitationType = m.InvitationType,
                    InvitedBy      = m.InvitedBy != null
                        ? _context.Users.Where(u => u.Id == m.InvitedBy).Select(u => u.Username).FirstOrDefault()
                        : null,
                    CreatedAt      = m.JoinedAt,
                    InvitedAt      = m.InvitedAt,
                    LastIpAddress  = m.User.LastIpAddress
                })
                .ToListAsync();
        }

        public async Task<bool> ApproveUserAsync(UserApprovalDto dto)
        {
            // SaaS-2: Onay ÜYELİK üzerinde yapılır — kullanıcı hesabı organizasyondan
            // bağımsızdır. Global filtre sayesinde başka organizasyonun üyeliği
            // buradan görünmez/değiştirilemez.
            var membership = await _context.Memberships
                .Include(m => m.User).ThenInclude(u => u.TokenInfo)
                .FirstOrDefaultAsync(m => m.UserId == dto.UserId && m.CompanyId == _currentUser.TenantId)
                ?? throw new KeyNotFoundException("Üyelik bulunamadı.");

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var user = membership.User;

                switch (dto.Action.ToLowerInvariant())
                {
                    case "approve":
                        // İş kuralı entity'de (anemik domain düzeltmesi)
                        membership.Approve(_currentUser.UserId, dto.Role, dto.Notes);

                        // Kota kaydı yalnızca ilk onayda oluşur
                        if (user.TokenInfo == null)
                        {
                            var company = await _context.Companies
                                .FirstOrDefaultAsync(c => c.Id == _currentUser.TenantId);
                            _context.UserTokens.Add(new UserToken
                            {
                                UserId          = user.Id,
                                RemainingTokens = company?.MonthlyQueryLimit ?? 100,
                                MonthlyLimit    = company?.MonthlyQueryLimit ?? 100,
                                LastResetDate   = DateTime.UtcNow,
                                CreatedAt       = DateTime.UtcNow,
                                UpdatedAt       = DateTime.UtcNow
                            });
                        }
                        break;

                    case "reject":
                        membership.Reject(_currentUser.UserId, dto.Notes);
                        break;

                    case "suspend":
                        membership.Suspend(_currentUser.UserId, dto.Notes);
                        break;

                    default:
                        throw new ArgumentException(
                            $"Geçersiz işlem: '{dto.Action}'. approve, reject veya suspend olmalı.");
                }

                // SaaS-2a: legacy ayna (yalnızca birincil organizasyon için)
                if (membership.IsPrimary)
                {
                    user.Role          = membership.Role;
                    user.Status        = membership.Status;
                    user.ApprovedBy    = membership.ApprovedBy;
                    user.ApprovedAt    = membership.ApprovedAt;
                    user.ApprovalNotes = membership.ApprovalNotes;
                    user.UpdatedAt     = DateTime.UtcNow;
                }

                // Rol/durum değişimi mevcut oturumları düşürür
                user.InvalidateSecurityStamp();

                var auditAction = dto.Action.ToLowerInvariant() switch
                {
                    "approve" => AuditActions.MemberApproved,
                    "reject"  => AuditActions.MemberRejected,
                    _         => AuditActions.MemberSuspended
                };
                _audit.Write(new AuditEvent(auditAction,
                    TargetType: "Membership", TargetId: user.Id.ToString(),
                    Summary: $"{user.Email} → {membership.Status} (rol: {membership.Role})"));

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _cache.Remove(CacheKeys.SecurityStamp(user.Id));
                _cache.Remove(CacheKeys.Membership(user.Id, _currentUser.TenantId));

                _logger.LogInformation("Üyelik {Action}: UserId={UserId}, Organizasyon={Tenant}, Yönetici={By}",
                    dto.Action, dto.UserId, _currentUser.TenantId, _currentUser.UserId);

                return true;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<PagedResult<DetailedUserDto>> GetCompanyUsersAsync(int page = 1, int pageSize = 100)
        {
            page     = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 500);

            var toplamUye = await _context.Memberships.CountAsync();

            // SaaS-2: Organizasyonun üyeleri = Memberships (kiracı filtresi otomatik)
            var uyeler = await _context.Memberships
                .Include(m => m.User).ThenInclude(u => u.TokenInfo)
                .OrderBy(m => m.User.Username)
                .Select(m => new DetailedUserDto
                {
                    Id             = m.UserId,
                    Username       = m.User.Username,
                    Email          = m.User.Email,
                    Role           = m.Role,
                    Status         = m.Status,
                    InvitationType = m.InvitationType,
                    InvitedBy      = m.InvitedBy != null
                        ? _context.Users.Where(u => u.Id == m.InvitedBy).Select(u => u.Username).FirstOrDefault()
                        : null,
                    ApprovedBy     = m.ApprovedBy != null
                        ? _context.Users.Where(u => u.Id == m.ApprovedBy).Select(u => u.Username).FirstOrDefault()
                        : null,
                    CreatedAt      = m.JoinedAt,
                    LastLoginAt    = m.User.LastLoginAt,
                    LastActivityAt = m.User.LastActivityAt,
                    IsActive       = m.IsActive,
                    RemainingTokens = m.User.TokenInfo != null ? m.User.TokenInfo.RemainingTokens : 0,
                    MonthlyLimit    = m.User.TokenInfo != null ? m.User.TokenInfo.MonthlyLimit : 0,
                    // Bu organizasyondaki proje/sorgu sayıları (kiracı filtresi zaten uygular)
                    ProjectCount    = m.User.ProjectAccesses.Count(pa => pa.CompanyId == m.CompanyId),
                    LastQueryAt     = m.User.QueryHistories
                        .Where(q => q.CompanyId == m.CompanyId)
                        .OrderByDescending(q => q.CreatedAt)
                        .Select(q => (DateTime?)q.CreatedAt)
                        .FirstOrDefault(),
                    TotalQueries    = m.User.QueryHistories.Count(q => q.CompanyId == m.CompanyId)
                })
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return PagedResult<DetailedUserDto>.Create(uyeler, page, pageSize, toplamUye);
        }

        public async Task<UserActivitySummaryDto> GetUserActivitySummaryAsync()
        {
            var today     = DateTime.UtcNow.Date;
            var thisMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            // SaaS-2: Sayımlar ÜYELİK tablosundan
            var memberships = await _context.Memberships
                .Include(m => m.User)
                .Where(m => m.CompanyId == _currentUser.TenantId) // explicit tenant (derinlemesine savunma)
                .ToListAsync();

            // ✅ Tenant filtresi eklendi — sadece bu şirketin sorguları
            var totalQueries = await _context.QueryHistories
                .Where(q => q.CompanyId == _currentUser.TenantId && q.CreatedAt >= thisMonth)
                .CountAsync();

            var recentActivities = await _context.QueryHistories
                .Include(q => q.User)
                .Include(q => q.Project)
                .Where(q =>
                    q.CompanyId == _currentUser.TenantId && // explicit tenant
                    q.CreatedAt >= DateTime.UtcNow.AddDays(-7))
                .OrderByDescending(q => q.CreatedAt)
                .Take(20)
                .Select(q => new RecentActivityDto
                {
                    Username    = q.User.Username,
                    Activity    = q.IsSuccessful ? "Sorgu çalıştırıldı" : "Sorgu başarısız",
                    Timestamp   = q.CreatedAt,
                    ProjectName = q.Project.Name
                })
                .ToListAsync();

            return new UserActivitySummaryDto
            {
                TotalUsers           = memberships.Count,
                ActiveUsers          = memberships.Count(m => m.Status == "approved" && m.IsActive),
                PendingUsers         = memberships.Count(m => m.Status == "pending"),
                SuspendedUsers       = memberships.Count(m => m.Status == "suspended"),
                UsersLoggedInToday   = memberships.Count(m => m.User.LastLoginAt?.Date == today),
                TotalQueriesThisMonth = totalQueries,
                RecentActivities     = recentActivities
            };
        }

        public async Task<bool> UpdateUserRoleAsync(UpdateUserRoleDto dto)
        {
            var validRoles = new HashSet<string> { "admin", "manager", "user" };
            if (!validRoles.Contains(dto.NewRole.ToLowerInvariant()))
                throw new ArgumentException($"Geçersiz rol: '{dto.NewRole}'.");

            // SaaS-2: Rol ÜYELİĞE aittir — kişi başka organizasyonda farklı role sahip olabilir
            var membership = await _context.Memberships
                .Include(m => m.User)
                .FirstOrDefaultAsync(m => m.UserId == dto.UserId && m.CompanyId == _currentUser.TenantId)
                ?? throw new KeyNotFoundException("Üyelik bulunamadı.");

            if (!membership.IsApproved)
                throw new InvalidOperationException("Sadece onaylı üyeliklerin rolü değiştirilebilir.");

            var yeniRol = dto.NewRole.ToLowerInvariant();

            if (membership.UserId == _currentUser.UserId && membership.Role == "admin" && yeniRol != "admin")
                throw new InvalidOperationException("Kendi admin rolünüzü kaldıramazsınız.");

            if (membership.Role == "admin" && yeniRol != "admin")
            {
                var adminCount = await _context.Memberships
                    .CountAsync(m => m.CompanyId == _currentUser.TenantId &&
                                     m.Role == "admin" && m.Status == "approved" && m.IsActive);
                if (adminCount <= 1)
                    throw new InvalidOperationException("Organizasyonda en az bir admin bulunmalıdır.");
            }

            var oldRole = membership.Role;
            membership.ChangeRole(yeniRol); // domain davranışı

            // SaaS-2a legacy ayna
            if (membership.IsPrimary)
            {
                membership.User.Role      = yeniRol;
                membership.User.UpdatedAt = DateTime.UtcNow;
            }

            membership.User.InvalidateSecurityStamp(); // rol claim'i eski token'larda kalmasın
            await _context.SaveChangesAsync();

            _cache.Remove(CacheKeys.SecurityStamp(membership.UserId));
            _cache.Remove(CacheKeys.Membership(membership.UserId, _currentUser.TenantId));

            _audit.Write(new AuditEvent(AuditActions.MemberRoleChanged,
                TargetType: "Membership", TargetId: membership.UserId.ToString(),
                Summary: $"{membership.User.Email}: {oldRole} → {yeniRol}"));
            await _context.SaveChangesAsync();

            _logger.LogInformation("Rol güncellendi: UserId={UserId}, {Old} → {New}, Yönetici={By}",
                dto.UserId, oldRole, yeniRol, _currentUser.UserId);

            return true;
        }

        public async Task<string> GenerateCompanyCodeAsync()
        {
            var company = await _context.Companies
                .FirstOrDefaultAsync(c => c.Id == _currentUser.TenantId)
                ?? throw new KeyNotFoundException("Şirket bulunamadı.");

            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            string newCode;
            int attempts = 0;

            do
            {
                if (++attempts > 10) throw new InvalidOperationException("Kod üretiminde hata oluştu.");
                var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(8);
                newCode = $"COMP-{string.Concat(bytes.Select(b => chars[b % chars.Length]))}";
            }
            while (await _context.Companies.AnyAsync(c => c.CompanyCode == newCode));

            company.CompanyCode = newCode;
            company.UpdatedAt   = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return newCode;
        }

        public async Task<List<CompanyInvitationDto>> GetPendingInvitationsAsync()
        {
            return await _context.CompanyInvitations
                .Include(i => i.InvitedByUser)
                .Where(i => i.CompanyId == _currentUser.TenantId && !i.IsUsed) // explicit tenant
                .OrderByDescending(i => i.CreatedAt)
                .Select(i => new CompanyInvitationDto
                {
                    Id              = i.Id,
                    InvitationToken = i.InvitationToken,
                    Email     = i.Email,
                    Role      = i.Role,
                    InvitedBy = i.InvitedByUser != null ? i.InvitedByUser.Username : "Sistem",
                    CreatedAt = i.CreatedAt,
                    ExpiresAt = i.ExpiresAt
                })
                .ToListAsync();
        }

        public async Task<bool> RemoveUserAsync(int userId)
        {
            // SaaS-2: Kullanıcı hesabı SİLİNMEZ — yalnızca bu organizasyondaki
            // ÜYELİĞİ pasifleştirilir. Kişinin diğer organizasyonlardaki erişimi
            // etkilenmez (çok organizasyonlu modelin doğal sonucu).
            var membership = await _context.Memberships
                .Include(m => m.User)
                .FirstOrDefaultAsync(m => m.UserId == userId && m.CompanyId == _currentUser.TenantId)
                ?? throw new KeyNotFoundException("Üyelik bulunamadı.");

            if (membership.UserId == _currentUser.UserId)
                throw new InvalidOperationException("Kendi üyeliğinizi bu şekilde kaldıramazsınız.");

            if (membership.Role == "admin")
            {
                var adminCount = await _context.Memberships
                    .CountAsync(m => m.CompanyId == _currentUser.TenantId &&
                                     m.Role == "admin" && m.IsActive && m.Status == "approved");
                if (adminCount <= 1)
                    throw new InvalidOperationException("Organizasyonda en az bir admin bulunmalıdır.");
            }

            membership.Deactivate(_currentUser.UserId); // domain davranışı

            // SaaS-2a legacy ayna
            if (membership.IsPrimary)
            {
                membership.User.IsActive      = false;
                membership.User.Status        = "suspended";
                membership.User.DeactivatedAt = DateTime.UtcNow;
                membership.User.DeactivatedBy = _currentUser.UserId;
                membership.User.UpdatedAt     = DateTime.UtcNow;
            }

            membership.User.InvalidateSecurityStamp();
            await _context.SaveChangesAsync();

            _cache.Remove(CacheKeys.SecurityStamp(userId));
            _cache.Remove(CacheKeys.Membership(userId, _currentUser.TenantId));

            _audit.Write(new AuditEvent(AuditActions.MemberRemoved,
                TargetType: "Membership", TargetId: userId.ToString(),
                Summary: $"{membership.User.Email} organizasyondan çıkarıldı"));
            await _context.SaveChangesAsync();

            _logger.LogInformation("Üyelik pasifleştirildi: UserId={UserId}, Organizasyon={Tenant}, Yönetici={By}",
                userId, _currentUser.TenantId, _currentUser.UserId);

            return true;
        }

        public async Task<bool> ValidateCompanyCodeAsync(string code)
        {
            return await _context.Companies
                .AnyAsync(c => c.CompanyCode == code && c.IsActive);
        }
    }
}
