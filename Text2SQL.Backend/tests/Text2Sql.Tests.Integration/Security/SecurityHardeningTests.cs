using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Text2Sql.Domain.Entities;
using Text2Sql.Infrastructure.Persistence;
using Xunit;
using Text2Sql.Tests.Common.Assertions;
using Text2Sql.Tests.Common.Builders;
using Text2Sql.Tests.Common.Data;
using Text2Sql.Tests.Common.Fakes;
using Text2Sql.Tests.Common.Fixtures;
using Text2Sql.Tests.Common.Http;

namespace Text2Sql.Tests.Integration.Security
{
    /// <summary>SaaS-7 — Hesap kilitleme, refresh token aile iptali, e-posta doğrulama.</summary>
    public class SecurityHardeningTests : IClassFixture<TestApp>
    {
        private readonly TestApp _factory;
        public SecurityHardeningTests(TestApp factory) => _factory = factory;

        private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);
        private const string Password = "P@ssw0rd123";

        // ── Hesap kilitleme ──────────────────────────────────────────────────

        [Fact]
        public async Task ArdisikBasarisizGiris_HesabiKilitler()
        {
            var client = _factory.CreateClient();
            var email = $"lockout-{Guid.NewGuid():N}@test.local";
            await Tenants.RegisterAsync(client, "Lockout Co", email);

            // Eşik 5 — ilk 5 deneme 401 döner
            for (var i = 1; i <= 5; i++)
            {
                var resp = await client.PostAsJsonAsync("/api/auth/login",
                    new { email, password = "yanlis-parola-123" });
                Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
            }

            // 6. denemede hesap kilitli → 403 (parola doğru olsa bile)
            var kilitli = await client.PostAsJsonAsync("/api/auth/login",
                new { email, password = Password });
            Assert.Equal(HttpStatusCode.Forbidden, kilitli.StatusCode);

            var body = await kilitli.Content.ReadAsStringAsync();
            Assert.Contains("kilitlen", body, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task BasariliGiris_BasarisizSayaciniSifirlar()
        {
            var client = _factory.CreateClient();
            var email = $"reset-{Guid.NewGuid():N}@test.local";
            await Tenants.RegisterAsync(client, "Reset Co", email);

            // Eşiğin altında kal (3 hatalı deneme)
            for (var i = 0; i < 3; i++)
                await client.PostAsJsonAsync("/api/auth/login",
                    new { email, password = "yanlis" });

            // Doğru parolayla giriş başarılı olmalı
            var ok = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

            // Sayaç sıfırlandı mı? (DB'den doğrula)
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.AsNoTracking().FirstAsync(u => u.Email == email);

            Assert.Equal(0, user.FailedLoginCount);
            Assert.Null(user.LockoutEndsAt);
        }

        // ── Refresh token aile iptali ────────────────────────────────────────

        [Fact]
        public async Task RefreshToken_Rotasyon_AyniAileyeAitOlur()
        {
            var client = _factory.CreateClient();
            var email = $"family-{Guid.NewGuid():N}@test.local";
            await Tenants.RegisterAsync(client, "Family Co", email);

            var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
            var refreshToken = (await login.Content.ReadFromJsonAsync<JsonElement>(J))
                .GetProperty("data").GetProperty("refreshToken").GetString()!;

            var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken });
            Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
            var yeniToken = (await refresh.Content.ReadFromJsonAsync<JsonElement>(J))
                .GetProperty("data").GetProperty("refreshToken").GetString()!;

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var eski = await db.RefreshTokens.AsNoTracking().FirstAsync(r => r.Token == refreshToken);
            var yeni = await db.RefreshTokens.AsNoTracking().FirstAsync(r => r.Token == yeniToken);

            Assert.Equal(eski.FamilyId, yeni.FamilyId);       // aynı aile
            Assert.True(eski.IsRevoked);                       // eski iptal
            Assert.Equal(yeni.Id, eski.ReplacedByTokenId);      // izlenebilir zincir
        }

