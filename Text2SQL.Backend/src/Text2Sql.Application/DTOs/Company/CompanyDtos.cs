namespace Text2Sql.Application.DTOs.Company
{
    public class InviteUserDto
    {
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = "user";
    }

    public class CompanyInvitationDto
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;

        // FAZ 4: Token admin listesine eklendi — e-posta kapalıyken (veya
        // ulaşmadığında) admin daveti linki elle paylaşabilsin. Endpoint zaten
        // yalnızca admin rolüne açık.
        public string InvitationToken { get; set; } = string.Empty;
        public string InvitedBy { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public bool IsExpired => DateTime.UtcNow > ExpiresAt;
    }

    public class PendingUserDto
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? InvitationType { get; set; }
        public string? InvitedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? InvitedAt { get; set; }
        public string? LastIpAddress { get; set; }
    }

    public class UserApprovalDto
    {
        public int UserId { get; set; }
        public string Action { get; set; } = string.Empty;
        public string Role { get; set; } = "user";
        public string? Notes { get; set; }
    }

    public class UpdateUserRoleDto
    {
        public int UserId { get; set; }
        public string NewRole { get; set; } = string.Empty;
        public string? Notes { get; set; }
    }

    public class DetailedUserDto
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? InvitationType { get; set; }
        public string? InvitedBy { get; set; }
        public string? ApprovedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public DateTime? LastActivityAt { get; set; }
        public bool IsActive { get; set; }
        public int RemainingTokens { get; set; }
        public int MonthlyLimit { get; set; }
        public int ProjectCount { get; set; }
        public DateTime? LastQueryAt { get; set; }
        public int TotalQueries { get; set; }
    }

    public class UserActivitySummaryDto
    {
        public int TotalUsers { get; set; }
        public int ActiveUsers { get; set; }
        public int PendingUsers { get; set; }
        public int SuspendedUsers { get; set; }
        public int UsersLoggedInToday { get; set; }
        public int TotalQueriesThisMonth { get; set; }
        public List<RecentActivityDto> RecentActivities { get; set; } = new();
    }

    public class RecentActivityDto
    {
        public string Username { get; set; } = string.Empty;
        public string Activity { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string? ProjectName { get; set; }
    }
}
