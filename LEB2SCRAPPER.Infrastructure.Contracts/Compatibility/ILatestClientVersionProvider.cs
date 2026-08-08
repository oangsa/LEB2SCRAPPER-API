namespace LEB2SCRAPPER.Infrastructure.Contracts.Compatibility;

public interface ILatestClientVersionProvider
{
    Task<string?> GetLatestVersionAsync(CancellationToken cancellationToken);
}
