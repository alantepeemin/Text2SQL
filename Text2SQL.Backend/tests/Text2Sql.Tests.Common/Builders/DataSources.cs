using System.Net.Http.Headers;
using Text2Sql.Tests.Common.Data;
using Text2Sql.Tests.Common.Http;

namespace Text2Sql.Tests.Common.Builders
{
    /// <summary>Veri kaynağı ekleme akışları (multipart form kurulumu tek yerde).</summary>
    public static class DataSources
    {
        public static async Task<(int dataSourceId, HttpResponseMessage response)> UploadSqliteAsync(
            HttpClient client, int projectId, byte[]? bytes = null, string dosyaAdi = "sample.sqlite")
        {
            using var form = new MultipartFormDataContent
            {
                { new StringContent("TestDB"),    "Name"   },
                { new StringContent("LocalFile"), "Mode"   },
                { new StringContent("Sqlite"),    "DbType" }
            };

            var file = new ByteArrayContent(bytes ?? SampleData.SqliteBytes());
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(file, "DatabaseFile", dosyaAdi);

            var response = await client.PostAsync($"/api/v2/projects/{projectId}/databases", form);
            if (!response.IsSuccessStatusCode)
                return (0, response);

            var id = (await response.ReadJsonAsync()).GetProperty("id").GetInt32();
            return (id, response);
        }

        /// <summary>Uzak PostgreSQL kaynağı ekleme denemesi (paket kapısı testleri için).</summary>
        public static Task<HttpResponseMessage> AddRemoteAsync(
            HttpClient client, int projectId, string connectionString = "Host=localhost;Database=x;Username=u;Password=p")
        {
            var form = new MultipartFormDataContent
            {
                { new StringContent("RemoteDB"),        "Name"             },
                { new StringContent("ConnectionString"),"Mode"             },
                { new StringContent("PostgreSql"),      "DbType"           },
                { new StringContent(connectionString),  "ConnectionString" }
            };

            return client.PostAsync($"/api/v2/projects/{projectId}/databases", form);
        }
    }
}
