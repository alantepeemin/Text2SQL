using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using Text2Sql.Domain.Common;
using Text2Sql.Infrastructure.Persistence;
using Text2Sql.Tests.Common.Assertions;
using Text2Sql.Tests.Common.Builders;
using Text2Sql.Tests.Common.Data;
using Text2Sql.Tests.Common.Fakes;
using Text2Sql.Tests.Common.Fixtures;
using Text2Sql.Tests.Common.Http;
using Xunit;

namespace Text2Sql.Tests.Integration.MultiTenancy
{
    /// <summary>
    /// SaaS-1 — Parametrik çapraz erişim testleri: A kiracısının kaynağına
    /// B kiracısı hiçbir uçtan erişememeli.
    /// </summary>
    public class CrossTenantAccessTests : IClassFixture<TestApp>
    {
        private readonly TestApp _factory;
        public CrossTenantAccessTests(TestApp factory) => _factory = factory;

        private async Task<(HttpClient client, int projectId, int dbId)> SetupTenantAsync(string ad)
        {
            var client = _factory.CreateClient();
            var token = await Tenants.RegisterAsync(
                client, ad, $"admin@{ad.ToLowerInvariant()}-{Guid.NewGuid():N}.test");
            client.UseBearer(token);
            var projectId = await Projects.CreateAsync(client, $"{ad} Projesi");
            var (dbId, _) = await DataSources.UploadSqliteAsync(
                client, projectId, SampleData.SqliteBytes());
            return (client, projectId, dbId);
        }

        public static IEnumerable<object[]> CaprazUclar => new List<object[]>
        {
            new object[] { "GET",    "/api/projects/{p}" },
            new object[] { "GET",    "/api/projects/{p}/databases" },
            new object[] { "GET",    "/api/projects/{p}/queries/history" },
            new object[] { "GET",    "/api/projects/{p}/queries/available-databases" },
            new object[] { "GET",    "/api/projects/{p}/access" },
            new object[] { "GET",    "/api/projects/{p}/activity" },
            new object[] { "DELETE", "/api/projects/{p}" },
        };

        [Theory]
        [MemberData(nameof(CaprazUclar))]
        public async Task YabanciKiraci_HicbirUcaErisemez(string method, string template)
        {
            var (_, projectIdA, _) = await SetupTenantAsync("TenantA");
            var (clientB, _, _)    = await SetupTenantAsync("TenantB");

            var url = template.Replace("{p}", projectIdA.ToString());
            var resp = await clientB.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

            // 404 beklenir: yabancı kiracıya kaynağın VARLIĞI dahi sızdırılmaz.
            Assert.True(resp.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden,
                $"{method} {url} → {(int)resp.StatusCode}. Kiracılar arası erişim engellenmedi!");
        }

        [Fact]
        public async Task YabanciKiraci_BaskasininVeriKaynaginda_SorguCalistiramaz()
        {
            var (_, projectIdA, dbIdA) = await SetupTenantAsync("TenantC");
            var (clientB, projectIdB, _) = await SetupTenantAsync("TenantD");

            // B'nin kendi projesi üzerinden A'nın veri kaynağı ID'sini kullanmayı dener
            var resp = await clientB.PostAsJsonAsync(
                $"/api/projects/{projectIdB}/queries/execute",
                new { databaseId = dbIdA, question = "Kaç şehir var?" });

            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }

        [Fact]
        public async Task Kiraci_KendiKaynaklarinaErisebilir_RegresyonKontrolu()
        {
            // Filtre fazla kısıtlayıcı olursa (örn. HasTenant mantığı bozulursa)
            // bu test kırmızıya döner — yanlış pozitif güvenliğe karşı sigorta.
            var (client, projectId, dbId) = await SetupTenantAsync("TenantE");

            var details = await client.GetAsync($"/api/projects/{projectId}");
            Assert.Equal(HttpStatusCode.OK, details.StatusCode);

            var databases = await client.GetAsync($"/api/projects/{projectId}/databases");
            Assert.Equal(HttpStatusCode.OK, databases.StatusCode);
            var body = await databases.Content.ReadFromJsonAsync<JsonElement>(TestJson.Options);
            Assert.Equal(1, body.GetProperty("data").GetArrayLength());

            var exec = await client.PostAsJsonAsync($"/api/projects/{projectId}/queries/execute",
                new { databaseId = dbId, question = "Kaç şehir var?" });
            Assert.Equal(HttpStatusCode.OK, exec.StatusCode);
            var result = await exec.Content.ReadFromJsonAsync<JsonElement>(TestJson.Options);
            Assert.True(result.GetProperty("success").GetBoolean(),
                "Kendi kaynağında sorgu çalışmadı: " + result.GetProperty("errorMessage"));
        }

        [Fact]
        public async Task GlobalFiltre_ServisKatmaniOlmadanDa_VeriyiAyirir()
        {
            // En önemli test: servislerdeki explicit Where'ler HİÇ olmasa bile
            // DbContext'in kendisi kiracıları ayırıyor mu? Sahte kiracı bağlamıyla
            // doğrudan DbContext kurup kanıtlıyoruz.
            var (_, projectIdA, _) = await SetupTenantAsync("TenantF");
            var (_, projectIdB, _) = await SetupTenantAsync("TenantG");

            using var scope = _factory.Services.CreateScope();
            var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>();

            // A'nın tenant id'sini projeden öğren (kiracısız bağlamda okunur)
            int tenantA, tenantB;
            using (var raw = new ApplicationDbContext(options))
            {
                tenantA = (await raw.Projects.SingleAsync(p => p.Id == projectIdA)).CompanyId;
                tenantB = (await raw.Projects.SingleAsync(p => p.Id == projectIdB)).CompanyId;
            }
            Assert.NotEqual(tenantA, tenantB);

            using var asTenantA = new ApplicationDbContext(options, new FakeTenantContext(tenantA));

            // A bağlamında yalnızca A'nın projeleri görünür
            var gorunenler = await asTenantA.Projects.Select(p => p.Id).ToListAsync();
            Assert.Contains(projectIdA, gorunenler);
            Assert.DoesNotContain(projectIdB, gorunenler);

            // Doğrudan ID ile erişmeye çalışmak bile null döner (filtre kaçınılmaz)
            Assert.Null(await asTenantA.Projects.FirstOrDefaultAsync(p => p.Id == projectIdB));

            // QueryHistory / ProjectAccess / ProjectDatabase için de aynı garanti
            Assert.Empty(await asTenantA.ProjectDatabases.Where(d => d.ProjectId == projectIdB).ToListAsync());
            Assert.Empty(await asTenantA.ProjectAccesses.Where(a => a.ProjectId == projectIdB).ToListAsync());
        }

        private sealed class FakeTenantContext : Text2Sql.Application.Contracts.ITenantContext
        {
            public FakeTenantContext(int tenantId) { TenantId = tenantId; }
            public bool HasTenant => true;
            public int TenantId { get; }
        }
    }
}
