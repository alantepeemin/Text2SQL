using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using Text2Sql.Domain.Authorization;
using Text2Sql.Tests.Common.Assertions;
using Text2Sql.Tests.Common.Builders;
using Text2Sql.Tests.Common.Data;
using Text2Sql.Tests.Common.Fakes;
using Text2Sql.Tests.Common.Fixtures;
using Text2Sql.Tests.Common.Http;
using Xunit;

namespace Text2Sql.Tests.Integration.Authorization
{
    /// <summary>
    /// SaaS-4 — Uçtan uca yetkilendirme: gerçek rollerle HTTP davranışı.
    /// </summary>
    public class PermissionEnforcementTests : IClassFixture<TestApp>
    {
        private readonly TestApp _factory;
        public PermissionEnforcementTests(TestApp factory) => _factory = factory;

        private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);
        private const string Password = "P@ssw0rd123";

        /// <summary>Organizasyon kurar, ikinci bir kullanıcıyı istenen rolle üye yapar ve o kullanıcının token'ını döndürür.</summary>
        private async Task<HttpClient> UyeIstemcisiOlusturAsync(string rol)
        {
            var adminClient = _factory.CreateClient();
            var adminEmail = $"perm-admin-{Guid.NewGuid():N}@test.local";
            var adminToken = await Tenants.RegisterAsync(
                adminClient, $"Perm Org {Guid.NewGuid():N}"[..20], adminEmail);
            adminClient.UseBearer(adminToken);

            var memberEmail = $"perm-member-{Guid.NewGuid():N}@test.local";
            var invite = await adminClient.PostAsJsonAsync("/api/company/invite-user",
                new { email = memberEmail, role = rol });
            Assert.Equal(HttpStatusCode.OK, invite.StatusCode);

            var list = await adminClient.GetAsync("/api/company/invitations");
            var lb = await list.Content.ReadFromJsonAsync<JsonElement>(J);
            var invToken = lb.GetProperty("data")[0].GetProperty("invitationToken").GetString();

            var anon = _factory.CreateClient();
            var accept = await anon.PostAsJsonAsync("/api/auth/accept-invitation",
                new { invitationToken = invToken, username = "permmember", password = Password });
            Assert.Equal(HttpStatusCode.OK, accept.StatusCode);

            var pending = await adminClient.GetAsync("/api/company/pending-users");
            var pb = await pending.Content.ReadFromJsonAsync<JsonElement>(J);
            var userId = pb.GetProperty("data")[0].GetProperty("id").GetInt32();

            var approve = await adminClient.PostAsJsonAsync("/api/company/approve-user",
                new { userId, action = "approve", role = rol });
            Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

            var login = await anon.PostAsJsonAsync("/api/auth/login",
                new { email = memberEmail, password = Password });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var body = await login.Content.ReadFromJsonAsync<JsonElement>(J);
            var token = body.GetProperty("data").GetProperty("accessToken").GetString()!;
            Assert.Equal(rol, body.GetProperty("data").GetProperty("role").GetString());

            var client = _factory.CreateClient();
            client.UseBearer(token);
            return client;
        }

        [Fact]
        public async Task Manager_ProjeOlusturabilir_AmaDenetimKaydiniOkuyamaz()
        {
            var manager = await UyeIstemcisiOlusturAsync("manager");

            var create = await manager.PostAsJsonAsync("/api/projects",
                new { name = "Manager Projesi", description = "izin testi" });
            Assert.Equal(HttpStatusCode.OK, create.StatusCode);

            var audit = await manager.GetAsync("/api/company/audit-logs");
            Assert.Equal(HttpStatusCode.Forbidden, audit.StatusCode);

            var members = await manager.GetAsync("/api/company/users");
            Assert.Equal(HttpStatusCode.OK, members.StatusCode); // members.read var
        }

        [Fact]
        public async Task User_ProjeOlusturamaz_VeDavetEdemez()
        {
            var user = await UyeIstemcisiOlusturAsync("user");

            var create = await user.PostAsJsonAsync("/api/projects",
                new { name = "Olmayacak Proje", description = "x" });
            Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);

            var invite = await user.PostAsJsonAsync("/api/company/invite-user",
                new { email = "biri@test.local", role = "user" });
            Assert.Equal(HttpStatusCode.Forbidden, invite.StatusCode);

            var usage = await user.GetAsync("/api/company/usage");
            Assert.Equal(HttpStatusCode.Forbidden, usage.StatusCode);
        }

        [Fact]
        public async Task RolClaimi_TokendanDogruOkunur_ClaimMapTuzagiRegresyonu()
        {
            // REGRESYON KORUMASI (SaaS-4 r2): JWT kütüphanesi "role" claim'ini
            // ClaimTypes.Role'a map ettiği için FindFirst("role") null döner.
            // Bu tuzağa geri düşülürse aşağıdaki istek 403 olur ve test kırılır.
            // Ayrıca login yanıtındaki rol ile token'daki etkin rolün aynı
            // olduğunu dolaylı olarak kanıtlar.
            var client = _factory.CreateClient();
            var email = $"claimmap-{Guid.NewGuid():N}@test.local";
            var token = await Tenants.RegisterAsync(
                client, "ClaimMap Org", email);
            client.UseBearer(token);
            await Plans.UpgradeAsync(_factory, "ClaimMap Org", "enterprise"); // audit.read

            // admin rolüne özel bir izin gerektiren uç (audit.read yalnızca admin'de)
            var audit = await client.GetAsync("/api/company/audit-logs");
            Assert.Equal(HttpStatusCode.OK, audit.StatusCode);

            // Rolün gerçekten "admin" olarak taşındığını login yanıtından doğrula
            var login = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login",
                new { email, password = Password });
            var body = await login.Content.ReadFromJsonAsync<JsonElement>(J);
            Assert.Equal("admin", body.GetProperty("data").GetProperty("role").GetString());
        }

        [Fact]
        public async Task Admin_TumYonetselUclaraErisir()
        {
            var client = _factory.CreateClient();
            var token = await Tenants.RegisterAsync(
                client, "Admin Perm Org", $"adminperm-{Guid.NewGuid():N}@test.local");
            client.UseBearer(token);
            await Plans.UpgradeAsync(_factory, "Admin Perm Org", "enterprise");

            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/company/users")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/company/usage")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/company/audit-logs")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/company/pending-users")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/projects/all")).StatusCode);
        }
    }
}
