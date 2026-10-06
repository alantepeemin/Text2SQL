using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Text2Sql.Tests.Common.Http
{
    /// <summary>
    /// HttpClient üzerinde tekrarlanan kalıpları tek yerde toplar.
    /// Testlerin kendi JSON okuma/serileştirme kodunu yazması, aynı hatanın
    /// on farklı yerde tekrarlanması demektir.
    /// </summary>
    public static class TestClientExtensions
    {
        public static HttpClient UseBearer(this HttpClient client, string token)
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
            return client;
        }

        public static HttpClient UseApiKey(this HttpClient client, string apiKey)
        {
            client.DefaultRequestHeaders.Remove("X-Api-Key");
            client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
            return client;
        }

        public static HttpClient ClearAuth(this HttpClient client)
        {
            client.DefaultRequestHeaders.Authorization = null;
            client.DefaultRequestHeaders.Remove("X-Api-Key");
            return client;
        }

        /// <summary>Yanıt gövdesini JsonElement olarak okur.</summary>
        public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response)
            => await response.Content.ReadFromJsonAsync<JsonElement>(TestJson.Options);

        /// <summary>Başlıklı POST — idempotency gibi başlık gerektiren senaryolar için.</summary>
        public static Task<HttpResponseMessage> PostJsonAsync(
            this HttpClient client, string url, object body,
            IDictionary<string, string>? headers = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(body)
            };

            if (headers != null)
                foreach (var (ad, deger) in headers)
                    request.Headers.Add(ad, deger);

            return client.SendAsync(request);
        }
    }
}
