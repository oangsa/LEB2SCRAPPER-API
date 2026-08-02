using LEB2SCRAPPER.Entity.Models.AccessKey;

namespace LEB2SCRAPPER.Infrastructure.Contracts.AccessKey;

public sealed class DeviceBindingRequestContext
{
    public DeviceBindingRequest? Current { get; private set; }

    public void Set(DeviceBindingRequest request)
    {
        Current = request;
    }
}
