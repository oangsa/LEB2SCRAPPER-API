using LEB2SCRAPPER.Entity.Exceptions.Leb2Integration;
using LEB2SCRAPPER.Repository.Master;

namespace LEB2SCRAPPER.Tests.Repository;

public class ScrapingRepositoryTests
{
    [Fact]
    public async Task DelayedSemesterLink_SucceedsWithinRenderTimeout()
    {
        var attempts = 0;

        await ScrapingRepository.WaitForUsableSemesterLinkAsync(
            () => Task.FromResult(Interlocked.Increment(ref attempts) >= 3),
            TimeSpan.FromSeconds(30));

        Assert.True(attempts >= 3);
    }

    [Fact]
    public async Task SemesterLinkTimeout_RemainsStructuralParseException()
    {
        var exception = await Assert.ThrowsAsync<StructuralParseException>(() =>
            ScrapingRepository.WaitForUsableSemesterLinkAsync(
                () => Task.FromResult(false),
                TimeSpan.FromMilliseconds(150)));

        Assert.Equal("semesters.semester_links", exception.FailureShape);
    }

    [Fact]
    public async Task SessionExpirationDuringSemesterWait_IsPreserved()
    {
        await Assert.ThrowsAsync<SessionExpiredException>(() =>
            ScrapingRepository.WaitForUsableSemesterLinkAsync(
                () => Task.FromException<bool>(new SessionExpiredException()),
                TimeSpan.FromSeconds(1)));
    }
}
