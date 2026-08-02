namespace LEB2SCRAPPER.Contracts.Repository.Core;

public interface IRepositoryManager
{
    IScrapingRepository ScrapingRepository { get; }
    IActivityRepository ActivityRepository { get; }
    IUserRepository UserRepository { get; }
    IAccessKeyRepository AccessKeyRepository { get; }
}
