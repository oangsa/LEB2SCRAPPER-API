using LEB2SCRAPPER.Infrastructure.Alerting;

namespace LEB2SCRAPPER.Tests.Infrastructure;

public class EmailFailureAlertOptionsTests
{
    [Fact]
    public void Validate_WhenDisabled_AllowsEmptySmtpSettings()
    {
        var options = new EmailFailureAlertOptions();

        options.Validate();
    }

    [Fact]
    public void Validate_WhenEnabledAndIncomplete_FailsFast()
    {
        var options = new EmailFailureAlertOptions
        {
            Enabled = true
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }
}
