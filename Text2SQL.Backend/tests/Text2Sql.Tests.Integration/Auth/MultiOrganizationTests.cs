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

namespace Text2Sql.Tests.Integration.Auth
{
    /// <summary>
    /// SaaS-2 — Çok organizasyonlu üyelik senaryoları.
    /// </summary>
    public class MultiOrganizationTests : IClassFixture<TestApp>
    {
        private readonly TestApp _factory;
        public MultiOrganizationTests(TestApp factory) => _factory = factory;

        private static readonly System.Text.Json.JsonSerializerOptions J =
            new(System.Text.Json.JsonSerializerDefaults.Web);

        // ApiClient.RegisterCompanyAndGetTokenAsync ile AYNI parola olmalı —
        // aksi halde yeniden giriş 401 alır (ilk koşumdaki hata buydu).
        private const string Password = "P@ssw0rd123";

        [Fact]
        public async Task KayitSonrasi_TekOrganizasyonListelenir_VeAktifOlanODur()
        {
            var client = _factory.CreateClient();
            var token = await Tenants.RegisterAsync(
                client, "Solo Org", $"solo-{Guid.NewGuid():N}@test.local");
            client.UseBearer(token);

            var resp = await client.GetAsync("/api/users/me/organizations");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var body = await resp.Content.ReadFromJsonAsync<JsonElement>(J);
            var orgs = body.GetProperty("data");
            Assert.Equal(1, orgs.GetArrayLength());
            Assert.True(orgs[0].GetProperty("isPrimary").GetBoolean());
            Assert.Equal("admin", orgs[0].GetProperty("role").GetString());
        }

        [Fact]
        public async Task AyniKullanici_IkinciOrganizasyona_DavetleKatilabilir()
        {
            var email = $"multi-{Guid.NewGuid():N}@test.local";

            // 1. organizasyon: kullanıcı kendi şirketini kurar (admin)
            var clientA = _factory.CreateClient();
            var tokenA = await Tenants.RegisterAsync(clientA, "Org A", email);
            clientA.UseBearer(tokenA);

            // 2. organizasyon: başka bir admin bu e-postayı davet eder
            var clientB = _factory.CreateClient();
            var tokenB = await Tenants.RegisterAsync(
                clientB, "Org B", $"ownerb-{Guid.NewGuid():N}@test.local");
            clientB.UseBearer(tokenB);

            var invite = await clientB.PostAsJsonAsync("/api/company/invite-user",
                new { email, role = "user" });
            Assert.Equal(HttpStatusCode.OK, invite.StatusCode);

            var list = await clientB.GetAsync("/api/company/invitations");
            var listBody = await list.Content.ReadFromJsonAsync<JsonElement>(J);
            var invitationToken = listBody.GetProperty("data")[0]
                .GetProperty("invitationToken").GetString();

            // SaaS-2 YENİ: mevcut hesap ikinci organizasyona üye olur
            var anon = _factory.CreateClient();
            var accept = await anon.PostAsJsonAsync("/api/auth/accept-invitation", new
            {
                invitationToken,
                username = "ignoredforexistinguser", // mevcut hesapta yok sayılır ama doğrulamayı geçmeli
                password = "IgnoredP@ss123"
            });
            Assert.Equal(HttpStatusCode.OK, accept.StatusCode);

            // B'nin yöneticisi onaylar
            var pending = await clientB.GetAsync("/api/company/pending-users");
            var pendingBody = await pending.Content.ReadFromJsonAsync<JsonElement>(J);
            Assert.Equal(1, pendingBody.GetProperty("data").GetArrayLength());
            var userId = pendingBody.GetProperty("data")[0].GetProperty("id").GetInt32();

            var approve = await clientB.PostAsJsonAsync("/api/company/approve-user",
                new { userId, action = "approve", role = "user" });
            Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

            // Kullanıcı artık İKİ organizasyonda üye — ama önce yeniden login
            // gerekir (onay SecurityStamp'i yeniler → eski token düşer).
            var reLogin = await anon.PostAsJsonAsync("/api/auth/login",
                new { email, password = Password });
            Assert.Equal(HttpStatusCode.OK, reLogin.StatusCode);
            var loginBody = await reLogin.Content.ReadFromJsonAsync<JsonElement>(J);
            var orgs = loginBody.GetProperty("data").GetProperty("organizations");
            Assert.Equal(2, orgs.GetArrayLength());
        }

