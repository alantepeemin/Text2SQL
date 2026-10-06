using FluentValidation;
using Text2Sql.Application.DTOs.Auth;

namespace Text2Sql.Application.Validators
{
    // Faz 2: FluentValidation — DataAnnotations'ın ifade edemediği koşullu
    // kurallar için altyapı. Mevcut kurallar birebir korunur (davranış değişmez).

    public class LoginDtoValidator : AbstractValidator<LoginDto>
    {
        public LoginDtoValidator()
        {
            RuleFor(x => x.Email).NotEmpty().EmailAddress();
            RuleFor(x => x.Password).NotEmpty().MinimumLength(6);
        }
    }

    public class CreateCompanyDtoValidator : AbstractValidator<CreateCompanyDto>
    {
        public CreateCompanyDtoValidator()
        {
            RuleFor(x => x.Name).NotEmpty().MinimumLength(2).MaximumLength(100);
            RuleFor(x => x.Domain).MaximumLength(100);
            RuleFor(x => x.MaxUsers).InclusiveBetween(1, 10000);
            RuleFor(x => x.MonthlyQueryLimit).InclusiveBetween(10, 1000000);
        }
    }

    public class CompleteRegistrationDtoValidator : AbstractValidator<CompleteRegistrationDto>
    {
        public CompleteRegistrationDtoValidator()
        {
            RuleFor(x => x.RegistrationToken).NotEmpty();
            RuleFor(x => x.Username).NotEmpty().MinimumLength(3).MaximumLength(50);
            RuleFor(x => x.Email).NotEmpty().EmailAddress();
            RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
        }
    }
}
