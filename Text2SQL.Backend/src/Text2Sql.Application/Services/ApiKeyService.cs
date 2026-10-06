using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using Text2Sql.Application.Common.Exceptions;
using Text2Sql.Application.Contracts;
using Text2Sql.Application.DTOs.ApiKeys;
using Text2Sql.Domain.Authorization;
using Text2Sql.Domain.Entities;

namespace Text2Sql.Application.Services
{
    public class ApiKeyService : IApiKeyService
    {
        /// <summary>Anahtar öneki — ortam ayrımı için "live" (ileride "test" eklenebilir).</summary>
        private const string KeyPrefix = "t2s_live_";
        /// <summary>
        /// Görüntüleme öneki uzunluğu. ApiKeyDefaults.DisplayPrefixLength ile
        /// AYNI olmalıdır (Api katmanı kimlik doğrulamada bu uzunlukta arar).
        /// Application katmanı Api'ye bağımlı olamayacağı için değer burada
        /// tekrarlanıyor; testler iki değerin eşitliğini doğruluyor.
        /// </summary>
        public const int DisplayPrefixLength = 12;

        /// <summary>
        /// GÜVENLİK: API anahtarına API anahtarı yönetme yetkisi VERİLEMEZ.
        /// Aksi halde sızan bir anahtar kendini kalıcılaştırabilir (yeni anahtarlar
        /// üretip iptal edilemez hale gelebilir) — ayrıcalık yükseltme zinciri.
        /// </summary>
        private static readonly IReadOnlySet<string> YasakliScopelar = new HashSet<string>
        {
            Permissions.ApiKeysManage,
            Permissions.ApiKeysRead,
            Permissions.BillingManage
        };

        private readonly IAppDbContext _context;
        private readonly ICurrentUserContext _currentUser;
        private readonly IAuditWriter _audit;
        private readonly IPlanLimitService _planLimits;
        private readonly ILogger<ApiKeyService> _logger;

        public ApiKeyService(
            IAppDbContext context,
            ICurrentUserContext currentUser,
            IAuditWriter audit,
            IPlanLimitService planLimits,
            ILogger<ApiKeyService> logger)
        {
            _context = context;
            _currentUser = currentUser;
            _audit = audit;
            _planLimits = planLimits;
            _logger = logger;
        }

        public async Task<CreateApiKeyResponse> CreateAsync(
            CreateApiKeyRequest request, CancellationToken ct = default)
        {
            var scopes = request.Scopes
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (scopes.Count == 0)
                throw new BadRequestException("En az bir scope belirtilmelidir (en-az-yetki ilkesi).");

            var gecersiz = scopes.Where(s => !Permissions.All.Contains(s)).ToList();
            if (gecersiz.Count > 0)
                throw new BadRequestException($"Geçersiz scope: {string.Join(", ", gecersiz)}");

            var yasakli = scopes.Where(s => YasakliScopelar.Contains(s)).ToList();
            if (yasakli.Count > 0)
                throw new BadRequestException(
                    $"Bu scope'lar API anahtarına verilemez: {string.Join(", ", yasakli)}");

            // Anahtarı sahibi olan kullanıcının kendi izinlerinden FAZLASINI
            // veremez — aksi halde manager, admin yetkili anahtar üretebilirdi.
            var kullaniciIzinleri = RolePermissions.For(_currentUser.Role);
            var asan = scopes.Where(s => !kullaniciIzinleri.Contains(s)).ToList();
            if (asan.Count > 0)
                throw new ForbiddenException(
                    $"Kendi yetkinizi aşan scope veremezsiniz: {string.Join(", ", asan)}");

            // SaaS-5: Plan anahtar limiti
            await _planLimits.EnsureCanAddApiKeyAsync(ct);

            var (plainKey, hash, prefix) = AnahtarUret();

            var apiKey = new ApiKey
            {
                CompanyId       = _currentUser.TenantId,
                Name            = request.Name,
                KeyHash         = hash,
                Prefix          = prefix,
                Scopes          = string.Join(',', scopes),
                CreatedByUserId = _currentUser.UserId,
                CreatedAt       = DateTime.UtcNow,
                ExpiresAt       = request.ExpiresInDays.HasValue
                    ? DateTime.UtcNow.AddDays(request.ExpiresInDays.Value)
                    : null
            };

            _context.ApiKeys.Add(apiKey);

            _audit.Write(new AuditEvent(AuditActions.ApiKeyCreated,
                TargetType: "ApiKey",
                // Anahtarın kendisi veya özeti denetim kaydına YAZILMAZ
                Summary: $"'{request.Name}' anahtarı oluşturuldu (önek: {prefix}, scope: {string.Join(' ', scopes)})"));

            await _context.SaveChangesAsync(ct);

            _logger.LogInformation("API anahtarı oluşturuldu: Id={Id}, Tenant={Tenant}, Scope sayısı={Count}",
                apiKey.Id, _currentUser.TenantId, scopes.Count);

            return new CreateApiKeyResponse
            {
                Key      = Map(apiKey),
                PlainKey = plainKey
            };
        }

        public async Task<List<ApiKeyDto>> ListAsync(CancellationToken ct = default)
            => (await _context.ApiKeys
                    .AsNoTracking()
                    .OrderByDescending(k => k.CreatedAt)
                    .ToListAsync(ct))
                .Select(Map)
                .ToList();

        public async Task RevokeAsync(int keyId, CancellationToken ct = default)
        {
            // Global kiracı filtresi başka organizasyonun anahtarını görünmez kılar
            var key = await _context.ApiKeys.FirstOrDefaultAsync(k => k.Id == keyId, ct)
                ?? throw new NotFoundException("API anahtarı bulunamadı.");

            key.Revoke(_currentUser.UserId);

            _audit.Write(new AuditEvent(AuditActions.ApiKeyRevoked,
                TargetType: "ApiKey", TargetId: keyId.ToString(),
                Summary: $"'{key.Name}' anahtarı iptal edildi (önek: {key.Prefix})"));

            await _context.SaveChangesAsync(ct);

            _logger.LogInformation("API anahtarı iptal edildi: Id={Id}, Tenant={Tenant}",
                keyId, _currentUser.TenantId);
        }

        // ── Yardımcılar ──────────────────────────────────────────────────────

        private static (string plainKey, string hash, string prefix) AnahtarUret()
        {
            // 32 byte kriptografik rastgelelik → 256 bit entropi
            var bytes = RandomNumberGenerator.GetBytes(32);
            var body = Convert.ToBase64String(bytes)
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');

            var plainKey = KeyPrefix + body;
            return (plainKey, Hash(plainKey), plainKey[..DisplayPrefixLength]);
        }

        /// <summary>
        /// SHA-256. Parolalarda BCrypt kullanılır (yavaş olması istenir);
        /// API anahtarları 256 bit rastgele olduğu için kaba kuvvet saldırısı
        /// anlamsızdır ve her istekte doğrulanacağından HIZLI hash doğru tercihtir.
        /// </summary>
        public static string Hash(string plainKey)
            => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plainKey)));

        private static ApiKeyDto Map(ApiKey k) => new()
        {
            Id         = k.Id,
            Name       = k.Name,
            Prefix     = k.Prefix,
            Scopes     = k.ScopeList.ToList(),
            CreatedAt  = k.CreatedAt,
            LastUsedAt = k.LastUsedAt,
            ExpiresAt  = k.ExpiresAt,
            RevokedAt  = k.RevokedAt,
            IsActive   = k.IsActive
        };
    }
}
