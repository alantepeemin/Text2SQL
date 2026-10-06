using Text2Sql.Application.Contracts;

namespace Text2Sql.Infrastructure.Providers
{
    public sealed class DataSourceProviderFactory : IDataSourceProviderFactory
    {
        private readonly IEnumerable<IDataSourceProvider> _providers;

        public DataSourceProviderFactory(IEnumerable<IDataSourceProvider> providers)
        {
            _providers = providers;
        }

        public IDataSourceProvider GetProvider(string dbType)
        {
            return _providers.FirstOrDefault(p => p.CanHandle(dbType))
                ?? throw new NotSupportedException(
                    $"'{dbType}' türü henüz sorgu için desteklenmiyor.");
        }
    }
}
