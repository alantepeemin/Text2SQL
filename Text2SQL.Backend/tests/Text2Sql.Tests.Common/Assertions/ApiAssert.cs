using System.Net;
using System.Text.Json;
using Text2Sql.Tests.Common.Http;
using Xunit;

namespace Text2Sql.Tests.Common.Assertions
{
    /// <summary>
    /// API sözleşmesine özel ortak doğrulamalar.
    ///
    /// Aynı sözleşme kontrolünü her testte elle yazmak, sözleşme değiştiğinde
    /// onlarca dosyayı düzeltmek demektir. Burada toplandığında hem tek yerden
    /// güncellenir hem de hata mesajları TUTARLI ve açıklayıcı olur.
    /// </summary>
    public static class ApiAssert
    {
        /// <summary>v1 zarfını doğrular ve içindeki veriyi döndürür.</summary>
        public static async Task<JsonElement> EnvelopeAsync(HttpResponseMessage response)
        {
            response.EnsureSuccessStatusCode();
            var body = await response.ReadJsonAsync();

            Assert.True(body.TryGetProperty("success", out var success),
                "v1 yanıtında 'success' alanı yok — zarf sözleşmesi bozulmuş.");
            Assert.True(success.GetBoolean());
            Assert.True(body.TryGetProperty("message", out _),
                "v1 yanıtında 'message' alanı yok.");
            Assert.True(body.TryGetProperty("data", out var data),
                "v1 yanıtında 'data' alanı yok.");

            return data;
        }

        /// <summary>v2 çıplak gövdesini doğrular ve döndürür.</summary>
        public static async Task<JsonElement> BareAsync(HttpResponseMessage response)
        {
            response.EnsureSuccessStatusCode();
            var body = await response.ReadJsonAsync();

            if (body.ValueKind == JsonValueKind.Object)
                Assert.False(body.TryGetProperty("data", out _),
                    "v2 yanıtı sarmalanmış görünüyor — 'data' alanı olmamalı.");

            return body;
        }

        /// <summary>
        /// v2 hata sözleşmesi: RFC 7807 + makine-okunur kod + izleme kimliği.
        /// </summary>
        public static async Task ProblemAsync(
            HttpResponseMessage response, HttpStatusCode beklenenDurum, string beklenenKod)
        {
            Assert.Equal(beklenenDurum, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

            var body = await response.ReadJsonAsync();

            Assert.Equal((int)beklenenDurum, body.GetProperty("status").GetInt32());
            Assert.Equal(beklenenKod, body.GetProperty("code").GetString());
            Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("traceId").GetString()),
                "ProblemDetails traceId taşımalı — destek kaydı bununla eşleşir.");
            Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("detail").GetString()));
        }

        /// <summary>v1 hata sözleşmesi (eski istemciler bunu bekler).</summary>
        public static async Task LegacyErrorAsync(
            HttpResponseMessage response, HttpStatusCode beklenenDurum)
        {
            Assert.Equal(beklenenDurum, response.StatusCode);

            var body = await response.ReadJsonAsync();
            Assert.False(body.GetProperty("success").GetBoolean());
            Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));
        }

        /// <summary>v2 sayfalama başlıkları.</summary>
        public static void PaginationHeaders(
            HttpResponseMessage response, int beklenenSayfa, int beklenenBoyut, int? beklenenToplam = null)
        {
            Assert.True(response.Headers.Contains("X-Total-Count"),
                "v2 sayfalanmış yanıtta X-Total-Count başlığı bulunmalı.");

            Assert.Equal(beklenenSayfa.ToString(),  response.Headers.GetValues("X-Page").First());
            Assert.Equal(beklenenBoyut.ToString(), response.Headers.GetValues("X-Page-Size").First());

            if (beklenenToplam.HasValue)
                Assert.Equal(beklenenToplam.Value.ToString(),
                             response.Headers.GetValues("X-Total-Count").First());
        }

        /// <summary>v1 uçları kullanımdan kaldırılma sinyali taşımalı.</summary>
        public static void Deprecated(HttpResponseMessage response)
        {
            Assert.True(response.Headers.Contains("Deprecation"),
                "v1 yanıtı Deprecation başlığı taşımalı — istemciye geçiş sinyali verilmiyor.");
            Assert.Equal("true", response.Headers.GetValues("Deprecation").First());
        }

        /// <summary>v2 uçları kullanımdan kaldırılmış sayılmaz.</summary>
        public static void NotDeprecated(HttpResponseMessage response)
            => Assert.False(response.Headers.Contains("Deprecation"),
                "v2 yanıtı Deprecation başlığı taşımamalı.");

        /// <summary>Kiracılar arası erişimde varlık dahi sızdırılmamalı.</summary>
        public static void HiddenFromOtherTenant(HttpResponseMessage response, string aciklama)
            => Assert.True(
                response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden,
                $"{aciklama} → {(int)response.StatusCode}. Kiracılar arası erişim engellenmedi!");
    }
}
