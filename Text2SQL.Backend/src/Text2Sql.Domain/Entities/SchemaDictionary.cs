using Text2Sql.Domain.Common;

namespace Text2Sql.Domain.Entities
{
    /// <summary>
    /// SaaS-9: Şema sözlüğü (semantic layer) — DOĞRULUĞU EN ÇOK ARTIRAN yatırım.
    ///
    /// Sorun: LLM, `rev_amt` kolonunun "ciro" olduğunu, `st` kolonundaki 'A'
    /// harfinin "aktif" demek olduğunu veya şirketin "büyük müşteri" tanımını
    /// bilemez. Ham şema teknik isimlerden oluşur; iş dili değildir.
    ///
    /// Çözüm: müşteri, tablo/kolon açıklamaları ve iş terimleri girer; bunlar
    /// prompt'a eklenir. Küçük bir token maliyetiyle belirgin doğruluk kazancı.
    ///
    /// Kapsam: kiracıya ait (ITenantScoped) ve veri kaynağına bağlı.
    /// </summary>
    public class SchemaAnnotation : ITenantScoped
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }

        public int DataSourceId { get; set; }
        public ProjectDatabase DataSource { get; set; } = null!;

        /// <summary>Tablo adı (zorunlu).</summary>
        public string TableName { get; set; } = string.Empty;

        /// <summary>Kolon adı — boşsa açıklama TABLO düzeyindedir.</summary>
        public string? ColumnName { get; set; }

        /// <summary>İş dilindeki açıklama ("net ciro, KDV hariç").</summary>
        public string Description { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public bool IsTableLevel => string.IsNullOrWhiteSpace(ColumnName);
    }

    /// <summary>
    /// SaaS-9: Sorgu geri bildirimi — doğruluğu ÖLÇMENİN tek yolu.
    ///
    /// Bugüne kadar "sistem ne kadar doğru?" sorusunun cevabı yoktu (Postman
    /// paketi yalnızca 16 sabit soruyu ölçüyor). Kullanıcı geri bildirimi hem
    /// gerçek doğruluk oranını verir hem ileride few-shot örnek havuzu olur:
    /// düzeltilmiş SQL'ler, benzer sorularda prompt'a örnek olarak eklenebilir.
    /// </summary>
    public class QueryFeedback : ITenantScoped
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }

        public int QueryHistoryId { get; set; }
        public QueryHistory QueryHistory { get; set; } = null!;

        public int UserId { get; set; }

        /// <summary>true = doğru sonuç (👍), false = yanlış (👎)</summary>
        public bool IsHelpful { get; set; }

        /// <summary>Kullanıcının düzelttiği SQL (opsiyonel — altın değerinde veri).</summary>
        public string? CorrectedSql { get; set; }

        public string? Comment { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
