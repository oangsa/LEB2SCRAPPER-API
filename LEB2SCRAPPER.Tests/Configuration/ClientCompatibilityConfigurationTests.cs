using LEB2SCRAPPER.Configuration;
using LEB2SCRAPPER.Infrastructure.Contracts.Compatibility;

namespace LEB2SCRAPPER.Tests.Configuration;

public sealed class ClientCompatibilityConfigurationTests
{
    [Fact]
    public void Create_AcceptsValidConfiguration()
    {
        var configuration = ClientCompatibilityConfiguration.Create(
            new ClientCompatibilityOptions
            {
                MinimumClientVersion = "0.5.0",
                LatestClientVersion = "0.6.0",
                DownloadUrl = "https://example.test/releases"
            });

        Assert.Equal("0.5.0", configuration.MinimumClientVersion);
        Assert.Equal("0.6.0", configuration.LatestClientVersion);
        Assert.Equal("https://example.test/releases", configuration.DownloadUrl);
    }

    [Fact]
    public void Create_RejectsMalformedMinimumVersion()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ClientCompatibilityConfiguration.Create(
                CreateOptions(minimumVersion: "banana")));
    }

    [Fact]
    public void Create_RejectsMalformedLatestVersion()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ClientCompatibilityConfiguration.Create(
                CreateOptions(latestVersion: "banana")));
    }

    [Fact]
    public void Create_RejectsMinimumVersionAboveLatestVersion()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ClientCompatibilityConfiguration.Create(
                CreateOptions(
                    minimumVersion: "1.0.0",
                    latestVersion: "0.9.0")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/releases")]
    [InlineData("ftp://example.test/releases")]
    [InlineData("https://")]
    public void Create_RejectsInvalidDownloadUrl(string downloadUrl)
    {
        Assert.Throws<InvalidOperationException>(() =>
            ClientCompatibilityConfiguration.Create(
                CreateOptions(downloadUrl: downloadUrl)));
    }

    private static ClientCompatibilityOptions CreateOptions(
        string minimumVersion = "0.5.0",
        string latestVersion = "0.5.0",
        string downloadUrl = "https://example.test/releases")
    {
        return new ClientCompatibilityOptions
        {
            MinimumClientVersion = minimumVersion,
            LatestClientVersion = latestVersion,
            DownloadUrl = downloadUrl
        };
    }
}
