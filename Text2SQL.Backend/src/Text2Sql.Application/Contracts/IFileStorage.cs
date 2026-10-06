namespace Text2Sql.Application.Contracts
{
    /// <summary>
    /// FAZ 4: Dosya depolama portu. İlk implementasyon yerel disk;
    /// S3/Azure Blob adaptörü aynı sözleşmeyle eklenebilir.
    /// Sorgu sonuç gövdeleri artık DB satırlarında değil burada yaşar
    /// (teknik borç #9: sınırsız DB büyümesi).
    /// </summary>
    public interface IFileStorage
    {
        /// <summary>İçeriği kaydeder, depo köküne göreli yolu döndürür.</summary>
        Task<string> SaveTextAsync(string relativeDirectory, string fileName, string content, CancellationToken ct = default);

        /// <summary>Göreli yoldan okur; dosya yoksa null döner.</summary>
        Task<string?> ReadTextAsync(string relativePath, CancellationToken ct = default);

        /// <summary>SaaS-8: Dosyayı siler (yoksa sessizce geçer). Saklama süresi işi kullanır.</summary>
        Task DeleteAsync(string relativePath, CancellationToken ct = default);

        /// <summary>SaaS-8: Bir dizini ve içeriğini siler — kiracı verisi imhası (GDPR).</summary>
        Task DeleteDirectoryAsync(string relativeDirectory, CancellationToken ct = default);
    }
}
