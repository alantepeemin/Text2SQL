using System.Text.Json;

namespace Text2Sql.Tests.Common.Http
{
    /// <summary>Tüm testlerde tek JSON ayarı — camelCase, API ile aynı.</summary>
    public static class TestJson
    {
        public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    }
}
