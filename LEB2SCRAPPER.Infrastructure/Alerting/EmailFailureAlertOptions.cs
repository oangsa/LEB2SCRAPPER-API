namespace LEB2SCRAPPER.Infrastructure.Alerting;

public sealed class EmailFailureAlertOptions
{
    public bool Enabled { get; set; }
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string ToAddress { get; set; } = string.Empty;
    public int DeliveryTimeoutSeconds { get; set; } = 30;

    public void Validate()
    {
        if (!Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(SmtpHost)
            || string.IsNullOrWhiteSpace(FromAddress)
            || string.IsNullOrWhiteSpace(ToAddress))
        {
            throw new InvalidOperationException(
                "Email failure alerts are enabled but SMTP host, sender, or recipient is missing.");
        }

        if (SmtpPort <= 0 || DeliveryTimeoutSeconds <= 0)
        {
            throw new InvalidOperationException(
                "Email failure alert port and delivery timeout must be greater than zero.");
        }
    }
}
