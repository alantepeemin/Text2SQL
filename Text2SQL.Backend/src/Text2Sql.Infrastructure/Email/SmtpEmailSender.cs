using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using Text2Sql.Application.Contracts;

namespace Text2Sql.Infrastructure.Email
{
    /// <summary>
    /// FAZ 4: MailKit tabanlı SMTP gönderici.
    /// Config: Email:Smtp:{Host,Port,Username,Password,FromAddress,FromName,UseStartTls}
    /// Sağlayıcı değişimi (Resend/SES) = aynı port, yeni adaptör.
    /// </summary>
    public sealed class SmtpEmailSender : IEmailSender
    {
        private readonly IConfiguration _config;
        private readonly ILogger<SmtpEmailSender> _logger;

        public SmtpEmailSender(IConfiguration config, ILogger<SmtpEmailSender> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            var host = _config["Email:Smtp:Host"]
                ?? throw new InvalidOperationException("Email:Smtp:Host yapılandırılmamış.");
            var port        = _config.GetValue("Email:Smtp:Port", 587);
            var username    = _config["Email:Smtp:Username"];
            var password    = _config["Email:Smtp:Password"];
            var fromAddress = _config["Email:Smtp:FromAddress"] ?? username
                ?? throw new InvalidOperationException("Email:Smtp:FromAddress yapılandırılmamış.");
            var fromName    = _config["Email:Smtp:FromName"] ?? "Text2SQL";
            var useStartTls = _config.GetValue("Email:Smtp:UseStartTls", true);

            var mime = new MimeMessage();
            mime.From.Add(new MailboxAddress(fromName, fromAddress));
            mime.To.Add(MailboxAddress.Parse(message.To));
            mime.Subject = message.Subject;
            mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody }.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(host, port,
                useStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto, ct);

            if (!string.IsNullOrEmpty(username))
                await client.AuthenticateAsync(username, password, ct);

            await client.SendAsync(mime, ct);
            await client.DisconnectAsync(true, ct);

            _logger.LogInformation("E-posta gönderildi — To: {To}, Subject: {Subject}",
                message.To, message.Subject);
        }
    }
}