        [Fact]
        public async Task IptalEdilmisRefreshTokenYenidenKullanilirsa_TumAileIptalEdilir()
        {
            var client = _factory.CreateClient();
            var email = $"reuse-{Guid.NewGuid():N}@test.local";
            await Tenants.RegisterAsync(client, "Reuse Co", email);

            var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
            var ilkToken = (await login.Content.ReadFromJsonAsync<JsonElement>(J))
                .GetProperty("data").GetProperty("refreshToken").GetString()!;

            // Normal rotasyon: ilkToken iptal edilir, yeniToken üretilir
            var refresh1 = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = ilkToken });
            var yeniToken = (await refresh1.Content.ReadFromJsonAsync<JsonElement>(J))
                .GetProperty("data").GetProperty("refreshToken").GetString()!;

            // SALDIRI SENARYOSU: çalınan ESKİ token tekrar kullanılıyor
            var saldiri = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = ilkToken });
            Assert.Equal(HttpStatusCode.Unauthorized, saldiri.StatusCode);

            // Sonuç: TÜM AİLE iptal → geçerli olan yeniToken da artık çalışmaz
            var mesruDeneme = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = yeniToken });
            Assert.Equal(HttpStatusCode.Unauthorized, mesruDeneme.StatusCode);

            // Ve olay denetim kaydına yazılmış olmalı
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var kayit = await db.AuditLogs.IgnoreQueryFilters().AsNoTracking()
                .Where(a => a.Action == "auth.refresh_token.reuse_detected")
                .OrderByDescending(a => a.Id)
                .FirstOrDefaultAsync();

            Assert.NotNull(kayit);
            Assert.Equal(email, kayit!.ActorEmail);
        }

        // ── E-posta doğrulama ────────────────────────────────────────────────

        [Fact]
        public async Task Kayit_DogrulamaTokeniUretir_VeTokenlaDogrulanir()
        {
            var client = _factory.CreateClient();
            var email = $"confirm-{Guid.NewGuid():N}@test.local";
            await Tenants.RegisterAsync(client, "Confirm Co", email);

            string token;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var user = await db.Users.AsNoTracking().FirstAsync(u => u.Email == email);

                Assert.False(user.EmailConfirmed);
                Assert.NotNull(user.EmailConfirmationToken);
                token = user.EmailConfirmationToken!;
            }

            // Anonim istemciyle doğrulama (kullanıcı e-postadaki linke tıklar)
            var anon = _factory.CreateClient();
            var confirm = await anon.PostAsJsonAsync("/api/auth/confirm-email", new { token });
            Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var user = await db.Users.AsNoTracking().FirstAsync(u => u.Email == email);

                Assert.True(user.EmailConfirmed);
                Assert.NotNull(user.EmailConfirmedAt);
                Assert.Null(user.EmailConfirmationToken); // token tüketildi
            }
        }

        [Fact]
        public async Task GecersizDogrulamaTokeni_400Doner()
        {
            var anon = _factory.CreateClient();
            var resp = await anon.PostAsJsonAsync("/api/auth/confirm-email",
                new { token = "kesinlikle-gecersiz-token" });
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }

        [Fact]
        public async Task DogrulamaTokeni_TekKullanimliktir()
        {
            var client = _factory.CreateClient();
            var email = $"once-{Guid.NewGuid():N}@test.local";
            await Tenants.RegisterAsync(client, "Once Co", email);

            string token;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                token = (await db.Users.AsNoTracking().FirstAsync(u => u.Email == email))
                    .EmailConfirmationToken!;
            }

            var anon = _factory.CreateClient();
            Assert.Equal(HttpStatusCode.OK,
                (await anon.PostAsJsonAsync("/api/auth/confirm-email", new { token })).StatusCode);

            // İkinci kez kullanılamaz
            Assert.Equal(HttpStatusCode.BadRequest,
                (await anon.PostAsJsonAsync("/api/auth/confirm-email", new { token })).StatusCode);
        }
    }
}
