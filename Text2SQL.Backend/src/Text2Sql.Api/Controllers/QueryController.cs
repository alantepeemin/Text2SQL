using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Text2Sql.Api.Http;
using Microsoft.AspNetCore.RateLimiting;
using Text2Sql.Api.Attributes;
using Text2Sql.Api.Auth;
using Text2Sql.Domain.Authorization;
using Text2Sql.Application.Common;
using Text2Sql.Application.Contracts;
using Text2Sql.Domain.Enums;
using Text2Sql.Application.DTOs.Database;
using Text2Sql.Api.Filters;
using Text2Sql.Application.DTOs.Jobs;
using Text2Sql.Application.DTOs.Query;
using Text2Sql.Application.DTOs.Schema;

namespace Text2Sql.Api.Controllers
{
    [ApiController]
    [Route("api/projects/{projectId:int}/queries")]
    [Route("api/v2/projects/{projectId:int}/queries")]
    [Route("api/v1/projects/{projectId:int}/queries")] // Faz 2: v1 alias — mevcut route korunur
    [Authorize]
    [EnableRateLimiting("query")] // Faz 2: kullanıcı başına LLM yük freni
    public class QueryController : ControllerBase
    {
        private readonly IQueryService _queryService;
        private readonly IQueryFeedbackService _feedback;
        private readonly IQueryJobService _jobs;

        public QueryController(
            IQueryService queryService,
            IQueryFeedbackService feedback,
            IQueryJobService jobs)
        {
            _queryService = queryService;
            _feedback = feedback;
            _jobs = jobs;
        }

        // ── Asenkron sorgu (202 + durum sorgulama) ───────────────────────────

        /// <summary>
        /// Sorguyu ARKA PLANDA çalıştırır; hemen 202 + iş kimliği döner.
        ///
        /// Neden: LLM + büyük sorgu 60 sn'yi bulabiliyor; senkron HTTP'de bu
        /// proxy/tarayıcı timeout'u ve kötü "yükleniyor" deneyimi demek.
        /// Senkron uç (execute) kısa sorgular için korunuyor.
        /// </summary>
        [HttpPost("execute-async")]
        [RequirePermission(Permissions.QueriesExecute)]
        [RequireProjectPermission(ProjectPermission.Editor)]
        [Idempotent]
        public async Task<IActionResult> ExecuteAsync(
            int projectId, [FromBody] ExecuteQueryRequest request, CancellationToken ct)
        {
            if (request is null) return BadRequest("Geçersiz istek.");
            if (request.DatabaseId <= 0) return BadRequest("Veritabanı seçimi gereklidir.");
            if (string.IsNullOrWhiteSpace(request.Question)) return BadRequest("Soru metni gereklidir.");

            var job = await _jobs.EnqueueAsync(projectId, request.DatabaseId, request.Question, ct);

            // 202 Accepted + Location: istemci durumu buradan çeker
            Response.Headers.Location = job.StatusUrl;
            return Accepted(job.StatusUrl, job);
        }

        /// <summary>Asenkron sorgunun durumu; tamamlandıysa sonucu da içerir.</summary>
        [HttpGet("jobs/{jobId:int}")]
        [RequirePermission(Permissions.QueriesRead)]
        [RequireProjectPermission(ProjectPermission.Viewer)]
        public async Task<IActionResult> GetJob(int projectId, int jobId, CancellationToken ct)
        {
            var job = await _jobs.GetAsync(jobId, ct);
            return this.ApiOk(job);
        }

        // ── SaaS-9: Sorgu geri bildirimi ─────────────────────────────────────

        /// <summary>
        /// 👍/👎 + (opsiyonel) düzeltilmiş SQL. Doğruluğu ÖLÇMENİN tek yolu;
        /// düzeltilmiş SQL'ler ileride few-shot örnek havuzu olur.
        /// </summary>
        [HttpPost("{queryHistoryId:int}/feedback")]
        [RequirePermission(Permissions.QueriesRead)]
        [RequireProjectPermission(ProjectPermission.Viewer)]
        public async Task<IActionResult> SubmitFeedback(
            int projectId, int queryHistoryId,
            [FromBody] SubmitFeedbackRequest request, CancellationToken ct)
        {
            await _feedback.SubmitAsync(queryHistoryId, request, ct);
            return this.ApiOk(null!, "Geri bildiriminiz kaydedildi.");
        }

        /// <summary>Organizasyonun ölçülen doğruluk oranı.</summary>
        [HttpGet("feedback-summary")]
        [RequirePermission(Permissions.QueriesRead)]
        [RequireProjectPermission(ProjectPermission.Viewer)]
        public async Task<IActionResult> GetFeedbackSummary(int projectId, CancellationToken ct)
        {
            var summary = await _feedback.GetSummaryAsync(ct);
            return this.ApiOk(summary);
        }

        [HttpPost("execute")]
        [Idempotent]   // Çift tıklama iki LLM çağrısı + iki kota tüketimi yapmasın
        // SaaS-4: İki katmanlı yetki — organizasyon izni + proje düzeyi izin.
        // Biri organizasyonda "sorgu çalıştırabilir mi", diğeri "BU projede
        // yetkisi var mı" sorusunu yanıtlar; ikisi de gereklidir.
        [RequirePermission(Permissions.QueriesExecute)]
        [RequireProjectPermission(ProjectPermission.Editor)]
        public async Task<IActionResult> Execute(int projectId, [FromBody] ExecuteQueryRequest request, CancellationToken ct)
        {
            if (request is null)
                return BadRequest("Geçersiz istek.");

            if (request.DatabaseId <= 0)
                return BadRequest("Veritabanı seçimi gereklidir.");

            if (string.IsNullOrWhiteSpace(request.Question))
                return BadRequest("Soru metni gereklidir.");

            var result = await _queryService.ExecuteQueryAsync(projectId, request.DatabaseId, request.Question, ct);
            return Ok(result);
        }

        [HttpGet("history")]
        [RequirePermission(Permissions.QueriesRead)]
        [RequireProjectPermission(ProjectPermission.Viewer)]
        public async Task<IActionResult> History(int projectId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        {
            var result = await _queryService.GetHistoryAsync(projectId, page, pageSize);
            return Ok(result);
        }

        [HttpGet("available-databases")]
        [RequireProjectPermission(ProjectPermission.Viewer)]
        public async Task<IActionResult> GetAvailableDatabases(int projectId)
        {
            var databases = await _queryService.GetAvailableDatabasesAsync(projectId);
            return this.ApiOk(databases);
        }
    }
}
