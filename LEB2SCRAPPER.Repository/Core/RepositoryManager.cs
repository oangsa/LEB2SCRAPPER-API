using LEB2SCRAPPER.Contracts.Repository.Core;
using LEB2SCRAPPER.Contracts.Repository;
using LEB2SCRAPPER.Repository.Master;

namespace LEB2SCRAPPER.Repository.Core
{
    public class RepositoryManager : IRepositoryManager
    {
        private readonly Lazy<IScrapingRepository> _scrapingRepository;
        private readonly Lazy<IActivityRepository> _activityRepository;
        private readonly Lazy<IUserRepository> _userRepository;

        public RepositoryManager()
        {
            _scrapingRepository = new Lazy<IScrapingRepository>(() => new ScrapingRepository());
            _activityRepository = new Lazy<IActivityRepository>(() => new ActivityRepository());
            _userRepository = new Lazy<IUserRepository>(() => new UserRepository());
        }

        public IScrapingRepository ScrapingRepository => _scrapingRepository.Value;
        public IActivityRepository ActivityRepository => _activityRepository.Value;
        public IUserRepository UserRepository => _userRepository.Value;

    }
}
