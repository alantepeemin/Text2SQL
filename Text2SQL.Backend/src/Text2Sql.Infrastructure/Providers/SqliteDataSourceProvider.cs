using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using System.Text;
using Text2Sql.Application.Contracts;
using Text2Sql.Domain.Entities;

namespace Text2Sql.Infrastructure.Providers
{
    /// <summary>
    /// FAZ 3: Eski QueryService'teki SQLite'a özel kod buraya damıtıldı.
    /// Davranış birebir korunur: ReadOnly mod, sqlite_master şeması,
    /// satır limiti ve komut timeout'u.
    /// </summary>
    public sealed class SqliteDataSourceProvider : IDataSourceProvider
    {
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _configuration;

        public SqliteDataSourceProvider(IWebHostEnvironment env, IConfiguration configuration)
        {
            _env = env;
            _configuration = configuration;
        }

        public string Dialect => "SQLite";

        public bool CanHandle(string dbType) =>
            dbType.Equals("sqlite", StringComparison.OrdinalIgnoreCase);

        public async Task<string> GetSchemaAsync(ProjectDatabase dataSource, CancellationToken ct = default)
        {
            var fullPath = ResolvePath(dataSource);

            await using var connection = new SqliteConnection($"Data Source={fullPath};Mode=ReadOnly");
            await connection.OpenAsync(ct);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT sql FROM sqlite_master
                WHERE type = 'table'
                  AND name NOT LIKE 'sqlite_%'
                ORDER BY name;";

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            var sb = new StringBuilder();

            while (await reader.ReadAsync(ct))
            {
                var tableDef = reader.IsDBNull(0) ? null : reader.GetString(0);
                if (!string.IsNullOrWhiteSpace(tableDef))
                {
                    sb.AppendLine(tableDef + ";");
                    sb.AppendLine();
                }
            }

            var schema = sb.ToString().Trim();
            if (string.IsNullOrEmpty(schema))
                throw new InvalidOperationException("Veritabanında sorgulanabilir tablo bulunamadı.");

            return schema;
        }

        public async Task<QueryExecutionResult> ExecuteReadOnlyAsync(
            ProjectDatabase dataSource, string sql, int maxRows, int timeoutSeconds,
            CancellationToken ct = default)
        {
            var fullPath = ResolvePath(dataSource);

            var columns = new List<string>();
            var rows    = new List<List<string>>();

            await using var conn = new SqliteConnection($"Data Source={fullPath};Mode=ReadOnly");
            await conn.OpenAsync(ct);

            await using var cmd = new SqliteCommand(sql, conn)
            {
                CommandTimeout = timeoutSeconds
            };

            await using var reader = await cmd.ExecuteReaderAsync(ct);

            for (int i = 0; i < reader.FieldCount; i++)
                columns.Add(reader.GetName(i));

            int rowCount = 0;
            while (await reader.ReadAsync(ct) && rowCount < maxRows)
            {
                var row = new List<string>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var val = reader.GetValue(i);
                    row.Add(val == DBNull.Value ? "" : val?.ToString() ?? "");
                }
                rows.Add(row);
                rowCount++;
            }

            return new QueryExecutionResult(columns, rows);
        }

        private string ResolvePath(ProjectDatabase dataSource)
        {
            var sqliteRoot = _configuration.GetValue<string>("Storage:SqliteRoot") ?? "Data/SQLite";
            var fullPath   = Path.Combine(_env.ContentRootPath, sqliteRoot, dataSource.SqliteFilePath ?? "");

            if (!File.Exists(fullPath))
                throw new FileNotFoundException("Veritabanı dosyası bulunamadı.");

            return fullPath;
        }
    }
}
