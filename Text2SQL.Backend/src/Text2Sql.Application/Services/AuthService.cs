using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Text2Sql.Application.Common.Exceptions;
using Text2Sql.Application.Contracts;
using Text2Sql.Application.DTOs.Auth;
using Text2Sql.Domain.Entities;

namespace Text2Sql.Application.Services
{
    /// <summary>
    /// SaaS-2: Kimlik doğrulama artık ÇOK ORGANİZASYONLU üyelik modeline dayanır.
    ///
    /// Token'daki tenantId = AKTİF organizasyon. Kullanıcı SwitchOrganizationAsync
    /// ile organizasyon değiştirir ve yeni token alır. Rol, aktif organizasyondaki
    /// üyelikten gelir (aynı kişi A'da admin, B'de user olabilir).
    /// </summary>
    public class AuthService : IAuthService
    {
        private readonly IAppDbContext _context;
        private readonly ICurrentUserContext _currentUser;
        private readonly IConfiguration _config;
        private readonly IAuditWriter _audit;
        private readonly IPlanService _planService;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<AuthService> _logger;

        public AuthService(
            IAppDbContext context,
            ICurrentUserContext currentUser,
            IConfiguration config,
            IAuditWriter audit,
            IPlanService planService,
            IEmailSender emailSender,
            ILogger<AuthService> logger)
        {
            _context = context;
            _currentUser = currentUser;
            _config = config;
            _audit = audit;
            _planService = planService;
            _emailSender = emailSender;
            _logger = logger;
        }

        // ─────────────────────────────────────────────────────────────────────
        // AŞAMA 1: Şirket kaydı başlat (durum DB'de — Faz 2)
        // ─────────────────────────────────────────────────────────────────────
        public async Task<CreateCompanyResponse> CreateCompanyAsync(CreateCompanyDto dto)
        {
            if (!string.IsNullOrEmpty(dto.Domain) &&
                await _context.Companies.AnyAsync(c => c.Domain == dto.Domain))
                throw new ConflictException("Bu domain zaten kayıtlı.");

            await _context.PendingRegistrations
                .Where(p => p.ExpiresAt < DateTime.UtcNow)
                .ExecuteDeleteAsync();

            var pending = new PendingRegistration
            {
                Token               = Guid.NewGuid().ToString("N"),
                Name                = dto.Name,
                Domain              = dto.Domain,
                AllowDomainAutoJoin = dto.AllowDomainAutoJoin,
                MaxUsers            = dto.MaxUsers,
                MonthlyQueryLimit   = dto.MonthlyQueryLimit,
                ExpiresAt           = DateTime.UtcNow.AddHours(1),
                CreatedAt           = DateTime.UtcNow
            };

            _context.PendingRegistrations.Add(pending);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Şirket kaydı başlatıldı: {Name}", dto.Name);

            return new CreateCompanyResponse
            {
                RegistrationToken = pending.Token,
                ExpiresAt = pending.ExpiresAt
            };
        }

