using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Text2Sql.Application.Common;
using Text2Sql.Application.Contracts;
using Text2Sql.Application.DTOs.User;

namespace Text2Sql.Application.Services
{
    public class UserService : IUserService
    {
        private readonly IAppDbContext _context;
        private readonly ICurrentUserContext  _currentUser;
        private readonly IMemoryCache _cache;
        private readonly ILogger<UserService> _logger;

        public UserService(
            IAppDbContext context,
            ICurrentUserContext currentUser,
            IMemoryCache cache,
            ILogger<UserService> logger)
        {
            _context     = context;
            _currentUser = currentUser;
            _cache       = cache;
            _logger      = logger;
        }

        public async Task<UserDto> GetProfileAsync(int userId)
        {
            if (userId != _currentUser.UserId)
                throw new UnauthorizedAccessException("Yalnızca kendi profilinizi görüntüleyebilirsiniz.");

            var user = await _context.Users
                .AsNoTracking()
                // SaaS-8 (r2): Legacy CompanyId filtresi KALDIRILDI.
                // Bunlar self-servis işlemler: kullanıcı KENDİ hesabı üzerinde
                // çalışıyor (yukarıdaki userId != _currentUser.UserId kontrolü
                // güvenceyi sağlıyor). Legacy filtre, çok organizasyonlu bir
                // kullanıcı birincil olmayan organizasyonda çalışırken profilini
                // "bulunamadı" yapıyordu — SaaS-2a'dan kalan sessiz hata.
                .Where(u => u.Id == userId)
                .FirstOrDefaultAsync()
                ?? throw new KeyNotFoundException("Kullanıcı bulunamadı.");

            return new UserDto
            {
                Username  = user.Username,
                Email     = user.Email,
                CreatedAt = user.CreatedAt
            };
        }

        public async Task<bool> UpdateProfileAsync(int userId, UpdateProfileRequest request)
        {
            if (userId != _currentUser.UserId)
                throw new UnauthorizedAccessException("Yalnızca kendi profilinizi güncelleyebilirsiniz.");

            var user = await _context.Users
                // SaaS-8 (r2): Legacy CompanyId filtresi KALDIRILDI.
                // Bunlar self-servis işlemler: kullanıcı KENDİ hesabı üzerinde
                // çalışıyor (yukarıdaki userId != _currentUser.UserId kontrolü
                // güvenceyi sağlıyor). Legacy filtre, çok organizasyonlu bir
                // kullanıcı birincil olmayan organizasyonda çalışırken profilini
                // "bulunamadı" yapıyordu — SaaS-2a'dan kalan sessiz hata.
                .Where(u => u.Id == userId)
                .FirstOrDefaultAsync()
                ?? throw new KeyNotFoundException("Kullanıcı bulunamadı.");

            // ✅ Email benzersizlik kontrolü — sistemde başka kullanıcıda var mı?
            if (!string.Equals(user.Email, request.Email, StringComparison.OrdinalIgnoreCase))
            {
                var emailTaken = await _context.Users
                    .AnyAsync(u => u.Email == request.Email && u.Id != userId);
                if (emailTaken)
                    throw new ArgumentException("Bu email adresi başka bir kullanıcı tarafından kullanılmaktadır.");
            }

            user.Username  = request.Username;
            user.Email     = request.Email;
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Profil güncellendi: UserId={UserId}", userId);
            return true;
        }

