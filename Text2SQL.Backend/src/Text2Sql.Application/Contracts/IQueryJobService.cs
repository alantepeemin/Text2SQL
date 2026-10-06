using Text2Sql.Application.DTOs.Jobs;

namespace Text2Sql.Application.Contracts
{
    public interface IQueryJobService
    {
        Task<CreateQueryJobResponse> EnqueueAsync(int projectId, int dataSourceId, string question, CancellationToken ct = default);
        Task<QueryJobDto> GetAsync(int jobId, CancellationToken ct = default);
    }

    /// <summary>
    /// İş kuyruğu portu.
    ///
    /// Tek instance kararı gereği bellek içi Channel kullanılır. Ölçeklenme
    /// ihtiyacı doğduğunda bu portun arkasına Redis/RabbitMQ adaptörü konur;
    /// çağıran kod değişmez. İş DURUMU zaten DB'de olduğu için kuyruğun
    /// kaybolması veri kaybı değil, yalnızca yeniden kuyruklama gerektirir.
    /// </summary>
    public interface IQueryJobQueue
    {
        ValueTask EnqueueAsync(int jobId, CancellationToken ct = default);
        IAsyncEnumerable<int> DequeueAllAsync(CancellationToken ct);
    }
}
