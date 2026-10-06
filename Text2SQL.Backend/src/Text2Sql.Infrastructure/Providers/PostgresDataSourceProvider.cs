using Microsoft.AspNetCore.DataProtection;
using Npgsql;
using System.Text;
using Text2Sql.Application.Contracts;
using Text2Sql.Domain.Entities;

namespace Text2Sql.Infrastructure.Providers
{
    /// <summary>
    /// FAZ 3: İlk uzak veri kaynağı provider'ı — provider mimarisinin kanıtı.
    ///
    /// Güvenlik katmanları:
    /// 1. Oturum SALT-OKUNUR moda alınır (SET SESSION ... READ ONLY) — yazma
    ///    denemeleri DB tarafından reddedilir.
    /// 2. SQL, SqlStatementValidator'dan geçmiş tek SELECT/WITH statement'ıdır.
    /// 3. Satır limiti + komut timeout'u.
    /// 4. Dokümante zorunluluk: müşteri bağlantısı en-az-yetkili (salt-okunur)
    ///    DB kullanıcısıyla tanımlanmalıdır.
    /// </summary>
    public sealed class PostgresDataSourceProvider : IDataSourceProvider
    {
        private readonly IDataProtector _protector;

        public PostgresDataSourceProvider(IDataProtectionProvider protectionProvider)
        {
            // ProjectDatabaseService ile AYNI purpose string — şifre çözümü için şart
            _protector = protectionProvider.CreateProtector("DatabaseConnections");
        }

        public string Dialect => "PostgreSQL";

        public bool CanHandle(string dbType) =>
            dbType.Equals("postgres", StringComparison.OrdinalIgnoreCase);

        public async Task<string> GetSchemaAsync(ProjectDatabase dataSource, CancellationToken ct = default)
        {
            var schemaName = string.IsNullOrWhiteSpace(dataSource.SchemaName) ? "public" : dataSource.SchemaName;

            await using var conn = new NpgsqlConnection(GetConnectionString(dataSource));
            await conn.OpenAsync(ct);

            const string sql = @"
                SELECT table_name, column_name, data_type, is_nullable
                FROM information_schema.columns
                WHERE table_schema = @schema
                ORDER BY table_name, ordinal_position;";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("schema", schemaName);
            cmd.CommandTimeout = 15;

            var sb = new StringBuilder();
            string? currentTable = null;

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var table    = reader.GetString(0);
                var column   = reader.GetString(1);
                var dataType = reader.GetString(2);
                var nullable = reader.GetString(3) == "YES" ? "NULL" : "NOT NULL";

                if (table != currentTable)
                {
                    if (currentTable != null) sb.AppendLine(");").AppendLine();
                    sb.AppendLine($"CREATE TABLE {table} (");
                    currentTable = table;
                }
                else
                {
                    sb.AppendLine(",");
                }
                sb.Append($"    {column} {dataType} {nullable}");
            }
            if (currentTable != null) sb.AppendLine().AppendLine(");");

            var schema = sb.ToString().Trim();
            if (string.IsNullOrEmpty(schema))
                throw new InvalidOperationException(
                    $"'{schemaName}' şemasında sorgulanabilir tablo bulunamadı.");

            return schema;
        }

        public async Task<QueryExecutionResult> ExecuteReadOnlyAsync(
            ProjectDatabase dataSource, string sql, int maxRows, int timeoutSeconds,
            CancellationToken ct = default)
        {
            var columns = new List<string>();
            var rows    = new List<List<string>>();

            await using var conn = new NpgsqlConnection(GetConnectionString(dataSource));
            await conn.OpenAsync(ct);

            // Oturumu salt-okunur moda al — yazma denemeleri DB tarafından reddedilir
            await using (var readOnlyCmd = new NpgsqlCommand(
                "SET SESSION CHARACTERISTICS AS TRANSACTION READ ONLY;", conn))
            {
                await readOnlyCmd.ExecuteNonQueryAsync(ct);
            }

            await using var cmd = new NpgsqlCommand(sql, conn)
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

        private string GetConnectionString(ProjectDatabase dataSource)
        {
            if (string.IsNullOrEmpty(dataSource.PasswordEncrypted))
                throw new InvalidOperationException("Veri kaynağının bağlantı bilgisi eksik.");

            // AddRemoteAsync tam bağlantı dizesini şifreleyerek saklar
            return _protector.Unprotect(dataSource.PasswordEncrypted);
        }
    }
}
