using Text2Sql.Domain.Entities;
using Xunit;

namespace Text2Sql.Tests.Unit.Domain
{
    /// <summary>
    /// QueryJob durum geçişleri. Durum makinesi entity'nin İÇİNDE yaşar
    /// (dışarıdan Status ataması yerine davranış metotları) — bu testler o
    /// kapsüllemenin bozulmadığını doğrular.
    /// </summary>
    public class QueryJobBehaviourTests
    {
        private static QueryJob Yeni() => new()
        {
            CompanyId = 1, ProjectId = 1, DataSourceId = 1, UserId = 1,
            Question = "test"
        };

        [Fact]
        public void YeniIs_BeklemeDurumundaBaslar_ve_SonlanmisSayilmaz()
        {
            var job = Yeni();

            Assert.Equal(QueryJobStatuses.Pending, job.Status);
            Assert.False(job.IsTerminal);
            Assert.Null(job.StartedAt);
            Assert.Null(job.CompletedAt);
        }

        [Fact]
        public void MarkRunning_BaslangicZamaniniYazar_AmaSonlandirmaz()
        {
            var job = Yeni();
            job.MarkRunning();

            Assert.Equal(QueryJobStatuses.Running, job.Status);
            Assert.NotNull(job.StartedAt);
            Assert.False(job.IsTerminal);
        }

        [Fact]
        public void MarkSucceeded_SonucReferansiniBaglar_ve_Sonlandirir()
        {
            var job = Yeni();
            job.MarkRunning();
            job.MarkSucceeded(queryHistoryId: 42);

            Assert.Equal(QueryJobStatuses.Succeeded, job.Status);
            Assert.Equal(42, job.QueryHistoryId);
            Assert.NotNull(job.CompletedAt);
            Assert.True(job.IsTerminal);
        }

        [Fact]
        public void MarkFailed_UzunHataMesajiniKirpar_KolonTasmasiOlmaz()
        {
            var job = Yeni();
            job.MarkFailed(new string('x', 5000));

            Assert.Equal(QueryJobStatuses.Failed, job.Status);
            Assert.Equal(1000, job.ErrorMessage!.Length);
            Assert.True(job.IsTerminal);
        }
    }
}
