using LEB2SCRAPPER.Security;

namespace LEB2SCRAPPER.Tests.Security;

public class SensitiveDataRedactorTests
{
    [Fact]
    public void Redact_RemovesOpaqueSessionValue()
    {
        const string sessionCookie = "leb2_session=fake-secret; other=fake-value";
        var message = $"Outbound request failed with Cookie: {sessionCookie}";

        var redacted = SensitiveDataRedactor.Redact(message, sessionCookie);

        Assert.DoesNotContain("fake-secret", redacted);
        Assert.DoesNotContain("fake-value", redacted);
        Assert.Contains("[REDACTED]", redacted);
    }

    [Fact]
    public void Redact_RemovesAccessKeyHeaderValue()
    {
        const string accessKey = "access-key-test-value";
        var message = $"Request access-key: {accessKey}";

        var redacted = SensitiveDataRedactor.Redact(message, accessKey);

        Assert.DoesNotContain(accessKey, redacted);
        Assert.Contains("access-key: [REDACTED]", redacted);
    }
}
