using LEB2SCRAPPER.Service.Contracts.Master;

namespace LEB2SCRAPPER.Service.Contracts.Core;

public interface IServiceManager
{
    IActivityService ActivityService { get; }
    IAccessKeyService AccessKeyService { get; }
    IUserService UserService { get; }
    IClassService ClassService { get; }
    ISemesterService SemesterService { get; }
}
