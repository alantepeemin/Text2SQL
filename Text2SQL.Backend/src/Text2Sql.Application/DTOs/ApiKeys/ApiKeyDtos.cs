using System.ComponentModel.DataAnnotations;

namespace Text2Sql.Application.DTOs.ApiKeys
{
    public class CreateApiKeyRequest
    {
        [Required, MinLength(3), MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        /// <summary>İzin adları (bkz. Permissions). Boş bırakılamaz — en-az-yetki ilkesi.</summary>
        [Required, MinLength(1)]
        public List<string> Scopes { get; set; } = new();

        [Range(1, 3650)]
        public int? ExpiresInDays { get; set; }
    }

    public class ApiKeyDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Prefix { get; set; } = string.Empty;
        public List<string> Scopes { get; set; } = new();
        public DateTime CreatedAt { get; set; }
        public DateTime? LastUsedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public DateTime? RevokedAt { get; set; }
        public bool IsActive { get; set; }
    }

    public class CreateApiKeyResponse
    {
        public ApiKeyDto Key { get; set; } = new();

        /// <summary>
        /// Tam anahtar — YALNIZCA BU YANITTA döner, bir daha gösterilemez.
        /// Sunucu yalnızca SHA-256 özetini saklar.
        /// </summary>
        public string PlainKey { get; set; } = string.Empty;

        public string Warning { get; set; } =
            "Bu anahtar bir daha gösterilmeyecek. Güvenli bir yere kaydedin.";
    }
}
