using LEB2SCRAPPER.Contracts.Repository.Core;

namespace LEB2SCRAPPER.Service;

public interface ICoreAdapterManager
{
    IRepositoryManager RepositoryManager { get; }
}

public class CoreAdapterManager : ICoreAdapterManager
{
    public CoreAdapterManager(IRepositoryManager repositoryManager)
    {
        RepositoryManager = repositoryManager;
    }
    public IRepositoryManager RepositoryManager { get; }
}
