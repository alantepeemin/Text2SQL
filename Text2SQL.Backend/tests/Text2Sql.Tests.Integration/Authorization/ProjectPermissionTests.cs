using System.Net;
using System.Net.Http.Json;
using Text2Sql.Domain.Enums;
using Text2Sql.Tests.Common.Builders;
using Text2Sql.Tests.Common.Fixtures;
using Xunit;

namespace Text2Sql.Tests.Integration.Authorization
{
    /// <summary>
    /// Proje düzeyi izinler (Viewer / Editor / Owner).
    ///
    /// Organizasyon izni "sorgu çalıştırabilir mi" sorusunu yanıtlar;
    /// proje izni "BU projede yetkisi var mı" sorusunu. İkisi de gereklidir
    /// ve bu ayrım daha önce test edilmiyordu.
    /// </summary>
    public class ProjectPermissionTests
    {
        private static Task<HttpResponseMessage> SorguCalistirAsync(
            HttpClient client, int projectId, int dataSourceId)
            => client.PostAsJsonAsync($"/api/v2/projects/{projectId}/queries/execute",
                new { databaseId = dataSourceId, question = "şehirler" });

        [Fact]
        public async Task ProjeErisimiOlmayanUye_ProjeyiGoremez()
        {
            using var app = new TestApp();
            var yonetici = await app.NewTenant("Izin Co A").WithSqliteDataSource().BuildAsync();
            var uye = await Members.AddAsync(app, yonetici.Client);

            var yanit = await uye.Client.GetAsync($"/api/v2/projects/{yonetici.ProjectId}");

            Assert.Equal(HttpStatusCode.Forbidden, yanit.StatusCode);
        }

        [Fact]
        public async Task Viewer_ProjeyiGorur_AmaSorguCalistiramaz()
        {
            using var app = new TestApp();
            var yonetici = await app.NewTenant("Izin Co B").WithSqliteDataSource().BuildAsync();
            var uye = await Members.AddAsync(app, yonetici.Client);

            var erisim = await Projects.GrantAccessAsync(
                yonetici.Client, yonetici.ProjectId, uye.UserId, ProjectPermission.Viewer);
            erisim.EnsureSuccessStatusCode();

            var goruntule = await uye.Client.GetAsync($"/api/v2/projects/{yonetici.ProjectId}");
            Assert.Equal(HttpStatusCode.OK, goruntule.StatusCode);

            // Sorgu çalıştırmak Editor gerektirir
            var sorgu = await SorguCalistirAsync(uye.Client, yonetici.ProjectId, yonetici.DataSourceId);
            Assert.Equal(HttpStatusCode.Forbidden, sorgu.StatusCode);
        }

        [Fact]
        public async Task Editor_SorguCalistirabilir()
        {
            using var app = new TestApp();
            var yonetici = await app.NewTenant("Izin Co C").WithSqliteDataSource().BuildAsync();
            var uye = await Members.AddAsync(app, yonetici.Client);

            var erisim = await Projects.GrantAccessAsync(
                yonetici.Client, yonetici.ProjectId, uye.UserId, ProjectPermission.Editor);
            erisim.EnsureSuccessStatusCode();

            var sorgu = await SorguCalistirAsync(uye.Client, yonetici.ProjectId, yonetici.DataSourceId);

            Assert.Equal(HttpStatusCode.OK, sorgu.StatusCode);
        }

        [Fact]
        public async Task Viewer_ProjeyiSilemez_OwnerGerekir()
        {
            using var app = new TestApp();
            var yonetici = await app.NewTenant("Izin Co D").WithProject().BuildAsync();
            var uye = await Members.AddAsync(app, yonetici.Client);

            await Projects.GrantAccessAsync(
                yonetici.Client, yonetici.ProjectId, uye.UserId, ProjectPermission.Viewer);

            var sil = await uye.Client.DeleteAsync($"/api/v2/projects/{yonetici.ProjectId}");

            Assert.Equal(HttpStatusCode.Forbidden, sil.StatusCode);
        }
    }
}
