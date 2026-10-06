using NetArchTest.Rules;
using Text2Sql.Domain.Entities;
using Text2Sql.Application.Contracts;
using Text2Sql.Infrastructure.Persistence;
using Xunit;

namespace Text2Sql.Tests.Architecture
{
    /// <summary>
    /// FAZ 1 — Mimari kuralları derleme sonrası doğrular.
    /// Bu testler kırmızıysa katman sınırı ihlal edilmiş demektir;
    /// "geçici çözüm" olarak kural gevşetmek yasaktır (ADR'ye işlenmeli).
    /// </summary>
    public class LayeringTests
    {
        private const string DomainNs         = "Text2Sql.Domain";
        private const string ApplicationNs    = "Text2Sql.Application";
        private const string InfrastructureNs = "Text2Sql.Infrastructure";
        private const string ApiNs            = "Text2Sql.Api";

        [Fact]
        public void Domain_HicbirUstKatmanaBagimliOlamaz()
        {
            var result = Types.InAssembly(typeof(Company).Assembly)
                .ShouldNot()
                .HaveDependencyOnAny(ApplicationNs, InfrastructureNs, ApiNs)
                .GetResult();

            Assert.True(result.IsSuccessful,
                "Domain şu tiplerde üst katmana bağımlı: " +
                string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>()));
        }

        [Fact]
        public void Application_InfrastructureVeApiyeBagimliOlamaz()
        {
            var result = Types.InAssembly(typeof(IAppDbContext).Assembly)
                .ShouldNot()
                .HaveDependencyOnAny(InfrastructureNs, ApiNs)
                .GetResult();

            Assert.True(result.IsSuccessful,
                "Application şu tiplerde Infrastructure/Api'ye bağımlı: " +
                string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>()));
        }

        [Fact]
        public void Infrastructure_ApiyeBagimliOlamaz()
        {
            var result = Types.InAssembly(typeof(ApplicationDbContext).Assembly)
                .ShouldNot()
                .HaveDependencyOnAny(ApiNs)
                .GetResult();

            Assert.True(result.IsSuccessful,
                "Infrastructure şu tiplerde Api'ye bağımlı: " +
                string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>()));
        }
    }
}
