using Microsoft.Data.SqlClient;
using Npgsql;
using System.Data.Common;
using Text2Sql.Application.Contracts;
using Text2Sql.Domain.Enums;

namespace Text2Sql.Infrastructure.Services
{
    public class ConnectionTester : IConnectionTester
    {
        public async Task<bool> TestConnectionAsync(DatabaseType dbType, string connectionString, CancellationToken cancellationToken = default)
        {
            try
            {
                DbConnection connection = dbType switch
                {
                    DatabaseType.SqlServer => new SqlConnection(connectionString),
                    DatabaseType.Postgres  => new NpgsqlConnection(connectionString), // Faz 1: etkinleştirildi
                    // DatabaseType.MySql  => new MySqlConnection(connectionString), // Faz 3+
                    _ => throw new NotSupportedException($"Veritabanı tipi desteklenmiyor: {dbType}")
                };

                await using (connection)
                {
                    await connection.OpenAsync(cancellationToken);
                    using var command = connection.CreateCommand();
                    command.CommandText = "SELECT 1";
                    command.CommandTimeout = 10;
                    await command.ExecuteScalarAsync(cancellationToken);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