        // ─────────────────────────────────────────────────────────────────────
        // AŞAMA 2: Organizasyon + admin üyeliği oluştur
        // ─────────────────────────────────────────────────────────────────────
        public async Task<AuthResponseDto> CompleteRegistrationAsync(CompleteRegistrationDto dto)
        {
            var pending = await _context.PendingRegistrations
                .FirstOrDefaultAsync(p => p.Token == dto.RegistrationToken);

            if (pending == null || pending.IsExpired)
                throw new BadRequestException("Geçersiz veya süresi dolmuş kayıt token'ı.");

            if (await _context.Users.AnyAsync(u => u.Email == dto.Email))
                throw new ConflictException("Bu email adresi zaten kayıtlı.");

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var company = new Company
                {
                    Name                = pending.Name,
                    Domain              = pending.Domain,
                    AllowDomainAutoJoin = pending.AllowDomainAutoJoin,
                    MaxUsers            = pending.MaxUsers,
                    MonthlyQueryLimit   = pending.MonthlyQueryLimit,
                    CompanyCode         = await GenerateUniqueCompanyCodeAsync(),
                    CreatedAt           = DateTime.UtcNow,
                    UpdatedAt           = DateTime.UtcNow
                };

                _context.Companies.Add(company);
                await _context.SaveChangesAsync();

                var user = new User
                {
                    CompanyId    = company.Id,   // legacy ayna
                    Username     = dto.Username,
                    Email        = dto.Email,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                    Role         = "admin",      // legacy ayna
                    Status       = "approved",   // legacy ayna
                    ApprovedAt   = DateTime.UtcNow,
                    IsActive     = true,
                    CreatedAt    = DateTime.UtcNow,
                    UpdatedAt    = DateTime.UtcNow
                };

                // SaaS-7: E-posta doğrulama tokeni
                user.EmailConfirmationToken  = GenerateSecureToken();
                user.EmailConfirmationSentAt = DateTime.UtcNow;

                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                user.ApprovedBy = user.Id;

                // SaaS-2: Asıl yetki kaydı — üyelik
                var membership = new Membership
                {
                    UserId     = user.Id,
                    CompanyId  = company.Id,
                    Role       = "admin",
                    Status     = "approved",
                    IsActive   = true,
                    IsPrimary  = true,
                    ApprovedBy = user.Id,
                    ApprovedAt = DateTime.UtcNow,
                    JoinedAt   = DateTime.UtcNow,
                    UpdatedAt  = DateTime.UtcNow
                };
                _context.Memberships.Add(membership);

                var userToken = new UserToken
                {
                    UserId          = user.Id,
                    RemainingTokens = pending.MonthlyQueryLimit,
                    MonthlyLimit    = pending.MonthlyQueryLimit,
                    LastResetDate   = DateTime.UtcNow,
                    CreatedAt       = DateTime.UtcNow,
                    UpdatedAt       = DateTime.UtcNow
                };
                _context.UserTokens.Add(userToken);

                _context.PendingRegistrations.Remove(pending);

                // SaaS-5: Yeni organizasyon Free planla başlar (aynı transaction)
                await _planService.EnsureDefaultSubscriptionAsync(company.Id);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _audit.Write(new AuditEvent(
                    AuditActions.OrganizationCreated,
                    TargetType: "Organization", TargetId: company.Id.ToString(),
                    Summary: $"'{company.Name}' oluşturuldu",
                    OverrideCompanyId: company.Id,
                    OverrideActorUserId: user.Id,
                    OverrideActorEmail: user.Email));
                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "Organizasyon kaydı tamamlandı: {Company}, Admin: {Username}",
                    company.Name, user.Username);

                await DogrulamaEpostasiGonderAsync(user);

                var refreshToken = await CreateRefreshTokenAsync(user.Id, company.Id, ipAddress: null);

