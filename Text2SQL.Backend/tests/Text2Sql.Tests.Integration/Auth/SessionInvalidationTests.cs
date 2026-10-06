using System.Net;
using System.Net.Http.Json;
using Text2Sql.Tests.Common.Builders;
using Text2Sql.Tests.Common.Fixtures;
using Text2Sql.Tests.Common.Http;
using Xunit;

namespace Text2Sql.Tests.Integration.Auth
{
    /// <summary>
    /// Oturum geçersizleştirme. Bir güvenlik olayında (parola değişimi, çıkış,
    /// hesap kapatma) mevcut token'ların ANINDA düşmesi gerekir; aksi halde
    /// çalınmış bir token access token ömrü boyunca geçerli kalır.
    /// </summary>
    public class SessionInvalidationTests
    {
        [Fact]
        public async Task ParolaDegisimi_EskiTokeniAnindaGecersizKilar()
        {
            using var app = new TestApp();
            var kiraci = await app.NewTenant("Oturum Co").BuildAsync();

            var oncesi = await kiraci.Client.GetAsync("/api/v2/users/profile");
            Assert.Equal(HttpStatusCode.OK, oncesi.StatusCode);

            var degistir = await kiraci.Client.PutAsJsonAsync("/api/v2/users/change-password", new
            {
                currentPassword = Tenants.DefaultPassword,
                newPassword = "YeniP@ssw0rd456"
            });
            degistir.EnsureSuccessStatusCode();

            // Aynı token artık kabul edilmemeli (SecurityStamp yenilendi)
            var sonrasi = await kiraci.Client.GetAsync("/api/v2/users/profile");
            Assert.True(sonrasi.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
                $"Parola değişiminden sonra eski token hâlâ çalışıyor: {(int)sonrasi.StatusCode}");

            // Yeni parola ile giriş çalışmalı
            var giris = await Tenants.LoginAsync(app.CreateClient(), kiraci.AdminEmail, "YeniP@ssw0rd456");
            Assert.Equal(HttpStatusCode.OK, giris.StatusCode);
        }

        [Fact]
        public async Task Cikis_RefreshTokeniIptalEder()
        {
            using var app = new TestApp();
            var istemci = app.CreateClient();
            var email = Text2Sql.Tests.Common.Data.SampleData.UniqueEmail();
            await Tenants.RegisterAsync(istemci, "Cikis Co", email);

            var giris = await Tenants.LoginAsync(istemci, email);
            var govde = await giris.ReadJsonAsync();
            var accessToken  = govde.GetProperty("accessToken").GetString()!;
            var refreshToken = govde.GetProperty("refreshToken").GetString()!;

            istemci.UseBearer(accessToken);
            var cikis = await istemci.PostAsJsonAsync("/api/v2/auth/logout", new { refreshToken });
            cikis.EnsureSuccessStatusCode();

            var yenile = await app.CreateClient()
                .PostAsJsonAsync("/api/v2/auth/refresh", new { refreshToken });

            Assert.True(yenile.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest,
                $"Çıkıştan sonra refresh token hâlâ kullanılabiliyor: {(int)yenile.StatusCode}");
        }

        [Fact]
        public async Task HesabiKapatan_KullaniciErisimiKaybeder()
        {
            using var app = new TestApp();
            var kiraci = await app.NewTenant("Kapatma Co").BuildAsync();
            var uye = await Members.AddAsync(app, kiraci.Client);

            var kapat = await uye.Client.DeleteAsync("/api/v2/users/deactivate");
            kapat.EnsureSuccessStatusCode();

            var sonra = await uye.Client.GetAsync("/api/v2/users/profile");
            Assert.True(sonra.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
                $"Kapatılmış hesap hâlâ erişebiliyor: {(int)sonra.StatusCode}");
        }
    }
}
