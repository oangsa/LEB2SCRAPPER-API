using System.Net;
using System.Net.Mail;
using LEB2SCRAPPER.Infrastructure.Contracts.Alerting;

namespace LEB2SCRAPPER.Infrastructure.Alerting;

public sealed class EmailFailureAlerter : IFailureAlerter
{
    private readonly EmailFailureAlertOptions _options;

    public EmailFailureAlerter(EmailFailureAlertOptions options)
    {
        _options = options;
        _options.Validate();
    }

    public async Task NotifyStructuralFailureAsync(
        StructuralFailureAlert alert,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return;
        }

        using var message = new MailMessage(
            _options.FromAddress,
            _options.ToAddress,
            $"LEB2 scraper structure failure: {alert.Endpoint}",
            BuildBody(alert));
        using var smtpClient = new SmtpClient(_options.SmtpHost, _options.SmtpPort)
        {
            EnableSsl = _options.EnableSsl
        };

        if (!string.IsNullOrWhiteSpace(_options.Username))
        {
            smtpClient.Credentials = new NetworkCredential(
                _options.Username,
                _options.Password);
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(_options.DeliveryTimeoutSeconds));

        await smtpClient.SendMailAsync(message, timeoutSource.Token);
    }

    private static string BuildBody(StructuralFailureAlert alert)
    {
        return $"""
            LEB2 returned the same unexpected response shape repeatedly.

            Endpoint: {alert.Endpoint}
            Failure shape: {alert.FailureShape}
            Attempts: {alert.FailureCount}
            Window started (UTC): {alert.WindowStartedAt:O}
            Detected (UTC): {alert.DetectedAt:O}
            """;
    }
}
