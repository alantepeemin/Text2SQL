using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Text2Sql.Application.Contracts;
using Text2Sql.Domain.Entities;
using Text2Sql.Domain.Enums;
using Text2Sql.Application.DTOs.Database;
using Text2Sql.Infrastructure.Persistence;

namespace Text2Sql.Infrastructure.Services
{
    public class ProjectDatabaseService : IProjectDatabaseService
    {
        private readonly ApplicationDbContext _context;
        private readonly ICurrentUserContext  _currentUser;
        private readonly IConnectionTester    _connectionTester;
        private readonly IDataProtector       _protector;
        private readonly IConfiguration       _configuration;
        private readonly IAuditWriter         _audit;
        private readonly IFeatureGate         _featureGate;
        private readonly IPlanLimitService    _planLimits;
        private readonly ILogger<ProjectDatabaseService> _logger;

        private const long MaxFileSizeBytes = 200 * 1024 * 1024; // 200 MB

        public ProjectDatabaseService(
            ApplicationDbContext context,
            ICurrentUserContext currentUser,
            IConnectionTester connectionTester,
            IDataProtectionProvider protectionProvider,
            IConfiguration configuration,
            IAuditWriter audit,
            IFeatureGate featureGate,
            IPlanLimitService planLimits,
            ILogger<ProjectDatabaseService> logger)
        {
            _context          = context;
            _currentUser      = currentUser;
            _connectionTester = connectionTester;
            _protector        = protectionProvider.CreateProtector("DatabaseConnections");
            _configuration    = configuration;
            _audit            = audit;
            _featureGate      = featureGate;
            _planLimits       = planLimits;
            _logger           = logger;
        }

        public async Task<int> AddLocalSqliteAsync(int projectId, CreateDatabaseForm form, CancellationToken ct = default)
        {
            if (form.DbType != DatabaseType.Sqlite)
                throw new ArgumentException("LocalFile modu yalnızca SQLite için desteklenir.");

            if (form.DatabaseFile == null || form.DatabaseFile.Length == 0)
                throw new ArgumentException("SQLite dosyası gereklidir.");

            if (form.DatabaseFile.Length > MaxFileSizeBytes)
                throw new ArgumentException($"Dosya boyutu {MaxFileSizeBytes / 1024 / 1024} MB'ı geçemez.");

            var ext = Path.GetExtension(form.DatabaseFile.FileName).ToLowerInvariant();
            if (ext != ".sqlite" && ext != ".db")
                throw new ArgumentException("Yalnızca .sqlite ve .db uzantılı dosyalar kabul edilir.");

            // SaaS-5: SQLite yükleme HER PLANDA mevcut; yalnızca sayı limiti uygulanır.
            await _planLimits.EnsureCanAddDataSourceAsync(ct);

            // GÜVENLİK (Faz 0): Uzantı taklit edilebilir — dosya içeriği doğrulanır.
            // Geçerli SQLite dosyaları "SQLite format 3\0" (16 byte) ile başlar.
            await ValidateSqliteMagicBytesAsync(form.DatabaseFile, ct);

            // Proje bu tenant'a ait mi ve kullanıcının yetkisi var mı?
            await EnsureProjectAccessAsync(projectId, ProjectPermission.Editor);

            var sqliteRoot = _configuration.GetValue<string>("Storage:SqliteRoot") ?? "Data/SQLite";
            var tenantDir  = Path.Combine(sqliteRoot, _currentUser.TenantId.ToString(), projectId.ToString());
            Directory.CreateDirectory(tenantDir);

            var fileName = $"{Guid.NewGuid()}{ext}";
            var fullPath = Path.Combine(tenantDir, fileName);

            try
            {
                await using var stream = new FileStream(fullPath, FileMode.Create);
                await form.DatabaseFile.CopyToAsync(stream, ct);

                var db = new ProjectDatabase
                {
                    ProjectId      = projectId,
                    CompanyId      = _currentUser.TenantId,
                    DbType         = "sqlite",
                    ConnectionName = form.Name,
                    SqliteFilePath = Path.Combine(
                        _currentUser.TenantId.ToString(), projectId.ToString(), fileName),
                    IsActive       = true,
                    CreatedAt      = DateTime.UtcNow,
                    UpdatedAt      = DateTime.UtcNow
                };

                _context.ProjectDatabases.Add(db);
                await _context.SaveChangesAsync(ct);

                _audit.Write(new AuditEvent(AuditActions.DataSourceCreated,
                    TargetType: "DataSource", TargetId: db.Id.ToString(),
                    Summary: $"SQLite veri kaynağı '{form.Name}' eklendi (proje {projectId})"));
                await _context.SaveChangesAsync(ct);

                _logger.LogInformation(
                    "SQLite DB eklendi: ProjectId={PId}, DbId={DbId}, Tenant={TId}",
                    projectId, db.Id, _currentUser.TenantId);

                return db.Id;
            }
            catch
            {
                if (File.Exists(fullPath))
                    try { File.Delete(fullPath); } catch { /* best effort */ }
                throw;
            }
        }

