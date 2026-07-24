namespace LEB2SCRAPPER.Infrastructure.Contracts.Outbound;

public interface IClientFingerprintProvider
{
    string CreateForSession(string sessionValue);

    string CreateForUsername(string username);
}
