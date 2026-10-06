using Text2Sql.Application.Common;
using Xunit;

namespace Text2Sql.Tests.Unit.Application
{
    /// <summary>
    /// Sayfalama hesapları. Sayfa sayısı hatası, kullanıcı arayüzünde
    /// "sonraki sayfa" düğmesinin boş sayfaya gitmesi demektir.
    /// </summary>
    public class PagedResultTests
    {
        [Theory]
        [InlineData(0,  10, 0)]
        [InlineData(1,  10, 1)]
        [InlineData(10, 10, 1)]
        [InlineData(11, 10, 2)]
        [InlineData(99, 10, 10)]
        public void ToplamSayfa_YukariYuvarlanir(int toplam, int sayfaBoyutu, int beklenen)
        {
            var sonuc = PagedResult<int>.Create(new List<int>(), page: 1, pageSize: sayfaBoyutu, total: toplam);

            Assert.Equal(beklenen, sonuc.TotalPages);
        }

        [Fact]
        public void SifirSayfaBoyutu_SifiraBolmeHatasiVermez()
        {
            var sonuc = PagedResult<int>.Create(new List<int>(), page: 1, pageSize: 0, total: 5);

            Assert.Equal(0, sonuc.TotalPages);
        }

        [Fact]
        public void Create_AlanlariAynenTasir()
        {
            var ogeler = new List<int> { 1, 2, 3 };

            var sonuc = PagedResult<int>.Create(ogeler, page: 2, pageSize: 3, total: 7);

            Assert.Equal(ogeler, sonuc.Items);
            Assert.Equal(2, sonuc.Page);
            Assert.Equal(3, sonuc.PageSize);
            Assert.Equal(7, sonuc.TotalCount);
        }
    }
}