        public async Task<int> AddRemoteAsync(int projectId, CreateDatabaseForm form, CancellationToken ct = default)
        {
            if (form.DbType == DatabaseType.Sqlite)
                throw new ArgumentException("Remote modu SQLite için desteklenmez. LocalFile modunu kullanın.");

            // SaaS-5: Uzak veri kaynağı bağlama ücretli paketlerde.
            // Kontrol controller attribute'u yerine BURADA çünkü tek endpoint
            // hem LocalFile hem Remote modunu karşılıyor — koşul moda bağlı.
            await _featureGate.EnsureEnabledAsync(Text2Sql.Domain.Billing.Features.RemoteDataSources, ct);
            await _planLimits.EnsureCanAddDataSourceAsync(ct);

            string connectionString;

            if (!string.IsNullOrWhiteSpace(form.ConnectionString))
            {
                connectionString = form.ConnectionString;
            }
            else if (!string.IsNullOrWhiteSpace(form.Host) &&
                     !string.IsNullOrWhiteSpace(form.Database) &&
                     !string.IsNullOrWhiteSpace(form.Username) &&
                     !string.IsNullOrWhiteSpace(form.Password))
            {
                connectionString = BuildConnectionString(form.DbType, form.Host, form.Port,
                    form.Database, form.Username, form.Password);
            }
            else
            {
                throw new ArgumentException("ConnectionString veya Host/Database/Username/Password bilgileri gereklidir.");
            }

            await EnsureProjectAccessAsync(projectId, ProjectPermission.Editor);

            if (!await _connectionTester.TestConnectionAsync(form.DbType, connectionString, ct))
                throw new ArgumentException("Veritabanına bağlanılamadı. Bağlantı bilgilerini kontrol edin.");

            var db = new ProjectDatabase
            {
                ProjectId          = projectId,
                CompanyId          = _currentUser.TenantId,
                DbType             = form.DbType.ToString().ToLowerInvariant(),
                ConnectionName     = form.Name,
                Host               = form.Host,
                Port               = form.Port,
                DatabaseName       = form.Database,
                SchemaName         = form.SchemaName,
                PasswordEncrypted  = _protector.Protect(connectionString),
                IsActive           = true,
                CreatedAt          = DateTime.UtcNow,
                UpdatedAt          = DateTime.UtcNow
            };

            _context.ProjectDatabases.Add(db);
            await _context.SaveChangesAsync(ct);

            _audit.Write(new AuditEvent(AuditActions.DataSourceCreated,
                TargetType: "DataSource", TargetId: db.Id.ToString(),
                // Bağlantı dizesi/şifre ASLA denetim kaydına yazılmaz
                Summary: $"{form.DbType} veri kaynağı '{form.Name}' eklendi (proje {projectId})"));
            await _context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Remote DB eklendi: ProjectId={PId}, DbId={DbId}, DbType={T}, Tenant={TId}",
                projectId, db.Id, form.DbType, _currentUser.TenantId);

            return db.Id;
        }

