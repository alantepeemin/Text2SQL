using Text2Sql.Application.Common;
using Text2Sql.Application.DTOs.Company;

namespace Text2Sql.Application.Contracts
{
    public interface ICompanyService
    {
        Task<bool> InviteUserAsync(InviteUserDto dto);
        Task<string> GenerateCompanyCodeAsync();
        Task<List<CompanyInvitationDto>> GetPendingInvitationsAsync();
        Task<PagedResult<DetailedUserDto>> GetCompanyUsersAsync(int page = 1, int pageSize = 100);
        Task<bool> RemoveUserAsync(int userId);
        Task<bool> ValidateCompanyCodeAsync(string code);
        Task<List<PendingUserDto>> GetPendingUsersAsync();
        Task<bool> ApproveUserAsync(UserApprovalDto dto);
        Task<UserActivitySummaryDto> GetUserActivitySummaryAsync();
        Task<bool> UpdateUserRoleAsync(UpdateUserRoleDto dto);
    }
}
