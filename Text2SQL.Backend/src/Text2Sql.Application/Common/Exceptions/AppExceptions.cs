namespace Text2Sql.Application.Common.Exceptions
{
    /// <summary>
    /// Faz 2: Alan (domain) hata hiyerarşisi.
    /// BCL exception tiplerine (ArgumentException → 400 gibi) anlam yüklemek
    /// kırılgandır: üçüncü parti kodun fırlattığı aynı tip, istemci hatası gibi
    /// raporlanır. Bu hiyerarşi HTTP eşlemesini açık sözleşmeye bağlar.
    /// Eski BCL eşlemeleri geçiş süresince ExceptionMiddleware'de korunur.
    /// </summary>
    public abstract class AppException : Exception
    {
        public abstract int StatusCode { get; }

        /// <summary>
        /// API v2: Makine-okunur hata kodu. İstemci mesaj METNİNE bakmak
        /// zorunda kalmaz (i18n ve sürüm değişimlerine dayanıklı).
        /// </summary>
        public abstract string Code { get; }

        protected AppException(string message) : base(message) { }
    }

    public sealed class BadRequestException : AppException
    {
        public override int StatusCode => 400;
        public override string Code => "BAD_REQUEST";
        public BadRequestException(string message) : base(message) { }
    }

    public sealed class UnauthorizedException : AppException
    {
        public override int StatusCode => 401;
        public override string Code => "UNAUTHORIZED";
        public UnauthorizedException(string message) : base(message) { }
    }

    public sealed class ForbiddenException : AppException
    {
        public override int StatusCode => 403;
        public override string Code => "FORBIDDEN";
        public ForbiddenException(string message) : base(message) { }
    }

    public sealed class NotFoundException : AppException
    {
        public override int StatusCode => 404;
        public override string Code => "NOT_FOUND";
        public NotFoundException(string message) : base(message) { }
    }

    public sealed class ConflictException : AppException
    {
        public override int StatusCode => 409;
        public override string Code => "CONFLICT";
        public ConflictException(string message) : base(message) { }
    }

    /// <summary>
    /// SaaS-5: Özellik mevcut planda yok. 402 Payment Required —
    /// 403'ten bilinçli olarak ayrıldı: 403 "yetkin yok", 402 "paketin kapsamıyor"
    /// demektir. İstemci bu ayrımla doğru mesajı ve yükseltme çağrısını gösterebilir.
    /// </summary>
    public sealed class FeatureNotAvailableException : AppException
    {
        public override int StatusCode => 402;
        public override string Code => "FEATURE_NOT_AVAILABLE";
        public FeatureNotAvailableException(string message) : base(message) { }
    }

    /// <summary>SaaS-5: Plan limiti aşıldı (üye/proje/veri kaynağı/anahtar sayısı).</summary>
    public sealed class PlanLimitExceededException : AppException
    {
        public override int StatusCode => 402;
        public override string Code => "PLAN_LIMIT_EXCEEDED";
        public PlanLimitExceededException(string message) : base(message) { }
    }

    public sealed class QuotaExceededException : AppException
    {
        public override int StatusCode => 429;
        public override string Code => "QUOTA_EXCEEDED";
        public QuotaExceededException(string message) : base(message) { }
    }
}
