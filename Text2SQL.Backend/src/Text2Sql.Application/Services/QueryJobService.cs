using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Text2Sql.Application.Common.Exceptions;
using Text2Sql.Application.Contracts;
using Text2Sql.Application.DTOs.Jobs;
using Text2Sql.Application.DTOs.Query;
using Text2Sql.Domain.Entities;

namespace Text2Sql.Application.Services
{
    public class QueryJobService : IQueryJobService
    {
        private readonly IAppDbContext _context;
        private readonly ICurrentUserContext _currentUser;
        private readonly IQueryJobQueue _queue;
        private readonly IFileStorage _fileStorage;

        public QueryJobService(
            IAppDbContext context,
            ICurrentUserContext currentUser,
            IQueryJobQueue queue,
            IFileStorage fileStorage)
        {
            _context = context;
            _currentUser = currentUser;
            _queue = queue;
            _fileStorage = fileStorage;
        }

        public async Task<CreateQueryJobResponse> EnqueueAsync(
            int projectId, int dataSourceId, string question, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(question))
                throw new BadRequestException("Soru metni gereklidir.");

            // Veri kaynağı doğrulaması İŞ OLUŞTURULMADAN yapılır: istemci
            // hatalı isteğin sonucunu 202 alıp saniyeler sonra öğrenmemeli.
            var gecerli = await _context.ProjectDatabases
                .AnyAsync(d => d.Id == dataSourceId && d.ProjectId == projectId && d.IsActive, ct);

            if (!gecerli)
                throw new NotFoundException("Veritabanı bulunamadı veya erişiminiz yok.");

            var job = new QueryJob
            {
                CompanyId    = _currentUser.TenantId,
                ProjectId    = projectId,
                DataSourceId = dataSourceId,
                UserId       = _currentUser.UserId,
                Question     = question,
                Status       = QueryJobStatuses.Pending,
                CreatedAt    = DateTime.UtcNow
            };

            _context.QueryJobs.Add(job);
            await _context.SaveChangesAsync(ct);

            // Kuyruğa DB kaydından SONRA yaz: worker işi bulamayabilirdi.
            await _queue.EnqueueAsync(job.Id, ct);

            return new CreateQueryJobResponse
            {
                JobId     = job.Id,
                Status    = job.Status,
                StatusUrl = $"/api/v2/projects/{projectId}/queries/jobs/{job.Id}"
            };
        }

        public async Task<QueryJobDto> GetAsync(int jobId, CancellationToken ct = default)
        {
            // Global kiracı filtresi başka organizasyonun işini görünmez kılar
            var job = await _context.QueryJobs
                .AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == jobId, ct)
                ?? throw new NotFoundException("Sorgu işi bulunamadı.");

            var dto = new QueryJobDto
            {
                Id           = job.Id,
                Status       = job.Status,
                Question     = job.Question,
                CreatedAt    = job.CreatedAt,
                StartedAt    = job.StartedAt,
                CompletedAt  = job.CompletedAt,
                ErrorMessage = job.ErrorMessage
            };

            if (job.Status == QueryJobStatuses.Succeeded && job.QueryHistoryId.HasValue)
                dto.Result = await SonucuOkuAsync(job.QueryHistoryId.Value, ct);

            return dto;
        }

        private async Task<QueryResultDto?> SonucuOkuAsync(int historyId, CancellationToken ct)
        {
            var h = await _context.QueryHistories
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == historyId, ct);

            if (h == null) return null;

            var rows = new List<List<string>>();
            var columns = new List<string>();

            var json = h.Result;
            if (!string.IsNullOrWhiteSpace(h.ResultPath))
            {
                try { json = await _fileStorage.ReadTextAsync(h.ResultPath, ct) ?? h.Result; }
                catch { /* dosya okunamadıysa legacy kolonla devam */ }
            }

            if (!string.IsNullOrWhiteSpace(json))
                try { rows = JsonSerializer.Deserialize<List<List<string>>>(json!) ?? new(); } catch { }

            if (!string.IsNullOrWhiteSpace(h.Columns))
                try { columns = JsonSerializer.Deserialize<List<string>>(h.Columns!) ?? new(); } catch { }

            return new QueryResultDto
            {
                Question     = h.Question,
                Sql          = h.SqlQuery ?? string.Empty,
                Columns      = columns,
                Rows         = rows,
                Success      = h.IsSuccessful,
                ErrorMessage = h.IsSuccessful ? null : h.ErrorMessage,
                CreatedAt    = h.CreatedAt
            };
        }
    }
}
