using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Text2Sql.Api.Http;
using Text2Sql.Api.Attributes;
using Text2Sql.Api.Auth;
using Text2Sql.Domain.Authorization;
using Text2Sql.Application.Common;
using Text2Sql.Application.Contracts;
using Text2Sql.Domain.Enums;
using Text2Sql.Application.DTOs.Database;
using Text2Sql.Application.DTOs.Schema;

namespace Text2Sql.Api.Controllers
{
    [ApiController]
    [Route("api/projects/{projectId:int}/databases")]
    [Route("api/v2/projects/{projectId:int}/databases")]
    [Route("api/v1/projects/{projectId:int}/databases")] // Faz 2: v1 alias — mevcut route korunur
    [Authorize]
    public class ProjectDatabaseController : ControllerBase
    {
        private readonly IProjectDatabaseService _service;
        private readonly ISchemaDictionaryService _dictionary;

        public ProjectDatabaseController(
            IProjectDatabaseService service, ISchemaDictionaryService dictionary)
        {
            _service = service;
            _dictionary = dictionary;
        }

        // ── SaaS-9: Şema sözlüğü (semantic layer) ────────────────────────────

        /// <summary>Veri kaynağının iş sözlüğü — tablo/kolon açıklamaları.</summary>
        [HttpGet("{dbId:int}/dictionary")]
        [RequirePermission(Permissions.DataSourcesRead)]
        [RequireProjectPermission(ProjectPermission.Viewer)]
        public async Task<IActionResult> GetDictionary(int dbId, CancellationToken ct)
        {
            var items = await _dictionary.GetAsync(dbId, ct);
            return this.ApiOk(items);
        }

        /// <summary>
        /// Açıklama ekle/güncelle. Doğruluğu en çok artıran yatırım:
        /// LLM `rev_amt` kolonunun "net ciro" olduğunu ancak buradan öğrenir.
        /// </summary>
        [HttpPut("{dbId:int}/dictionary")]
        [RequirePermission(Permissions.SchemaDictionaryManage)]
        [RequireProjectPermission(ProjectPermission.Editor)]
        public async Task<IActionResult> UpsertDictionary(
            int dbId, [FromBody] UpsertAnnotationRequest request, CancellationToken ct)
        {
            var item = await _dictionary.UpsertAsync(dbId, request, ct);
            return this.ApiOk(item, "Açıklama kaydedildi.");
        }

        [HttpDelete("{dbId:int}/dictionary/{annotationId:int}")]
        [RequirePermission(Permissions.SchemaDictionaryManage)]
        [RequireProjectPermission(ProjectPermission.Editor)]
        public async Task<IActionResult> DeleteDictionaryEntry(
            int dbId, int annotationId, CancellationToken ct)
        {
            await _dictionary.DeleteAsync(annotationId, ct);
            return this.ApiOk(null!, "Açıklama silindi.");
        }

        [HttpPost]
        [RequirePermission(Permissions.DataSourcesManage)]
        [RequireProjectPermission(ProjectPermission.Editor)]
        public async Task<IActionResult> AddDatabase(
            int projectId,
            [FromForm] CreateDatabaseForm form,
            CancellationToken ct = default)
        {
            if (!ModelState.IsValid)
                return BadRequest("Geçersiz form verileri.");

            int dbId = form.Mode == DatabaseCreateMode.LocalFile
                ? await _service.AddLocalSqliteAsync(projectId, form, ct)
                : await _service.AddRemoteAsync(projectId, form, ct);

            var response = new
            {
                Id        = dbId,
                Name      = form.Name,
                DbType    = form.DbType.ToString(),
                CreatedAt = DateTime.UtcNow
            };

            return this.ApiOk(response, "Veritabanı başarıyla eklendi.");
        }

        [HttpGet]
        [RequireProjectPermission(ProjectPermission.Viewer)]
        public async Task<IActionResult> GetDatabases(int projectId)
        {
            var list = await _service.GetProjectDatabasesAsync(projectId);
            return this.ApiOk(list);
        }

        [HttpDelete("{dbId:int}")]
        [RequirePermission(Permissions.DataSourcesManage)]
        [RequireProjectPermission(ProjectPermission.Editor)]
        public async Task<IActionResult> DeleteDatabase(int projectId, int dbId)
        {
            // ✅ projectId artık servise de gönderiliyor (çapraz proje koruması)
            var success = await _service.DeleteDatabaseAsync(projectId, dbId);
            return this.ApiOk(success, "Veritabanı pasifleştirildi.");
        }
    }
}
