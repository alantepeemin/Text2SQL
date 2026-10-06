using Microsoft.Extensions.Configuration;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using Text2Sql.Infrastructure.Storage;
using Text2Sql.Tests.Common.Assertions;
using Text2Sql.Tests.Common.Builders;
using Text2Sql.Tests.Common.Data;
using Text2Sql.Tests.Common.Fakes;
using Text2Sql.Tests.Common.Fixtures;
using Text2Sql.Tests.Common.Http;
using Xunit;

namespace Text2Sql.Tests.Integration.Auth
{
    /// <summary>FAZ 4 — davetiye akışı (NoOp e-posta ile) uçtan uca.</summary>
    public class InvitationFlowTests : IClassFixture<TestApp>
    {
        private readonly TestApp _factory;
        public InvitationFlowTests(TestApp factory) => _factory = factory;

        [Fact]
        public async Task DavetOlustur_ListedeGorunur_VeDavetleKayitOlunur()
        {
            var client = _factory.CreateClient();
            var token = await Tenants.RegisterAsync(
                client, "Invite Co", "admin@invite-test.com");
            client.UseBearer(token);

            // Davet oluştur (Email:Enabled=false → NoOp, ama kayıt oluşmalı)
            var invite = await client.PostAsJsonAsync("/api/company/invite-user",
                new { email = "davetli@invite-test.com", role = "user" });
            Assert.Equal(HttpStatusCode.OK, invite.StatusCode);

            // Bekleyen davetlerde görünmeli — token'ı oradan alalım
            var list = await client.GetAsync("/api/company/invitations");
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            var body = await list.Content.ReadFromJsonAsync<JsonElement>(TestJson.Options);
            var invitations = body.GetProperty("data");
            Assert.Equal(1, invitations.GetArrayLength());

            var invitationToken = invitations[0].GetProperty("invitationToken").GetString();
            Assert.False(string.IsNullOrEmpty(invitationToken));

            // Davetle kayıt (anonim istemci) → onay bekleyen kullanıcı
            var anon = _factory.CreateClient();
            var accept = await anon.PostAsJsonAsync("/api/auth/accept-invitation", new
            {
                invitationToken,
                username = "davetliuser",
                password = "P@ssw0rd123"
            });
            Assert.Equal(HttpStatusCode.OK, accept.StatusCode);

            // Bekleyen kullanıcılar listesinde görünmeli
            var pending = await client.GetAsync("/api/company/pending-users");
            var pendingBody = await pending.Content.ReadFromJsonAsync<JsonElement>(TestJson.Options);
            Assert.Equal(1, pendingBody.GetProperty("data").GetArrayLength());
        }
    }
}
