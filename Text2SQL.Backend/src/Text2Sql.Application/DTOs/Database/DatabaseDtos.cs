using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;
using Text2Sql.Domain.Enums;

namespace Text2Sql.Application.DTOs.Database
{
    public class CreateDatabaseForm
    {
        [Required(ErrorMessage = "Veritabanı adı gereklidir.")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mod seçimi gereklidir.")]
        public DatabaseCreateMode Mode { get; set; }

        [Required(ErrorMessage = "Veritabanı tipi gereklidir.")]
        public DatabaseType DbType { get; set; }

        public IFormFile? DatabaseFile { get; set; }

        public string? ConnectionString { get; set; }
        public string? Host { get; set; }
        public int? Port { get; set; }
        public string? Database { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? SchemaName { get; set; }
    }

    public class ProjectDatabaseDto
    {
        public int Id { get; set; }
        public string DbType { get; set; } = string.Empty;
        public string ConnectionName { get; set; } = string.Empty;
        public string? Host { get; set; }
        public int? Port { get; set; }
        public string? DatabaseName { get; set; }
        public string? SchemaName { get; set; }
    }

    public class DatabaseOption
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string DbType { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }
}
