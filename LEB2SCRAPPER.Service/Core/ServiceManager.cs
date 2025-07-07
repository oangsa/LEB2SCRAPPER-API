using LEB2SCRAPPER.Service.Contracts.Core;
using LEB2SCRAPPER.Service.Contracts.Master;
using LEB2SCRAPPER.Service.Master;

namespace LEB2SCRAPPER.Service.Core;

public class ServiceManager : IServiceManager
{
    private readonly Lazy<IActivityService> _activityService;
    private readonly Lazy<IUserService> _userService;
    private readonly Lazy<IClassService> _classService;
    private readonly Lazy<ISemesterService> _semesterService;

    public ServiceManager(ICoreAdapterManager coreAdapterManager)
    {
        _activityService = new Lazy<IActivityService>(() => new ActivityService(coreAdapterManager));
        _userService = new Lazy<IUserService>(() => new UserService(coreAdapterManager));
        _classService = new Lazy<IClassService>(() => new ClassService(coreAdapterManager));
        _semesterService = new Lazy<ISemesterService>(() => new SemesterService(coreAdapterManager));
    }
    public IActivityService ActivityService => _activityService.Value;
    public IUserService UserService => _userService.Value;
    public IClassService ClassService => _classService.Value;
    public ISemesterService SemesterService => _semesterService.Value;
}
