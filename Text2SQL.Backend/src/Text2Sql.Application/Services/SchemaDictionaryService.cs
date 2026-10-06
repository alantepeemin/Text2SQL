using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text;
using Text2Sql.Application.Common.Exceptions;
using Text2Sql.Application.Contracts;
using Text2Sql.Application.DTOs.Schema;
using Text2Sql.Domain.Entities;

namespace Text2Sql.Application.Services
{
    public class SchemaDictionaryService : ISchemaDictionaryService
    {
        private readonly IAppDbContext _context;
        private readonly ICurrentUserContext _currentUser;
        private readonly ILogger<SchemaDictionaryService> _logger;

        public SchemaDictionaryService(
            IAppDbContext context,
            ICurrentUserContext currentUser,
            ILogger<SchemaDictionaryService> logger)
        {
            _context = context;
            _currentUser = currentUser;
            _logger = logger;
        }

        public async Task<List<SchemaAnnotationDto>> GetAsync(int dataSourceId, CancellationToken ct = default)
            => await _context.SchemaAnnotations
                .AsNoTracking()
                .Where(a => a.DataSourceId == dataSourceId)
                .OrderBy(a => a.TableName).ThenBy(a => a.ColumnName)
                .Select(a => new SchemaAnnotationDto
                {
                    Id          = a.Id,
                    TableName   = a.TableName,
                    ColumnName  = a.ColumnName,
                    Description = a.Description,
                    UpdatedAt   = a.UpdatedAt
                })
                .ToListAsync(ct);

