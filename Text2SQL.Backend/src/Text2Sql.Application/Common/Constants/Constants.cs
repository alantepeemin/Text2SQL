namespace Text2Sql.Application.Common.Constants
{
    public static class Roles
    {
        public const string Admin   = "admin";
        public const string Manager = "manager";
        public const string User    = "user";
    }

    public static class UserStatus
    {
        public const string Pending   = "pending";
        public const string Approved  = "approved";
        public const string Rejected  = "rejected";
        public const string Suspended = "suspended";
    }

    public static class InvitationType
    {
        public const string Invitation = "invitation";
        public const string Code       = "code";
        public const string Domain     = "domain";
    }
}
