using System.Net.Http.Json;
using Text2Sql.Domain.Enums;
using Text2Sql.Tests.Common.Http;

namespace Text2Sql.Tests.Common.Builders
{
    public static class Projects
    {
        public static async Task<int> CreateAsync(HttpClient client, string ad = "Proje")
        {
            var resp = await client.PostAsJsonAsync("/api/v2/projects",
                new { name = ad, description = "test" });
            resp.EnsureSuccessStatusCode();
            return (await resp.ReadJsonAsync()).GetInt32();
        }

        /// <summary>
        /// Proje erişimi verir.
        ///
        /// İzin SAYISAL değer olarak gönderilir: API'de string→enum dönüştürücü
        /// yapılandırılmamıştır, dolayısıyla "Viewer" metni 400 döndürür.
        /// </summary>
        public static Task<HttpResponseMessage> GrantAccessAsync(
            HttpClient client, int projectId, int userId, ProjectPermission permission)
            => client.PostAsJsonAsync($"/api/v2/projects/{projectId}/access",
                new { userId, permission = (int)permission });

        public static Task<HttpResponseMessage> UpdateAccessAsync(
            HttpClient client, int projectId, int userId, ProjectPermission permission)
            => client.PutAsJsonAsync($"/api/v2/projects/{projectId}/access/{userId}",
                new { permission = (int)permission });
    }
}