        public async Task<List<ProjectDatabaseDto>> GetProjectDatabasesAsync(int projectId)
        {
            // Hem proje tenant'a ait hem de kullanıcının erişimi var mı?
            await EnsureProjectAccessAsync(projectId, ProjectPermission.Viewer);

            return await _context.ProjectDatabases
                .Where(db => db.ProjectId == projectId
                          && db.CompanyId == _currentUser.TenantId // explicit tenant
                          && db.IsActive)
                .Select(db => new ProjectDatabaseDto
                {
                    Id             = db.Id,
                    DbType         = db.DbType,
                    ConnectionName = db.ConnectionName,
                    Host           = db.Host,
                    Port           = db.Port,
                    DatabaseName   = db.DatabaseName,
                    SchemaName     = db.SchemaName
                })
                .ToListAsync();
        }

        public async Task<bool> DeleteDatabaseAsync(int projectId, int dbId)
        {
            // ✅ Çapraz proje güvenlik kontrolü: hem projectId hem tenant filtresi
            var db = await _context.ProjectDatabases
                .FirstOrDefaultAsync(d =>
                    d.Id        == dbId         &&
                    d.ProjectId == projectId     &&   // ← çapraz proje koruması
                    d.CompanyId == _currentUser.TenantId); // ← tenant koruması

            if (db == null)
                throw new KeyNotFoundException("Veritabanı bulunamadı veya bu projeye ait değil.");

            // Soft delete
            db.IsActive   = false;
            db.UpdatedAt  = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _audit.Write(new AuditEvent(AuditActions.DataSourceDeleted,
                TargetType: "DataSource", TargetId: dbId.ToString(),
                Summary: $"Veri kaynağı '{db.ConnectionName}' pasifleştirildi (proje {projectId})"));
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "DB pasifleştirildi: DbId={DbId}, ProjectId={PId}, Tenant={TId}",
                dbId, projectId, _currentUser.TenantId);

            return true;
        }

        // ─── Yardımcı metodlar ───────────────────────────────────────────────

        private static readonly byte[] SqliteMagicBytes =
            "SQLite format 3\0"u8.ToArray(); // 16 byte

        private static async Task ValidateSqliteMagicBytesAsync(IFormFile file, CancellationToken ct)
        {
            var header = new byte[16];

            await using var stream = file.OpenReadStream();
            var read = await stream.ReadAtLeastAsync(header, 16, throwOnEndOfStream: false, ct);

            if (read < 16 || !header.AsSpan().SequenceEqual(SqliteMagicBytes))
                throw new ArgumentException(
                    "Dosya geçerli bir SQLite veritabanı değil. (İçerik doğrulaması başarısız.)");
        }

        private async Task EnsureProjectAccessAsync(int projectId, ProjectPermission minPermission)
        {
            // Önce proje bu tenant'a ait mi?
            var project = await _context.Projects
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == projectId && p.CompanyId == _currentUser.TenantId);

            if (project == null)
                throw new KeyNotFoundException("Proje bulunamadı.");

            // Admin her projeye erişebilir
            if (_currentUser.IsAdmin) return;

            var access = await _context.ProjectAccesses
                .FirstOrDefaultAsync(a => a.ProjectId == projectId && a.UserId == _currentUser.UserId);

            if (access == null || (int)access.Permission < (int)minPermission)
                throw new UnauthorizedAccessException("Bu işlem için yeterli proje izniniz yok.");
        }

        private static string BuildConnectionString(
            DatabaseType dbType, string host, int? port,
            string database, string username, string password)
        {
            return dbType switch
            {
                DatabaseType.Postgres  =>
                    $"Host={host};Port={port ?? 5432};Database={database};Username={username};Password={password};",
                DatabaseType.SqlServer =>
                    $"Server={host}{(port.HasValue ? $",{port}" : "")};Database={database};User Id={username};Password={password};TrustServerCertificate=True;",
                DatabaseType.MySql     =>
                    $"Server={host};Port={port ?? 3306};Database={database};Uid={username};Pwd={password};",
                _ => throw new NotSupportedException($"Desteklenmeyen veritabanı tipi: {dbType}")
            };
        }
    }
}
