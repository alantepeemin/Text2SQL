using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Text2Sql.Tests.Common.Data;
using Text2Sql.Tests.Common.Fixtures;
using Text2Sql.Tests.Common.Http;

namespace Text2Sql.Tests.Common.Builders
{
    /// <summary>
    /// Kiracı kurulumu için akıcı (fluent) kurucu.
    ///
    /// Önceki test kodunda her sınıf kendi 15 satırlık kurulum yardımcısını
    /// yazıyordu; kayıt akışı değiştiğinde hepsi ayrı ayrı düzeltiliyordu.
    /// Kurulum artık TEK yerde tanımlıdır:
    ///
    ///     var kiraci = await app.NewTenant("Sorgu A.Ş.")
    ///                           .WithProject()
    ///                           .WithSqliteDataSource()
    ///                           .BuildAsync();
    /// </summary>
    public sealed class TenantBuilder
    {
        private readonly TestApp _app;
        private readonly string _organizationName;

        private string? _email;
        private string _username = "adminuser";
        private string _password = "P@ssw0rd123";
        private string? _planCode;
        private bool _withProject;
        private string _projectName = "Proje";
        private bool _withDataSource;
        private int _maxUsers = 10;
        private int _monthlyQueryLimit = 100;

        internal TenantBuilder(TestApp app, string organizationName)
        {
            _app = app;
            _organizationName = organizationName;
        }

        public TenantBuilder WithAdmin(string email, string username = "adminuser", string password = "P@ssw0rd123")
        {
            _email = email;
            _username = username;
            _password = password;
            return this;
        }

        /// <summary>Organizasyon limitlerini kayıt anında belirler (kota testleri için).</summary>
        public TenantBuilder WithLimits(int maxUsers = 10, int monthlyQueryLimit = 100)
        {
            _maxUsers = maxUsers;
            _monthlyQueryLimit = monthlyQueryLimit;
            return this;
        }

        /// <summary>Planı doğrudan veritabanında yükseltir (ödeme akışı test kapsamı dışı).</summary>
        public TenantBuilder WithPlan(string planCode)
        {
            _planCode = planCode;
            return this;
        }

        public TenantBuilder WithProject(string ad = "Proje")
        {
            _withProject = true;
            _projectName = ad;
            return this;
        }

        /// <summary>Projeye örnek SQLite veri kaynağı yükler (WithProject gerektirir).</summary>
        public TenantBuilder WithSqliteDataSource()
        {
            _withProject = true;
            _withDataSource = true;
            return this;
        }

        public async Task<TestTenant> BuildAsync()
        {
            var client = _app.CreateClient();
            var email = _email ?? SampleData.UniqueEmail();

            // 1. Organizasyon oluştur → kayıt jetonu
            var step1 = await client.PostAsJsonAsync("/api/v2/auth/create-company", new
            {
                name = _organizationName,
                maxUsers = _maxUsers,
                monthlyQueryLimit = _monthlyQueryLimit
            });
            step1.EnsureSuccessStatusCode();

            var registrationToken = (await step1.ReadJsonAsync())
                .GetProperty("registrationToken").GetString()!;

            // 2. İlk yöneticiyi oluştur → access token
            var step2 = await client.PostAsJsonAsync("/api/v2/auth/complete-registration", new
            {
                registrationToken,
                username = _username,
                email,
                password = _password
            });
            step2.EnsureSuccessStatusCode();

            var token = (await step2.ReadJsonAsync()).GetProperty("accessToken").GetString()!;
            client.UseBearer(token);

            var companyId = await _app.QueryDatabaseAsync(db => db.Companies
                .IgnoreQueryFilters()
                .Where(c => c.Name == _organizationName)
                .OrderByDescending(c => c.Id)
                .Select(c => c.Id)
                .FirstAsync());

            var tenant = new TestTenant
            {
                Client = client,
                AccessToken = token,
                OrganizationName = _organizationName,
                AdminEmail = email,
                CompanyId = companyId
            };

            if (_planCode != null)
                await Plans.UpgradeAsync(_app, companyId, _planCode);

            if (_withProject)
                tenant.ProjectId = await tenant.CreateProjectAsync(_projectName);

            if (_withDataSource)
            {
                var (dataSourceId, response) = await tenant.UploadSqliteAsync(tenant.ProjectId);
                response.EnsureSuccessStatusCode();
                tenant.DataSourceId = dataSourceId;
            }

            return tenant;
        }
    }

    public static class TenantBuilderExtensions
    {
        /// <summary>Yeni bir kiracı kurulumu başlatır.</summary>
        public static TenantBuilder NewTenant(this TestApp app, string organizationName)
            => new(app, organizationName);
    }
}