        public async Task<SchemaAnnotationDto> UpsertAsync(
            int dataSourceId, UpsertAnnotationRequest request, CancellationToken ct = default)
        {
            // Veri kaynağı bu kiracıya ait mi? (global filtre zaten uygular,
            // explicit kontrol derinlemesine savunma)
            var varMi = await _context.ProjectDatabases
                .AnyAsync(d => d.Id == dataSourceId, ct);
            if (!varMi)
                throw new NotFoundException("Veri kaynağı bulunamadı.");

            var kolon = string.IsNullOrWhiteSpace(request.ColumnName) ? null : request.ColumnName.Trim();

            var mevcut = await _context.SchemaAnnotations
                .FirstOrDefaultAsync(a => a.DataSourceId == dataSourceId
                                          && a.TableName == request.TableName
                                          && a.ColumnName == kolon, ct);

            if (mevcut == null)
            {
                mevcut = new SchemaAnnotation
                {
                    CompanyId    = _currentUser.TenantId,
                    DataSourceId = dataSourceId,
                    TableName    = request.TableName.Trim(),
                    ColumnName   = kolon,
                    Description  = request.Description.Trim(),
                    CreatedAt    = DateTime.UtcNow,
                    UpdatedAt    = DateTime.UtcNow
                };
                _context.SchemaAnnotations.Add(mevcut);
            }
            else
            {
                mevcut.Description = request.Description.Trim();
                mevcut.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync(ct);

            return new SchemaAnnotationDto
            {
                Id          = mevcut.Id,
                TableName   = mevcut.TableName,
                ColumnName  = mevcut.ColumnName,
                Description = mevcut.Description,
                UpdatedAt   = mevcut.UpdatedAt
            };
        }

        public async Task DeleteAsync(int annotationId, CancellationToken ct = default)
        {
            var kayit = await _context.SchemaAnnotations
                .FirstOrDefaultAsync(a => a.Id == annotationId, ct)
                ?? throw new NotFoundException("Açıklama bulunamadı.");

            _context.SchemaAnnotations.Remove(kayit);
            await _context.SaveChangesAsync(ct);
        }

        public async Task<string?> BuildGlossaryAsync(
            int dataSourceId, string schemaText, CancellationToken ct = default)
        {
            var aciklamalar = await _context.SchemaAnnotations
                .AsNoTracking()
                .Where(a => a.DataSourceId == dataSourceId)
                .ToListAsync(ct);

            if (aciklamalar.Count == 0) return null;

            // TOKEN TASARRUFU: Yalnızca prompt'a giren şemada GEÇEN tabloların
            // açıklamalarını ekle. Şema daraltıldığında (SchemaOptimizer) elenen
            // tabloların açıklamaları da otomatik elenir.
            var ilgili = aciklamalar
                .Where(a => schemaText.Contains(a.TableName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (ilgili.Count == 0) return null;

            var sb = new StringBuilder();
            sb.AppendLine("BUSINESS GLOSSARY (customer-provided meanings — prefer these interpretations):");

            foreach (var grup in ilgili.GroupBy(a => a.TableName, StringComparer.OrdinalIgnoreCase)
                                       .OrderBy(g => g.Key))
            {
                var tabloAciklamasi = grup.FirstOrDefault(a => a.IsTableLevel);
                if (tabloAciklamasi != null)
                    sb.AppendLine($"- {grup.Key}: {tabloAciklamasi.Description}");
                else
                    sb.AppendLine($"- {grup.Key}:");

                foreach (var kolon in grup.Where(a => !a.IsTableLevel).OrderBy(a => a.ColumnName))
                    sb.AppendLine($"    · {kolon.ColumnName}: {kolon.Description}");
            }

            return sb.ToString();
        }
    }

    public class QueryFeedbackService : IQueryFeedbackService
    {
        private readonly IAppDbContext _context;
        private readonly ICurrentUserContext _currentUser;

        public QueryFeedbackService(IAppDbContext context, ICurrentUserContext currentUser)
        {
            _context = context;
            _currentUser = currentUser;
        }

        public async Task SubmitAsync(
            int queryHistoryId, SubmitFeedbackRequest request, CancellationToken ct = default)
        {
            var sorgu = await _context.QueryHistories
                .FirstOrDefaultAsync(h => h.Id == queryHistoryId, ct)
                ?? throw new NotFoundException("Sorgu kaydı bulunamadı.");

            // Aynı sorguya tek geri bildirim (güncellenebilir)
            var mevcut = await _context.QueryFeedbacks
                .FirstOrDefaultAsync(f => f.QueryHistoryId == queryHistoryId
                                          && f.UserId == _currentUser.UserId, ct);

            if (mevcut == null)
            {
                _context.QueryFeedbacks.Add(new QueryFeedback
                {
                    CompanyId      = _currentUser.TenantId,
                    QueryHistoryId = queryHistoryId,
                    UserId         = _currentUser.UserId,
                    IsHelpful      = request.IsHelpful,
                    CorrectedSql   = request.CorrectedSql,
                    Comment        = request.Comment,
                    CreatedAt      = DateTime.UtcNow
                });
            }
            else
            {
                mevcut.IsHelpful    = request.IsHelpful;
                mevcut.CorrectedSql = request.CorrectedSql;
                mevcut.Comment      = request.Comment;
            }

            await _context.SaveChangesAsync(ct);
        }

        public async Task<FeedbackSummaryDto> GetSummaryAsync(CancellationToken ct = default)
        {
            var kayitlar = await _context.QueryFeedbacks
                .AsNoTracking()
                .Select(f => new { f.IsHelpful, f.CorrectedSql })
                .ToListAsync(ct);

            var helpful = kayitlar.Count(k => k.IsHelpful);

            return new FeedbackSummaryDto
            {
                TotalFeedback      = kayitlar.Count,
                HelpfulCount       = helpful,
                NotHelpfulCount    = kayitlar.Count - helpful,
                AccuracyRate       = kayitlar.Count == 0 ? 0 : Math.Round(100.0 * helpful / kayitlar.Count, 1),
                CorrectedSqlCount  = kayitlar.Count(k => !string.IsNullOrWhiteSpace(k.CorrectedSql))
            };
        }
    }
}
