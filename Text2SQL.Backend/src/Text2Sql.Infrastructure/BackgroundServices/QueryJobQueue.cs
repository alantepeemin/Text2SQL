using System.Threading.Channels;
using Text2Sql.Application.Contracts;

namespace Text2Sql.Infrastructure.BackgroundServices
{
    public sealed class InMemoryQueryJobQueue : IQueryJobQueue
    {
        // Sınırlı kapasite: sınırsız kuyruk, ani yükte belleği tüketir ve
        // gerçek sorunu (kapasite yetersizliği) gizler.
        private readonly Channel<int> _channel = Channel.CreateBounded<int>(
            new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.Wait });

        public ValueTask EnqueueAsync(int jobId, CancellationToken ct = default)
            => _channel.Writer.WriteAsync(jobId, ct);

        public IAsyncEnumerable<int> DequeueAllAsync(CancellationToken ct)
            => _channel.Reader.ReadAllAsync(ct);
    }
}