        [Fact]
        public async Task OrganizasyonDegistir_YeniTokenBaskaKiracidaCalisir()
        {
            var email = $"switch-{Guid.NewGuid():N}@test.local";

            var clientA = _factory.CreateClient();
            var tokenA = await Tenants.RegisterAsync(clientA, "Switch A", email);
            clientA.UseBearer(tokenA);
            var projeA = await Projects.CreateAsync(clientA, "A Projesi");

            // İkinci organizasyon + davet + onay
            var clientB = _factory.CreateClient();
            var tokenB = await Tenants.RegisterAsync(
                clientB, "Switch B", $"ownerb2-{Guid.NewGuid():N}@test.local");
            clientB.UseBearer(tokenB);
            var invite = await clientB.PostAsJsonAsync("/api/company/invite-user",
                new { email, role = "manager" });
            Assert.Equal(HttpStatusCode.OK, invite.StatusCode);

            var list = await clientB.GetAsync("/api/company/invitations");
            var lb = await list.Content.ReadFromJsonAsync<JsonElement>(J);
            Assert.True(lb.GetProperty("data").GetArrayLength() > 0, "Davet listesi boş.");
            var invToken = lb.GetProperty("data")[0].GetProperty("invitationToken").GetString();

            var anon = _factory.CreateClient();
            // username en az 3 karakter olmalı (AcceptInvitationDto doğrulaması) —
            // ilk koşumda "x" kullanılmış ve istek 400 almıştı.
            var accept = await anon.PostAsJsonAsync("/api/auth/accept-invitation",
                new { invitationToken = invToken, username = "switchuser", password = "IgnoredP@ss123" });
            Assert.Equal(HttpStatusCode.OK, accept.StatusCode);

            var pending = await clientB.GetAsync("/api/company/pending-users");
            var pb = await pending.Content.ReadFromJsonAsync<JsonElement>(J);
            Assert.True(pb.GetProperty("data").GetArrayLength() > 0, "Onay bekleyen üyelik yok.");
            var userId = pb.GetProperty("data")[0].GetProperty("id").GetInt32();

            var approve = await clientB.PostAsJsonAsync("/api/company/approve-user",
                new { userId, action = "approve", role = "manager" });
            Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

            // Yeniden login → A aktif (birincil)
            var login = await anon.PostAsJsonAsync("/api/auth/login",
                new { email, password = Password });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var loginBody = await login.Content.ReadFromJsonAsync<JsonElement>(J);
            var accessToken = loginBody.GetProperty("data").GetProperty("accessToken").GetString()!;
            var orgB = loginBody.GetProperty("data").GetProperty("organizations")
                .EnumerateArray().First(o => o.GetProperty("role").GetString() == "manager")
                .GetProperty("id").GetInt32();

            var client = _factory.CreateClient();
            client.UseBearer(accessToken);

            // A bağlamında A'nın projesi görünür
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/projects/{projeA}")).StatusCode);

            // B'ye geç
            var switchResp = await client.PostAsJsonAsync("/api/auth/switch-organization",
                new { organizationId = orgB });
            Assert.Equal(HttpStatusCode.OK, switchResp.StatusCode);
            var sb = await switchResp.Content.ReadFromJsonAsync<JsonElement>(J);
            var tokenInB = sb.GetProperty("data").GetProperty("accessToken").GetString()!;
            Assert.Equal(orgB, sb.GetProperty("data").GetProperty("activeOrganizationId").GetInt32());
            Assert.Equal("manager", sb.GetProperty("data").GetProperty("role").GetString());

            // B bağlamında A'nın projesi ARTIK GÖRÜNMEZ (kiracı izolasyonu)
            var clientInB = _factory.CreateClient();
            clientInB.UseBearer(tokenInB);
            var crossResp = await clientInB.GetAsync($"/api/projects/{projeA}");
            Assert.Equal(HttpStatusCode.NotFound, crossResp.StatusCode);
        }

        [Fact]
        public async Task UyeOlmadigiOrganizasyona_Gecemez()
        {
            var clientA = _factory.CreateClient();
            var tokenA = await Tenants.RegisterAsync(
                clientA, "NoAccess A", $"na-{Guid.NewGuid():N}@test.local");
            clientA.UseBearer(tokenA);

            var clientB = _factory.CreateClient();
            var tokenB = await Tenants.RegisterAsync(
                clientB, "NoAccess B", $"nb-{Guid.NewGuid():N}@test.local");
            clientB.UseBearer(tokenB);
            var orgsB = await clientB.GetAsync("/api/users/me/organizations");
            var ob = await orgsB.Content.ReadFromJsonAsync<JsonElement>(J);
            var orgBId = ob.GetProperty("data")[0].GetProperty("id").GetInt32();

            // A kullanıcısı B organizasyonuna geçmeyi dener → 404
            var resp = await clientA.PostAsJsonAsync("/api/auth/switch-organization",
                new { organizationId = orgBId });
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }
    }
}
