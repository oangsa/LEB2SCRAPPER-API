using LEB2SCRAPPER.Infrastructure.Outbound;

namespace LEB2SCRAPPER.Tests.Infrastructure;

public class HmacClientFingerprintProviderTests
{
    [Fact]
    public void UsernameFingerprint_IsNormalizedAndDoesNotExposeInput()
    {
        using var provider = new HmacClientFingerprintProvider();

        var first = provider.CreateForUsername("  Fake.User  ");
        var second = provider.CreateForUsername("fake.user");

        Assert.Equal(first, second);
        Assert.DoesNotContain("FAKE", first, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(64, first.Length);
    }

    [Fact]
    public void Fingerprints_AreDomainSeparatedAndProcessLocal()
    {
        using var firstProvider = new HmacClientFingerprintProvider();
        using var secondProvider = new HmacClientFingerprintProvider();

        var sessionFingerprint = firstProvider.CreateForSession("same-input");
        var usernameFingerprint = firstProvider.CreateForUsername("same-input");
        var restartedFingerprint = secondProvider.CreateForSession("same-input");

        Assert.NotEqual(sessionFingerprint, usernameFingerprint);
        Assert.NotEqual(sessionFingerprint, restartedFingerprint);
        Assert.DoesNotContain("same-input", sessionFingerprint);
    }
}
