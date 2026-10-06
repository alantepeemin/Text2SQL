using System.Net.Http.Json;
using Text2Sql.Tests.Common.Http;

namespace Text2Sql.Tests.Common.Builders
{
    /// <summary>
    /// Kurulmuş bir test kiracısı: kimliği doğrulanmış istemci + oluşturulan
    /// kaynakların kimlikleri. Testler kurulum ayrıntısıyla değil, doğrulamak
    /// istedikleri davranışla ilgilenir.
    /// </summary>
    public sealed class TestTenant
    {
        public required HttpClient Client { get; init; }
        public required string AccessToken { get; init; }
        public required string OrganizationName { get; init; }
        public required string AdminEmail { get; init; }
        public required int CompanyId { get; init; }

        /// <summary>İlk proje (WithProject çağrıldıysa), aksi halde 0.</summary>
        public int ProjectId { get; internal set; }

        /// <summary>İlk veri kaynağı (WithSqliteDataSource çağrıldıysa), aksi halde 0.</summary>
        public int DataSourceId { get; internal set; }

        public string ProjectUrl(string suffix = "", string version = "v2")
            => $"/api/{version}/projects/{ProjectId}{suffix}";

        /// <summary>Bu kiracıya ait yeni bir proje oluşturur ve kimliğini döndürür.</summary>
        public async Task<int> CreateProjectAsync(string ad = "Proje")
        {
            var resp = await Client.PostAsJsonAsync("/api/v2/projects", new { name = ad, description = "test" });
            resp.EnsureSuccessStatusCode();
            return (await resp.ReadJsonAsync()).GetInt32();
        }

        /// <summary>Bu kiracıya ait projeye örnek SQLite veri kaynağı yükler.</summary>
        public Task<(int dataSourceId, HttpResponseMessage response)> UploadSqliteAsync(
            int projectId, byte[]? bytes = null, string dosyaAdi = "sample.sqlite")
            => DataSources.UploadSqliteAsync(Client, projectId, bytes, dosyaAdi);

    }
}