        public async Task<bool> ChangePasswordAsync(int userId, ChangePasswordRequest request)
        {
            if (userId != _currentUser.UserId)
                throw new UnauthorizedAccessException("Yalnızca kendi şifrenizi değiştirebilirsiniz.");

            var user = await _context.Users
                // SaaS-8 (r2): Legacy CompanyId filtresi KALDIRILDI.
                // Bunlar self-servis işlemler: kullanıcı KENDİ hesabı üzerinde
                // çalışıyor (yukarıdaki userId != _currentUser.UserId kontrolü
                // güvenceyi sağlıyor). Legacy filtre, çok organizasyonlu bir
                // kullanıcı birincil olmayan organizasyonda çalışırken profilini
                // "bulunamadı" yapıyordu — SaaS-2a'dan kalan sessiz hata.
                .Where(u => u.Id == userId)
                .FirstOrDefaultAsync()
                ?? throw new KeyNotFoundException("Kullanıcı bulunamadı.");

            if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
                throw new ArgumentException("Mevcut şifre yanlış.");

            if (request.CurrentPassword == request.NewPassword)
                throw new ArgumentException("Yeni şifre mevcut şifreyle aynı olamaz.");

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
            user.UpdatedAt    = DateTime.UtcNow;

            // GÜVENLİK: Parola değişimi MEVCUT OTURUMLARI DÜŞÜRMELİDİR.
            // Kullanıcılar parolayı çoğunlukla "birileri hesabıma girmiş olabilir"
            // endişesiyle değiştirir; eski access token'ın ömrü boyunca geçerli
            // kalması bu eylemi anlamsız kılardı. SecurityStamp yenilenir
            // (access token'lar anında düşer) ve refresh token'lar iptal edilir.
            user.InvalidateSecurityStamp();

            var aktifTokenlar = await _context.RefreshTokens
                .Where(r => r.UserId == userId && !r.IsRevoked)
                .ToListAsync();

            foreach (var token in aktifTokenlar)
            {
                token.IsRevoked     = true;
                token.RevokedAt     = DateTime.UtcNow;
                token.RevokedReason = "Password changed";
            }

            await _context.SaveChangesAsync();
            _cache.Remove(CacheKeys.SecurityStamp(userId));

            _logger.LogInformation(
                "Şifre değiştirildi ve {Count} oturum düşürüldü: UserId={UserId}",
                aktifTokenlar.Count, userId);
            return true;
        }

        /// <summary>
        /// ✅ Soft delete — kullanıcı ve tüm geçmişi korunur.
        /// Hard delete yerine pasifleştirme yapılır.
        /// </summary>
        public async Task<bool> DeactivateAccountAsync(int userId)
        {
            if (userId != _currentUser.UserId)
                throw new UnauthorizedAccessException("Yalnızca kendi hesabınızı devre dışı bırakabilirsiniz.");

            var user = await _context.Users
                // SaaS-8 (r2): Legacy CompanyId filtresi KALDIRILDI.
                // Bunlar self-servis işlemler: kullanıcı KENDİ hesabı üzerinde
                // çalışıyor (yukarıdaki userId != _currentUser.UserId kontrolü
                // güvenceyi sağlıyor). Legacy filtre, çok organizasyonlu bir
                // kullanıcı birincil olmayan organizasyonda çalışırken profilini
                // "bulunamadı" yapıyordu — SaaS-2a'dan kalan sessiz hata.
                .Where(u => u.Id == userId)
                .FirstOrDefaultAsync()
                ?? throw new KeyNotFoundException("Kullanıcı bulunamadı.");

            // SaaS-2: Son admin kontrolü ÜYELİK üzerinden — kişinin aktif
            // organizasyondaki rolüne bakılır (diğer organizasyonlar etkilenmez).
            var aktifUyelik = await _context.Memberships
                .FirstOrDefaultAsync(m => m.UserId == userId && m.CompanyId == _currentUser.TenantId);

            if (aktifUyelik?.Role == "admin")
            {
                var adminCount = await _context.Memberships
                    .CountAsync(m => m.CompanyId == _currentUser.TenantId &&
                                     m.Role == "admin" && m.IsActive && m.Status == "approved");
                if (adminCount <= 1)
                    throw new InvalidOperationException("Organizasyondaki son admin hesabını silemezsiniz.");
            }

            // Hesap kapatıldığında TÜM üyelikler pasifleşir
            var tumUyelikler = await _context.Memberships
                .IgnoreQueryFilters()   // kendi hesabının tüm organizasyonları
                .Where(m => m.UserId == userId)
                .ToListAsync();
            foreach (var uyelik in tumUyelikler)
                uyelik.Deactivate(userId);

            // Soft delete
            user.IsActive      = false;
            user.Status        = "suspended";
            user.DeactivatedAt = DateTime.UtcNow;
            user.DeactivatedBy = userId;
            user.InvalidateSecurityStamp();
            user.UpdatedAt     = DateTime.UtcNow;

            // Tüm aktif refresh token'larını iptal et
            var activeTokens = await _context.RefreshTokens
                .Where(r => r.UserId == userId && !r.IsRevoked)
                .ToListAsync();

            foreach (var token in activeTokens)
            {
                token.IsRevoked     = true;
                token.RevokedAt     = DateTime.UtcNow;
                token.RevokedReason = "Account deactivated";
            }

            await _context.SaveChangesAsync();
            _cache.Remove(CacheKeys.SecurityStamp(userId));

            _logger.LogInformation("Hesap pasifleştirildi: UserId={UserId}", userId);
            return true;
        }
    }
}
