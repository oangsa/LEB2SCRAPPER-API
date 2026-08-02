using LEB2SCRAPPER.Contracts.Repository.Core;
using LEB2SCRAPPER.Contracts.Repository;
using LEB2SCRAPPER.Infrastructure.Contracts.HttpService;
using LEB2SCRAPPER.Infrastructure.Contracts.Outbound;
using LEB2SCRAPPER.Repository.Master;
using LEB2SCRAPPER.Repository.Caching;
using Microsoft.Extensions.Logging;

namespace LEB2SCRAPPER.Repository.Core
{
    public class RepositoryManager : IRepositoryManager
    {
        private readonly Lazy<IScrapingRepository> _scrapingRepository;
        private readonly Lazy<IActivityRepository> _activityRepository;
        private readonly Lazy<IUserRepository> _userRepository;
        private readonly Lazy<IAccessKeyRepository> _accessKeyRepository;

        public RepositoryManager(
            IHttpService httpService,
            IOutboundRequestGate outboundRequestGate,
            IClientFingerprintProvider clientFingerprintProvider,
            IStructuralScrapeCache structuralScrapeCache,
            IActivityResultCache activityResultCache,
            IAccessKeyRepository accessKeyRepository,
            ILogger<ScrapingRepository> scrapingRepositoryLogger)
        {
            _scrapingRepository = new Lazy<IScrapingRepository>(
                () => new ScrapingRepository(
                    outboundRequestGate,
                    clientFingerprintProvider,
                    structuralScrapeCache,
                    scrapingRepositoryLogger));
            _activityRepository = new Lazy<IActivityRepository>(
                () => new ActivityRepository(
                    httpService,
                    clientFingerprintProvider,
                    activityResultCache));
            _userRepository = new Lazy<IUserRepository>(
                () => new UserRepository(
                    httpService,
                    clientFingerprintProvider));
            _accessKeyRepository = new Lazy<IAccessKeyRepository>(
                () => accessKeyRepository);
        }

        public IScrapingRepository ScrapingRepository => _scrapingRepository.Value;
        public IActivityRepository ActivityRepository => _activityRepository.Value;
        public IUserRepository UserRepository => _userRepository.Value;
        public IAccessKeyRepository AccessKeyRepository => _accessKeyRepository.Value;

    }
}
