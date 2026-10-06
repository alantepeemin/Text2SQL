using FluentValidation;
using Text2Sql.Application.DTOs.Database;
using Text2Sql.Domain.Enums;

namespace Text2Sql.Application.Validators
{
    /// <summary>
    /// Faz 2: CreateDatabaseForm'un Mode'a göre değişen koşullu kuralları —
    /// önceden servis içinde elle if'lerle kontrol ediliyordu, artık istek
    /// daha servise ulaşmadan doğrulanır. Servisteki kontroller derinlemesine
    /// savunma olarak korunur.
    /// </summary>
    public class CreateDatabaseFormValidator : AbstractValidator<CreateDatabaseForm>
    {
        public CreateDatabaseFormValidator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(100);

            When(x => x.Mode == DatabaseCreateMode.LocalFile, () =>
            {
                RuleFor(x => x.DbType)
                    .Equal(DatabaseType.Sqlite)
                    .WithMessage("LocalFile modu yalnızca SQLite için desteklenir.");

                RuleFor(x => x.DatabaseFile)
                    .NotNull()
                    .WithMessage("SQLite dosyası gereklidir.");
            });

            When(x => x.Mode == DatabaseCreateMode.Remote, () =>
            {
                RuleFor(x => x.DbType)
                    .NotEqual(DatabaseType.Sqlite)
                    .WithMessage("Remote modu SQLite için desteklenmez.");

                RuleFor(x => x)
                    .Must(x => !string.IsNullOrWhiteSpace(x.ConnectionString) ||
                               (!string.IsNullOrWhiteSpace(x.Host) &&
                                !string.IsNullOrWhiteSpace(x.Database) &&
                                !string.IsNullOrWhiteSpace(x.Username) &&
                                !string.IsNullOrWhiteSpace(x.Password)))
                    .WithMessage("ConnectionString veya Host/Database/Username/Password bilgileri gereklidir.");
            });
        }
    }
}
