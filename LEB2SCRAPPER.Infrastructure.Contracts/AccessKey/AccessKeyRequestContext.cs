using LEB2SCRAPPER.Entity.Models.AccessKey;

namespace LEB2SCRAPPER.Infrastructure.Contracts.AccessKey;

public sealed class AccessKeyRequestContext
{
    public AccessKeyState? Current { get; private set; }

    public void Set(AccessKeyState state)
    {
        Current = state;
    }
}