                return BuildAuthResponse(user, membership, company, refreshToken.Token,
                    userToken.RemainingTokens,
                    new List<OrganizationSummaryDto>
                    {
                        new() { Id = company.Id, Name = company.Name, Role = "admin",
                                IsPrimary = true, IsActive = true }
                    });
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // LOGIN — aktif organizasyon: birincil üyelik (yoksa ilk geçerli üyelik)
        // ─────────────────────────────────────────────────────────────────────
        public async Task<AuthResponseDto> LoginAsync(LoginDto dto, string? ipAddress = null)
        {
            // Kimlik doğrulanmamış akış → global kiracı filtresi pasif
            var user = await _context.Users
                .Include(u => u.TokenInfo)
                .Include(u => u.Memberships).ThenInclude(m => m.Company)
                .FirstOrDefaultAsync(u => u.Email == dto.Email);

            // SaaS-7: Hesap kilitliyse parola doğru olsa bile giriş yok.
            // Kullanıcı varlığını sızdırmamak için mesaj genel tutulur ama
            // kilitlenme durumu açıkça bildirilir (meşru kullanıcı bilmeli).
            if (user != null && user.IsLockedOut)
            {
                await _audit.WriteAndSaveAsync(new AuditEvent(
                    AuditActions.LoginBlockedLockout,
                    TargetType: "User", TargetId: user.Id.ToString(),
                    Summary: $"Hesap kilitli (bitiş: {user.LockoutEndsAt:O})",
                    OverrideCompanyId: user.Memberships.FirstOrDefault()?.CompanyId ?? 0,
                    OverrideActorUserId: user.Id,
                    OverrideActorEmail: dto.Email));

                throw new ForbiddenException(
                    "Çok fazla başarısız giriş denemesi nedeniyle hesabınız geçici olarak kilitlendi. " +
                    "Lütfen birkaç dakika sonra tekrar deneyin.");
            }

            if (user == null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            {
                // SaaS-7: Hesap bazlı başarısız deneme sayacı (IP rate limit'e ek).
                if (user != null)
                {
                    user.RegisterFailedLogin();
                    await _context.SaveChangesAsync();
                }

                // SaaS-3: Güvenlik olayı — kiracı bağlamı yok, bağımsız kaydedilir.
                // Parola ASLA loglanmaz/kaydedilmez.
                await _audit.WriteAndSaveAsync(new AuditEvent(
                    AuditActions.LoginFailed,
                    TargetType: "User",
                    Summary: "Hatalı email veya şifre",
                    OverrideCompanyId: user?.Memberships.FirstOrDefault()?.CompanyId ?? 0,
                    OverrideActorUserId: user?.Id,
                    OverrideActorEmail: dto.Email));

                throw new UnauthorizedException("Email veya şifre hatalı.");
            }

            if (!user.CanLogin)
                throw new ForbiddenException("Hesabınız devre dışı bırakılmıştır.");

            var erisilebilir = user.Memberships
                .Where(m => m.CanAccess && m.Company.IsActive)
                .ToList();

            if (erisilebilir.Count == 0)
            {
                // Neden erişemiyor? En açıklayıcı mesajı seç.
                var bekleyen = user.Memberships.FirstOrDefault(m => m.IsPending);
                var askida   = user.Memberships.FirstOrDefault(m => m.IsSuspended);
                var msg = bekleyen != null
                    ? "Hesabınız henüz onaylanmamış. Lütfen organizasyon yöneticisiyle iletişime geçin."
                    : askida != null
                        ? "Hesabınız askıya alınmış. Lütfen organizasyon yöneticisiyle iletişime geçin."
                        : "Hiçbir organizasyona erişiminiz bulunmuyor.";
                throw new ForbiddenException(msg);
            }

            var aktif = erisilebilir.FirstOrDefault(m => m.IsPrimary) ?? erisilebilir[0];

            // SaaS-7: Başarılı girişte kilit ve sayaç temizlenir (domain davranışı)
            user.RegisterSuccessfulLogin(DateTime.UtcNow, ipAddress);
            await _context.SaveChangesAsync();

            var refreshToken = await CreateRefreshTokenAsync(user.Id, aktif.CompanyId, ipAddress);

            _audit.Write(new AuditEvent(
                AuditActions.LoginSucceeded,
                TargetType: "User", TargetId: user.Id.ToString(),
                Summary: $"Aktif organizasyon: {aktif.Company.Name}",
                OverrideCompanyId: aktif.CompanyId,
                OverrideActorUserId: user.Id,
                OverrideActorEmail: user.Email));
            await _context.SaveChangesAsync();

            _logger.LogInformation("Giriş yapıldı: {Username}, Aktif organizasyon: {Company}",
                user.Username, aktif.Company.Name);

            return BuildAuthResponse(user, aktif, aktif.Company, refreshToken.Token,
                user.TokenInfo?.RemainingTokens ?? 0, MapOrganizations(erisilebilir));
        }

        // ─────────────────────────────────────────────────────────────────────
        // SaaS-7: E-POSTA DOĞRULAMA
        // ─────────────────────────────────────────────────────────────────────
        public async Task<bool> ConfirmEmailAsync(string token, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(token))
                throw new BadRequestException("Doğrulama token'ı gereklidir.");

            // Anonim akış → kiracı filtresi pasif
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.EmailConfirmationToken == token, ct);

            if (user == null)
                throw new BadRequestException("Geçersiz veya kullanılmış doğrulama bağlantısı.");

