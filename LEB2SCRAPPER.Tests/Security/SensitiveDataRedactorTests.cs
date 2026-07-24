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
}
