using LEB2SCRAPPER.Infrastructure.Contracts.Authentication;

namespace LEB2SCRAPPER.Authentication;

public sealed class Leb2SessionCredential : ILeb2SessionCredentialStore, IDisposable
{
    private string? _value;

    public string? Value => _value;

    public void Set(string value)
    {
        _value = value;
    }

    public void Clear()
    {
        _value = null;
    }

    public void Dispose()
    {
        Clear();
    }
}