            // Süre sınırı: 7 gün (yeniden gönderim mümkün)
            if (user.EmailConfirmationSentAt < DateTime.UtcNow.AddDays(-7))
                throw new BadRequestException(
                    "Doğrulama bağlantısının süresi dolmuş. Yeni bir bağlantı isteyin.");

            user.ConfirmEmail();

            _audit.Write(new AuditEvent(AuditActions.EmailConfirmed,
                TargetType: "User", TargetId: user.Id.ToString(),
                Summary: "E-posta adresi doğrulandı",
                OverrideCompanyId: user.CompanyId,
                OverrideActorUserId: user.Id,
                OverrideActorEmail: user.Email));

            await _context.SaveChangesAsync(ct);

            _logger.LogInformation("E-posta doğrulandı: UserId={UserId}", user.Id);
            return true;
        }

        public async Task ResendEmailConfirmationAsync(CancellationToken ct = default)
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == _currentUser.UserId, ct)
                ?? throw new NotFoundException("Kullanıcı bulunamadı.");

            if (user.EmailConfirmed) return; // idempotent

            user.EmailConfirmationToken  = GenerateSecureToken();
            user.EmailConfirmationSentAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);

            await DogrulamaEpostasiGonderAsync(user);
        }

        /// <summary>
        /// Doğrulama e-postası — best effort. SMTP hatası kayıt akışını KIRMAZ
        /// (kullanıcı içeri girer, doğrulamayı sonra yapar); e-posta kapalıysa
        /// NoOpEmailSender devrededir.
        /// </summary>
        private async Task DogrulamaEpostasiGonderAsync(User user)
        {
            try
            {
                var baseUrl = _config["Email:InvitationBaseUrl"] ?? "http://localhost:3001";
                var link = $"{baseUrl.TrimEnd('/')}/confirm-email?token={user.EmailConfirmationToken}";

                await _emailSender.SendAsync(new EmailMessage(
                    user.Email,
                    "Text2SQL — e-posta adresinizi doğrulayın",
                    $"""
                    <p>Merhaba {user.Username},</p>
                    <p>Hesabınızı etkinleştirmek için e-posta adresinizi doğrulayın:</p>
                    <p><a href="{link}">E-postamı doğrula</a></p>
                    <p>Bu bağlantı 7 gün geçerlidir.</p>
                    """));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Doğrulama e-postası gönderilemedi: UserId={UserId}", user.Id);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // SaaS-2: ORGANİZASYON DEĞİŞTİR
        // ─────────────────────────────────────────────────────────────────────
        public async Task<AuthResponseDto> SwitchOrganizationAsync(int organizationId, string? ipAddress = null)
        {
            var userId = _currentUser.UserId;

            // MEŞRU ÇAPRAZ-KİRACI OKUMA (1/2):
            // Kullanıcı şu anda A organizasyonunda; B'ye geçmek için B'deki
            // üyeliğini okumalı. Global filtre bunu gizlerdi. IgnoreQueryFilters
            // KULLANICININ KENDİ üyelikleriyle sınırlıdır (UserId == userId) —
            // başka kullanıcının verisine erişim imkânsızdır.
            var hedef = await _context.Memberships
                .IgnoreQueryFilters()
                .Include(m => m.Company)
                .Include(m => m.User).ThenInclude(u => u.TokenInfo)
                .FirstOrDefaultAsync(m => m.UserId == userId && m.CompanyId == organizationId);

            if (hedef == null)
                throw new NotFoundException("Bu organizasyonda üyeliğiniz bulunmuyor.");

            if (!hedef.CanAccess || !hedef.Company.IsActive)
                throw new ForbiddenException("Bu organizasyona erişiminiz aktif değil.");

            if (!hedef.User.CanLogin)
                throw new ForbiddenException("Hesabınız devre dışı bırakılmıştır.");

            var refreshToken = await CreateRefreshTokenAsync(userId, organizationId, ipAddress);

            var tumUyelikler = await _context.Memberships
                .IgnoreQueryFilters()
                .Include(m => m.Company)
                .Where(m => m.UserId == userId && m.IsActive && m.Status == "approved")
                .ToListAsync();

            _audit.Write(new AuditEvent(
                AuditActions.OrganizationSwitched,
                TargetType: "Organization", TargetId: organizationId.ToString(),
                Summary: $"'{hedef.Company.Name}' organizasyonuna geçildi",
                OverrideCompanyId: organizationId,
                OverrideActorUserId: userId,
                OverrideActorEmail: hedef.User.Email));
            await _context.SaveChangesAsync();

            _logger.LogInformation("Organizasyon değiştirildi: UserId={UserId} → {Company}",
                userId, hedef.Company.Name);

            return BuildAuthResponse(hedef.User, hedef, hedef.Company, refreshToken.Token,
                hedef.User.TokenInfo?.RemainingTokens ?? 0, MapOrganizations(tumUyelikler));
        }

        public async Task<List<OrganizationSummaryDto>> GetMyOrganizationsAsync()
        {
            var userId = _currentUser.UserId;

            // MEŞRU ÇAPRAZ-KİRACI OKUMA (2/2): yalnızca kullanıcının kendi üyelikleri.
            var uyelikler = await _context.Memberships
                .IgnoreQueryFilters()
                .Include(m => m.Company)
                .Where(m => m.UserId == userId)
                .ToListAsync();

            return MapOrganizations(uyelikler);
        }

        // ─────────────────────────────────────────────────────────────────────
        // DAVETİYE İLE KAYIT — mevcut kullanıcı ikinci organizasyona katılabilir
        // ─────────────────────────────────────────────────────────────────────
        public async Task<PendingRegistrationResult> AcceptInvitationAsync(AcceptInvitationDto dto)
        {
            var invitation = await _context.CompanyInvitations
                .IgnoreQueryFilters() // anonim akış: davet token'ı kiracı bağlamı taşımaz
                .Include(i => i.Company)
                .FirstOrDefaultAsync(i => i.InvitationToken == dto.InvitationToken && !i.IsUsed);

            if (invitation == null)
                throw new BadRequestException("Geçersiz davetiye token'ı.");

            if (invitation.ExpiresAt < DateTime.UtcNow)
                throw new BadRequestException("Davetiye süresi dolmuş.");

            var mevcutKullanici = await _context.Users
                .Include(u => u.Memberships)
                .FirstOrDefaultAsync(u => u.Email == invitation.Email);

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                User user;

                if (mevcutKullanici != null)
                {
                    // SaaS-2 YENİ YETENEK: Var olan hesap ikinci organizasyona katılır.
                    if (mevcutKullanici.Memberships.Any(m => m.CompanyId == invitation.CompanyId))
                        throw new ConflictException("Bu organizasyonda zaten üyeliğiniz var.");

                    user = mevcutKullanici;
                }
                else
                {
                    user = new User
                    {
                        CompanyId      = invitation.CompanyId, // legacy ayna (ilk organizasyon)
                        Username       = dto.Username,
                        Email          = invitation.Email,
                        PasswordHash   = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                        Role           = invitation.Role,
                        Status         = "pending",
                        InvitedBy      = invitation.InvitedBy,
                        InvitedAt      = invitation.CreatedAt,
                        InvitationType = "invitation",
                        IsActive       = true,
                        CreatedAt      = DateTime.UtcNow,
                        UpdatedAt      = DateTime.UtcNow
                    };
                    _context.Users.Add(user);
                    await _context.SaveChangesAsync();
                }

                _context.Memberships.Add(new Membership
                {
                    UserId         = user.Id,
                    CompanyId      = invitation.CompanyId,
                    Role           = invitation.Role,
                    Status         = "pending",       // yönetici onayı gerekir
                    IsActive       = true,
                    IsPrimary      = mevcutKullanici == null,
                    InvitationType = "invitation",
                    InvitedBy      = invitation.InvitedBy,
                    InvitedAt      = invitation.CreatedAt,
                    JoinedAt       = DateTime.UtcNow,
                    UpdatedAt      = DateTime.UtcNow
                });

                invitation.IsUsed = true;
                invitation.UsedAt = DateTime.UtcNow;
                invitation.UsedBy = user.Id;

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _audit.Write(new AuditEvent(
                    AuditActions.MemberJoined,
                    TargetType: "Membership", TargetId: user.Id.ToString(),
                    Summary: $"Davetiye ile katıldı (onay bekliyor), rol: {invitation.Role}",
                    OverrideCompanyId: invitation.CompanyId,
                    OverrideActorUserId: user.Id,
                    OverrideActorEmail: invitation.Email));
                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "Davetiye ile üyelik oluşturuldu (onay bekliyor): {Email} → {Company}",
                    invitation.Email, invitation.Company.Name);

                return new PendingRegistrationResult
                {
                    Success     = true,
                    Message     = "Üyelik oluşturuldu. Yönetici onayından sonra erişebilirsiniz.",
                    CompanyName = invitation.Company.Name
                };
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // KOD İLE KAYIT — mevcut kullanıcı da katılabilir
        // ─────────────────────────────────────────────────────────────────────
        public async Task<PendingRegistrationResult> JoinByCodeAsync(JoinByCodeDto dto)
        {
            var company = await _context.Companies
                .FirstOrDefaultAsync(c => c.CompanyCode == dto.CompanyCode && c.IsActive);

            if (company == null)
                throw new BadRequestException("Geçersiz şirket kodu.");

            var mevcutKullanici = await _context.Users
                .Include(u => u.Memberships)
                .FirstOrDefaultAsync(u => u.Email == dto.Email);

            if (mevcutKullanici != null &&
                mevcutKullanici.Memberships.Any(m => m.CompanyId == company.Id))
                throw new ConflictException("Bu organizasyonda zaten üyeliğiniz var.");

            // Organizasyon kullanıcı limiti — üyelik sayısı üzerinden
            var aktifUyeSayisi = await _context.Memberships
                .IgnoreQueryFilters() // anonim akış
                .CountAsync(m => m.CompanyId == company.Id && m.IsActive);

            if (aktifUyeSayisi >= company.MaxUsers)
                throw new ConflictException("Organizasyon kullanıcı limiti dolmuş.");

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                User user;
                if (mevcutKullanici != null)
                {
                    user = mevcutKullanici;
                }
                else
                {
                    user = new User
                    {
                        CompanyId      = company.Id, // legacy ayna
                        Username       = dto.Username,
                        Email          = dto.Email,
                        PasswordHash   = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                        Role           = "user",
                        Status         = "pending",
                        InvitationType = "code",
                        IsActive       = true,
                        CreatedAt      = DateTime.UtcNow,
                        UpdatedAt      = DateTime.UtcNow
                    };
                    _context.Users.Add(user);
                    await _context.SaveChangesAsync();
                }

                _context.Memberships.Add(new Membership
                {
                    UserId         = user.Id,
                    CompanyId      = company.Id,
                    Role           = "user",
                    Status         = "pending",
                    IsActive       = true,
                    IsPrimary      = mevcutKullanici == null,
                    InvitationType = "code",
                    JoinedAt       = DateTime.UtcNow,
                    UpdatedAt      = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation("Kod ile üyelik oluşturuldu (onay bekliyor): {Email} → {Company}",
                    dto.Email, company.Name);

                return new PendingRegistrationResult
                {
                    Success     = true,
                    Message     = "Üyelik oluşturuldu. Yönetici onayından sonra erişebilirsiniz.",
                    CompanyName = company.Name
                };
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // REFRESH TOKEN — aktif organizasyon bağlamı korunur
        // ─────────────────────────────────────────────────────────────────────
        public async Task<AuthResponseDto> RefreshTokenAsync(string refreshToken, string? ipAddress = null)
        {
            var token = await _context.RefreshTokens
                .Include(r => r.User).ThenInclude(u => u.TokenInfo)
                .Include(r => r.User).ThenInclude(u => u.Memberships).ThenInclude(m => m.Company)
                .FirstOrDefaultAsync(r => r.Token == refreshToken);

            if (token == null)
                throw new UnauthorizedException("Geçersiz veya süresi dolmuş refresh token.");

            // ── SaaS-7: YENİDEN KULLANIM TESPİTİ ─────────────────────────────
            // İptal edilmiş bir token tekrar sunulduysa iki olasılık var:
            // (a) saldırgan eski token'ı ele geçirdi, (b) istemci yarış yaşadı.
            // Her iki durumda güvenli davranış aynıdır: TÜM AİLEYİ iptal et
            // (OAuth 2.0 Security BCP önerisi). Meşru kullanıcı yeniden giriş yapar.
            if (token.IsRevoked)
            {
                var iptalEdilen = await _context.RefreshTokens
                    .Where(r => r.FamilyId == token.FamilyId && !r.IsRevoked)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(r => r.IsRevoked, true)
                        .SetProperty(r => r.RevokedAt, DateTime.UtcNow)
                        .SetProperty(r => r.RevokedReason, "Token ailesi iptal edildi: yeniden kullanım tespit edildi"));

                // Tüm oturumları düşür (stamp yenile) — çalınan access token da ölsün
                token.User.InvalidateSecurityStamp();
                await _context.SaveChangesAsync();

                await _audit.WriteAndSaveAsync(new AuditEvent(
                    AuditActions.RefreshTokenReuseDetected,
                    TargetType: "RefreshToken", TargetId: token.Id.ToString(),
                    Summary: $"Yeniden kullanım tespit edildi; {iptalEdilen} token iptal edildi (aile: {token.FamilyId})",
                    OverrideCompanyId: token.CompanyId ?? 0,
                    OverrideActorUserId: token.UserId,
                    OverrideActorEmail: token.User.Email));

                _logger.LogWarning(
                    "GÜVENLİK: Refresh token yeniden kullanımı — UserId={UserId}, Aile={Family}",
                    token.UserId, token.FamilyId);

                throw new UnauthorizedException(
                    "Güvenlik nedeniyle oturumunuz sonlandırıldı. Lütfen yeniden giriş yapın.");
            }

            if (!token.IsActive)
                throw new UnauthorizedException("Geçersiz veya süresi dolmuş refresh token.");

            var user = token.User;
            if (!user.CanLogin)
                throw new ForbiddenException("Hesabınız devre dışı bırakılmıştır.");

            var erisilebilir = user.Memberships
                .Where(m => m.CanAccess && m.Company.IsActive)
                .ToList();

            // Token'ın verildiği organizasyon hâlâ erişilebilir mi?
            var aktif = (token.CompanyId.HasValue
                            ? erisilebilir.FirstOrDefault(m => m.CompanyId == token.CompanyId.Value)
                            : null)
                        ?? erisilebilir.FirstOrDefault(m => m.IsPrimary)
                        ?? erisilebilir.FirstOrDefault();

            if (aktif == null)
                throw new ForbiddenException("Hiçbir organizasyona erişiminiz bulunmuyor.");

            // Token rotation — yeni token AYNI AİLEYE ait olur
            token.IsRevoked     = true;
            token.RevokedAt     = DateTime.UtcNow;
            token.RevokedReason = "Replaced by new token";

            var newRefreshToken = await CreateRefreshTokenAsync(
                user.Id, aktif.CompanyId, ipAddress, token.FamilyId);

            token.ReplacedByTokenId = newRefreshToken.Id;

            user.LastActivityAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return BuildAuthResponse(user, aktif, aktif.Company, newRefreshToken.Token,
                user.TokenInfo?.RemainingTokens ?? 0, MapOrganizations(erisilebilir));
        }

        public async Task RevokeRefreshTokenAsync(string refreshToken, string reason = "Logout")
        {
            var token = await _context.RefreshTokens
                .FirstOrDefaultAsync(r => r.Token == refreshToken);

            if (token == null || !token.IsActive) return;

            token.IsRevoked     = true;
            token.RevokedAt     = DateTime.UtcNow;
            token.RevokedReason = reason;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Refresh token iptal edildi: UserId={UserId}, Sebep={Reason}",
                token.UserId, reason);
        }

        // ─────────────────────────────────────────────────────────────────────
        // TOKEN ÜRETİMİ
        // ─────────────────────────────────────────────────────────────────────
        public string GenerateAccessToken(User user, Membership membership)
        {
            var secret = _config["JwtSettings:SecretKey"]
                ?? throw new InvalidOperationException("JwtSettings:SecretKey yapılandırılmamış.");

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));

            var claims = new[]
            {
                new Claim("userId",   user.Id.ToString()),
                new Claim("tenantId", membership.CompanyId.ToString()), // AKTİF organizasyon
                new Claim("username", user.Username),
                new Claim("role",     membership.Role),                 // üyelikten gelir
                new Claim("status",   membership.Status),
                new Claim("sstamp",   user.SecurityStamp),
                new Claim(ClaimTypes.Name,  user.Username),
                new Claim(ClaimTypes.Role,  membership.Role),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            };

            var token = new JwtSecurityToken(
                issuer:             _config["JwtSettings:Issuer"]   ?? "Text2SQL.API",
                audience:           _config["JwtSettings:Audience"] ?? "Text2SQL.Client",
                claims:             claims,
                expires:            DateTime.UtcNow.AddMinutes(GetAccessTokenMinutes()),
                signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Yardımcılar
        // ─────────────────────────────────────────────────────────────────────

        private AuthResponseDto BuildAuthResponse(
            User user, Membership membership, Company company, string refreshToken,
            int remainingTokens, List<OrganizationSummaryDto> organizations)
            => new()
            {
                AccessToken          = GenerateAccessToken(user, membership),
                RefreshToken         = refreshToken,
                AccessTokenExpiry    = DateTime.UtcNow.AddMinutes(GetAccessTokenMinutes()),
                Username             = user.Username,
                Role                 = membership.Role,
                CompanyName          = company.Name,
                RemainingTokens      = remainingTokens,
                ActiveOrganizationId = membership.CompanyId,
                Organizations        = organizations
            };

        private static List<OrganizationSummaryDto> MapOrganizations(IEnumerable<Membership> memberships)
            => memberships.Select(m => new OrganizationSummaryDto
            {
                Id        = m.CompanyId,
                Name      = m.Company?.Name ?? string.Empty,
                Role      = m.Role,
                IsPrimary = m.IsPrimary,
                IsActive  = m.CanAccess
            }).ToList();

        private async Task<RefreshToken> CreateRefreshTokenAsync(
            int userId, int? companyId, string? ipAddress, Guid? familyId = null)
        {
            var expiryDays = _config.GetValue<int>("JwtSettings:RefreshTokenExpiryDays", 7);

            var token = new RefreshToken
            {
                UserId      = userId,
                CompanyId   = companyId,
                // Yeni oturum → yeni aile; rotasyon → mevcut aile devam eder
                FamilyId    = familyId ?? Guid.NewGuid(),
                Token       = GenerateSecureToken(),
                ExpiresAt   = DateTime.UtcNow.AddDays(expiryDays),
                CreatedByIp = ipAddress,
                CreatedAt   = DateTime.UtcNow
            };

            _context.RefreshTokens.Add(token);
            await _context.SaveChangesAsync();
            return token;
        }

        private static string GenerateSecureToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(64);
            return Convert.ToBase64String(bytes)
                .Replace("+", "-").Replace("/", "_").TrimEnd('=');
        }

        private int GetAccessTokenMinutes()
            => _config.GetValue<int>("JwtSettings:AccessTokenExpiryMinutes", 60);

        private async Task<string> GenerateUniqueCompanyCodeAsync()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            string code;
            int attempts = 0;

            do
            {
                if (++attempts > 10)
                    throw new InvalidOperationException("Şirket kodu üretiminde hata.");

                var bytes = RandomNumberGenerator.GetBytes(8);
                code = $"COMP-{string.Concat(bytes.Select(b => chars[b % chars.Length]))}";
            }
            while (await _context.Companies.AnyAsync(c => c.CompanyCode == code));

            return code;
        }
    }
}
