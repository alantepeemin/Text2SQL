using Xunit;

namespace Text2Sql.Tests.Architecture
{
    /// <summary>
    /// Katmanlar arası sabit tutarlılığı. Birim testi değildir: iki AYRI
    /// katmanın (Application ve Api) aynı sabiti taşıdığını doğrular, yani
    /// kod tabanının yapısına dair bir kuraldır.
    /// </summary>
    public class ApiKeyConstantsTests
    {
        [Fact]
        public void OnekUzunlugu_ApiVeApplicationKatmanindaAyni()
        {
            // Bu iki sabit farklılaşırsa kimlik doğrulama sessizce hiçbir
            // anahtarı bulamaz hale gelir — bu yüzden testle bağlandılar.
            Assert.Equal(
                Text2Sql.Application.Services.ApiKeyService.DisplayPrefixLength,
                Text2Sql.Api.Auth.ApiKeyDefaults.DisplayPrefixLength);
        }
    }
}
