using Text2Sql.Application.Contracts;

namespace Text2Sql.Api.Auth
{
    public sealed class AmbientContext : IAmbientContext
    {
        private static readonly AsyncLocal<AmbientScope?> Storage = new();

        public AmbientScope? Current => Storage.Value;

        public IDisposable Push(int userId, int tenantId, string role)
        {
            var onceki = Storage.Value;
            Storage.Value = new AmbientScope(userId, tenantId, role);
            return new Restorer(onceki);
        }

        private sealed class Restorer : IDisposable
        {
            private readonly AmbientScope? _onceki;
            public Restorer(AmbientScope? onceki) => _onceki = onceki;
            public void Dispose() => Storage.Value = _onceki;
        }
    }
}
