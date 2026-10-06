using Text2Sql.Domain.Common;

namespace Text2Sql.Domain.Entities
{
    /// <summary>
    /// SaaS-3: Ölçüm (metering) kaydı — faturalamanın ve gerçek kotanın temeli.
    ///
    /// Neden gerekli: kota bugüne kadar "sorgu adedi" üzerinden işliyordu, oysa
    /// gerçek maliyet TOKEN'dır. 20 tablolu bir şema tek sorguda 5-10k input token
    /// tüketebilir. Kiracı başına maliyet bilinmeden fiyat konulamaz.
    ///
    /// Faturalama sağlayıcısından bağımsız tutulur: sağlayıcı değişse de veri kalır.
    /// </summary>
    public class UsageRecord : ITenantScoped
    {
        public long Id { get; set; }

        public int CompanyId { get; set; }
        public int UserId { get; set; }

        /// <summary>Ölçüm tipi — bkz. UsageTypes ("query", ileride "export" vb.).</summary>
        public string Type { get; set; } = "query";

        public int? ProjectId { get; set; }
        public int? DataSourceId { get; set; }

        public string? Model { get; set; }
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
        public int TotalTokens => PromptTokens + CompletionTokens;

        /// <summary>Config'teki model fiyatlandırmasına göre tahmini maliyet (USD).</summary>
        public decimal EstimatedCostUsd { get; set; }

        public int DurationMs { get; set; }
        public bool IsSuccessful { get; set; }

        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    }

    public static class UsageTypes
    {
        public const string Query = "query";
    }
}
