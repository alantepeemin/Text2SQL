using Text2Sql.Application.Common.Exceptions;
using Text2Sql.Application.Queries;
using Text2Sql.Infrastructure.Providers;
using Xunit;

namespace Text2Sql.Tests.Unit.Infrastructure
{
    /// <summary>FAZ 3 — Provider factory davranışı.</summary>
    public class DataSourceProviderFactoryTests
    {
        [Fact]
        public void BilinmeyenTip_NotSupported()
        {
            var factory = new DataSourceProviderFactory(
                Array.Empty<Text2Sql.Application.Contracts.IDataSourceProvider>());

            Assert.Throws<NotSupportedException>(() => factory.GetProvider("oracle"));
        }
    }
}
