namespace LEB2SCRAPPER.Infrastructure.Contracts.Authentication;

public interface ILeb2SessionCredential
{
    string? Value { get; }
}

public interface ILeb2SessionCredentialStore : ILeb2SessionCredential
{
    void Clear();
    void Set(string value);
}
