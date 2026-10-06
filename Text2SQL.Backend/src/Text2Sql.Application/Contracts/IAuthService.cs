using Text2Sql.Domain.Entities;
using Text2Sql.Application.DTOs.Auth;

namespace Text2Sql.Application.Contracts
{
    public interface IAuthService
    {
        Task<CreateCompanyResponse>     CreateCompanyAsync(CreateCompanyDto dto);
        Task<AuthResponseDto>           CompleteRegistrationAsync(CompleteRegistrationDto dto);
        Task<AuthResponseDto>           LoginAsync(LoginDto dto, string? ipAddress = null);
        Task<PendingRegistrationResult> AcceptInvitationAsync(AcceptInvitationDto dto);
        Task<PendingRegistrationResult> JoinByCodeAsync(JoinByCodeDto dto);
        Task<AuthResponseDto>           RefreshTokenAsync(string refreshToken, string? ipAddress = null);
        Task                            RevokeRefreshTokenAsync(string refreshToken, string reason = "Logout");
        // SaaS-7: E-posta doğrulama
        Task<bool> ConfirmEmailAsync(string token, CancellationToken ct = default);
        Task ResendEmailConfirmationAsync(CancellationToken ct = default);

        // SaaS-2: Çok organizasyonlu üyelik
        Task<AuthResponseDto>                SwitchOrganizationAsync(int organizationId, string? ipAddress = null);
        Task<List<OrganizationSummaryDto>>   GetMyOrganizationsAsync();

        string GenerateAccessToken(User user, Membership membership);
    }
}
