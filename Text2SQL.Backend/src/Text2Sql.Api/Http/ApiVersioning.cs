namespace Text2Sql.Api.Http
{
    /// <summary>
    /// API sürüm tespiti.
    ///
    /// Yol tabanlı sürümleme (/api/v1, /api/v2) tercih edildi: tarayıcıdan
    /// denenebilir, proxy/log'larda görünür, Swagger'da ayrıştırılabilir.
    /// (Header tabanlı sürümleme daha "saf" kabul edilir ama keşfedilebilirliği düşüktür.)
    ///
    /// Sürümsüz yollar (/api/company/...) v1 kabul edilir — ilk günden beri
    /// var olan istemciler kırılmasın.
    /// </summary>
    public static class ApiVersioning
    {
        public const string V1 = "v1";
        public const string V2 = "v2";

        public static string ResolveVersion(HttpContext context)
        {
            var path = context.Request.Path.Value ?? string.Empty;
            return path.StartsWith("/api/v2/", StringComparison.OrdinalIgnoreCase) ? V2 : V1;
        }

        public static bool IsV2(this HttpContext context)
            => ResolveVersion(context) == V2;

        /// <summary>Result filter'ın okuyacağı isteğe bağlı başarı mesajı.</summary>
        public const string MessageItemKey = "__api_success_message";

        /// <summary>
        /// v1 zarfı YALNIZCA bu işaretin konduğu yanıtlara uygulanır.
        ///
        /// Neden: bazı uçlar (queries/execute, queries/history) v1'de de
        /// ZATEN çıplak veri döndürüyordu. Her 2xx yanıtı sarmalamak, mevcut
        /// istemcileri kıran sessiz bir sözleşme değişikliği olurdu.
        /// </summary>
        public const string EnvelopeItemKey = "__api_envelope";
    }
}
