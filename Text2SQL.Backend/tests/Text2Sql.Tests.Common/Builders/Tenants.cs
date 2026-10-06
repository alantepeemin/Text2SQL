using System.Net.Http.Json;
using Text2Sql.Tests.Common.Data;
using Text2Sql.Tests.Common.Http;

namespace Text2Sql.Tests.Common.Builders
{
    /// <summary>
    /// Kayıt akışının tek tanımı. İki aşamalı kayıt (organizasyon → ilk yönetici)
    /// değiştiğinde düzeltilecek TEK yer burasıdır.
    /// </summary>
    public static class Tenants
    {
        public const string DefaultPassword = "P@ssw0rd123";

        /// <summary>Organizasyon + ilk yöneticiyi oluşturur, access token döndürür.</summary>
        public static async Task<string> RegisterAsync(
            HttpClient client, string organizationName, string? email = null,
            string username = "adminuser", string password = DefaultPassword,
            int maxUsers = 10, int monthlyQueryLimit = 100)
        {
            var step1 = await client.PostAsJsonAsync("/api/v2/auth/create-company", new
            {
                name = organizationName,
                maxUsers,
                monthlyQueryLimit
            });
            step1.EnsureSuccessStatusCode();

            var registrationToken = (await step1.ReadJsonAsync())
                .GetProperty("registrationToken").GetString()!;

            var step2 = await client.PostAsJsonAsync("/api/v2/auth/complete-registration", new
            {
                registrationToken,
                username,
                email = email ?? SampleData.UniqueEmail(),
                password
            });
            step2.EnsureSuccessStatusCode();

            return (await step2.ReadJsonAsync()).GetProperty("accessToken").GetString()!;
        }

        /// <summary>Kayıt olur ve istemciyi doğrudan yetkilendirir.</summary>
        public static async Task<string> RegisterAndAuthenticateAsync(
            HttpClient client, string organizationName, string? email = null)
        {
            var token = await RegisterAsync(client, organizationName, email);
            client.UseBearer(token);
            return token;
        }

        public static Task<HttpResponseMessage> LoginAsync(
            HttpClient client, string email, string password = DefaultPassword)
            => client.PostAsJsonAsync("/api/v2/auth/login", new { email, password });
    }
}
