using LEB2SCRAPPER.Contracts.Repository.Core;
using LEB2SCRAPPER.Contracts.Repository;
using LEB2SCRAPPER.Infrastructure.Contracts.HttpService;
using LEB2SCRAPPER.Infrastructure.Contracts.Outbound;
using LEB2SCRAPPER.Repository.Master;
using LEB2SCRAPPER.Repository.Caching;

namespace LEB2SCRAPPER.Repository.Core
{
    public class RepositoryManager : IRepositoryManager
    {
        private readonly Lazy<IScrapingRepository> _scrapingRepository;
        private readonly Lazy<IActivityRepository> _activityRepository;
        private readonly Lazy<IUserRepository> _userRepository;

        public RepositoryManager(
            IHttpService httpService,
            IOutboundRequestGate outboundRequestGate,
            IClientFingerprintProvider clientFingerprintProvider,
            IStructuralScrapeCache structuralScrapeCache,
            IActivityResultCache activityResultCache)
        {
            _scrapingRepository = new Lazy<IScrapingRepository>(
                () => new ScrapingRepository(
                    outboundRequestGate,
                    clientFingerprintProvider,
                    structuralScrapeCache));
            _activityRepository = new Lazy<IActivityRepository>(
                () => new ActivityRepository(
                    httpService,
                    clientFingerprintProvider,
                    activityResultCache));
            _userRepository = new Lazy<IUserRepository>(
                () => new UserRepository(
                    httpService,
                    clientFingerprintProvider));
        }

        public IScrapingRepository ScrapingRepository => _scrapingRepository.Value;
        public IActivityRepository ActivityRepository => _activityRepository.Value;
        public IUserRepository UserRepository => _userRepository.Value;

    }
}
