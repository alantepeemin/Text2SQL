using System.ComponentModel.DataAnnotations;

namespace Text2Sql.Application.DTOs.Auth
{
    public class LoginDto
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required, MinLength(6)]
        public string Password { get; set; } = string.Empty;
    }

    public class CreateCompanyDto
    {
        [Required, MinLength(2), MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Domain { get; set; }

        public bool AllowDomainAutoJoin { get; set; } = false;

        [Range(1, 10000)]
        public int MaxUsers { get; set; } = 10;

        [Range(10, 1000000)]
        public int MonthlyQueryLimit { get; set; } = 1000;
    }

    public class CreateCompanyResponse
    {
        public string RegistrationToken { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
    }

    public class CompleteRegistrationDto
    {
        [Required]
        public string RegistrationToken { get; set; } = string.Empty;

        [Required, MinLength(3), MaxLength(50)]
        public string Username { get; set; } = string.Empty;

        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required, MinLength(8)]
        public string Password { get; set; } = string.Empty;
    }

    public class AcceptInvitationDto
    {
        [Required]
        public string InvitationToken { get; set; } = string.Empty;

        [Required, MinLength(3), MaxLength(50)]
        public string Username { get; set; } = string.Empty;

        [Required, MinLength(8)]
        public string Password { get; set; } = string.Empty;
    }

    public class JoinByCodeDto
    {
        [Required]
        public string CompanyCode { get; set; } = string.Empty;

        [Required, MinLength(3), MaxLength(50)]
        public string Username { get; set; } = string.Empty;

        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required, MinLength(8)]
        public string Password { get; set; } = string.Empty;
    }

    public class RefreshTokenDto
    {
        [Required]
        public string RefreshToken { get; set; } = string.Empty;
    }

    /// <summary>SaaS-2: Kullanıcının üye olduğu bir organizasyon.</summary>
    public class OrganizationSummaryDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public bool IsPrimary { get; set; }
        public bool IsActive { get; set; }
    }

    public class ConfirmEmailDto
    {
        [Required]
        public string Token { get; set; } = string.Empty;
    }

    public class SwitchOrganizationDto
    {
        [Required]
        public int OrganizationId { get; set; }
    }

    public class AuthResponseDto
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTime AccessTokenExpiry { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public int RemainingTokens { get; set; }

        // SaaS-2: Aktif organizasyon ve kullanıcının tüm üyelikleri.
        // İstemci organizasyon seçici gösterebilir; v1 istemcisi bu alanları
        // yok sayabilir (geriye uyumlu ekleme).
        public int ActiveOrganizationId { get; set; }
        public List<OrganizationSummaryDto> Organizations { get; set; } = new();
    }

    /// <summary>
    /// Davetiye/kod ile kayıtta, kullanıcı onay beklediği için
    /// token yerine bu DTO döndürülür.
    /// </summary>
    public class PendingRegistrationResult
    {
        public bool Success { get; set; } = true;
        public string Message { get; set; } = string.Empty;
        public string? CompanyName { get; set; }
    }
}
