using System.Net.Http.Json;
using Text2Sql.Tests.Common.Data;
using Text2Sql.Tests.Common.Fixtures;
using Text2Sql.Tests.Common.Http;

namespace Text2Sql.Tests.Common.Builders
{
    /// <summary>Organizasyona eklenmiş, kimliği doğrulanmış bir üye.</summary>
    public sealed class TestMember
    {
        public required HttpClient Client { get; init; }
        public required int UserId { get; init; }
        public required string Email { get; init; }
        public required string Role { get; init; }
    }

    /// <summary>
    /// Üye ekleme akışı: davet → kabul → onay → giriş.
    ///
    /// Dört adımlık bu zincir, yetkilendirme testlerinin çoğunda gerekiyor.
    /// Tek yerde tanımlı olmasaydı her test sınıfı kendi kopyasını taşırdı —
    /// ve akış değiştiğinde hepsi ayrı ayrı kırılırdı.
    /// </summary>
    public static class Members
    {
        public static async Task<TestMember> AddAsync(
            TestApp app, HttpClient adminClient, string role = "user")
        {
            var email = SampleData.UniqueEmail("uye");
            var username = "uye" + Guid.NewGuid().ToString("N")[..8];

            var davet = await adminClient.PostAsJsonAsync("/api/v2/company/invite-user",
                new { email, role });
            davet.EnsureSuccessStatusCode();

            var davetler = await (await adminClient.GetAsync("/api/v2/company/invitations")).ReadJsonAsync();
            var invitationToken = davetler.EnumerateArray()
                .First(d => d.GetProperty("email").GetString() == email)
                .GetProperty("invitationToken").GetString();

            var anon = app.CreateClient();
            var kabul = await anon.PostAsJsonAsync("/api/v2/auth/accept-invitation", new
            {
                invitationToken,
                username,
                password = Tenants.DefaultPassword
            });
            kabul.EnsureSuccessStatusCode();

            var bekleyenler = await (await adminClient.GetAsync("/api/v2/company/pending-users")).ReadJsonAsync();
            var userId = bekleyenler.EnumerateArray()
                .First(u => u.GetProperty("email").GetString() == email)
                .GetProperty("id").GetInt32();

            var onay = await adminClient.PostAsJsonAsync("/api/v2/company/approve-user",
                new { userId, action = "approve", role });
            onay.EnsureSuccessStatusCode();

            // Onay SecurityStamp'i yeniler → üyenin yeniden giriş yapması gerekir
            var giris = await Tenants.LoginAsync(anon, email);
            giris.EnsureSuccessStatusCode();
            var token = (await giris.ReadJsonAsync()).GetProperty("accessToken").GetString()!;

            var uyeIstemcisi = app.CreateClient().UseBearer(token);

            return new TestMember
            {
                Client = uyeIstemcisi,
                UserId = userId,
                Email  = email,
                Role   = role
            };
        }
    }
}
