namespace Text2Sql.Domain.Enums
{
    public enum ProjectPermission
    {
        Viewer = 0,
        Editor = 1,
        Owner  = 2
    }

    public enum DatabaseType
    {
        Sqlite,
        Postgres,
        SqlServer,
        MySql
    }

    public enum DatabaseCreateMode
    {
        LocalFile,
        Remote
    }
}
