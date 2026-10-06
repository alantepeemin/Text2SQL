namespace Text2Sql.Application.Contracts
{
    public sealed record EmailMessage(string To, string Subject, string HtmlBody);

    /// <summary>
    /// FAZ 4: E-posta gönderim portu. Davetiye özelliği bugüne kadar e-posta
    /// GÖNDERMEDEN "gönderildi" diyordu — artık gerçekten iletilir.
    /// Config Email:Enabled=false iken NoOp implementasyonu devrededir
    /// (SMTP kurulumu olmadan sistem çalışmaya devam eder).
    /// </summary>
    public interface IEmailSender
    {
        Task SendAsync(EmailMessage message, CancellationToken ct = default);
    }
}
