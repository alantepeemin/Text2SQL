using System.Reflection;
using Text2Sql.Application.Common.Exceptions;
using Xunit;

namespace Text2Sql.Tests.Unit.Application
{
    /// <summary>
    /// Hata kodu sözleşmesi.
    ///
    /// İstemci hata METNİNE değil KODUNA dallanır. Bir kodun sessizce değişmesi
    /// veya yeni bir istisnanın kodsuz eklenmesi, istemci tarafında fark
    /// edilmeyen bir kırılmadır — bu testler o sözleşmeyi mühürler.
    /// </summary>
    public class ErrorCodeMappingTests
    {
        [Theory]
        [InlineData(typeof(BadRequestException),          400, "BAD_REQUEST")]
        [InlineData(typeof(UnauthorizedException),        401, "UNAUTHORIZED")]
        [InlineData(typeof(FeatureNotAvailableException), 402, "FEATURE_NOT_AVAILABLE")]
        [InlineData(typeof(PlanLimitExceededException),   402, "PLAN_LIMIT_EXCEEDED")]
        [InlineData(typeof(ForbiddenException),           403, "FORBIDDEN")]
        [InlineData(typeof(NotFoundException),            404, "NOT_FOUND")]
        [InlineData(typeof(ConflictException),            409, "CONFLICT")]
        [InlineData(typeof(QuotaExceededException),       429, "QUOTA_EXCEEDED")]
        public void Istisna_BeklenenDurumKoduVeHataKodunuTasir(Type tip, int durum, string kod)
        {
            var istisna = (AppException)Activator.CreateInstance(tip, "test mesajı")!;

            Assert.Equal(durum, istisna.StatusCode);
            Assert.Equal(kod, istisna.Code);
        }

        [Fact]
        public void TumAppExceptionTurevleri_KodTanimlar_ve_KodlarBenzersizdir()
        {
            var turevler = typeof(AppException).Assembly.GetTypes()
                .Where(t => t.IsSubclassOf(typeof(AppException)) && !t.IsAbstract)
                .ToList();

            Assert.NotEmpty(turevler);

            var kodlar = new List<string>();
            foreach (var tip in turevler)
            {
                var ornek = (AppException)Activator.CreateInstance(tip, "x")!;
                Assert.False(string.IsNullOrWhiteSpace(ornek.Code),
                    $"{tip.Name} makine-okunur bir kod tanımlamıyor.");
                kodlar.Add(ornek.Code);
            }

            Assert.Equal(kodlar.Count, kodlar.Distinct().Count());
        }
    }
}
