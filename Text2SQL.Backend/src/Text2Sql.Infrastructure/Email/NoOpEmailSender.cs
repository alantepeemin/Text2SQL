using Microsoft.Extensions.Logging;
using Text2Sql.Application.Contracts;

namespace Text2Sql.Infrastructure.Email
{
    /// <summary>Email:Enabled=false iken devrede — göndermez, loglar.</summary>
    public sealed class NoOpEmailSender : IEmailSender
    {
        private readonly ILogger<NoOpEmailSender> _logger;

        public NoOpEmailSender(ILogger<NoOpEmailSender> logger) => _logger = logger;

        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            _logger.LogWarning(
                "E-posta gönderimi devre dışı (Email:Enabled=false). Gönderilmeyen mesaj — To: {To}, Subject: {Subject}",
                message.To, message.Subject);
            return Task.CompletedTask;
        }
    }
}
