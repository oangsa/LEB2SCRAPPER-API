using LEB2SCRAPPER.Entity.Models.Users;
using LEB2SCRAPPER.Entity.Models.Authentication;
using LEB2SCRAPPER.Entity.Models.Response;
using LEB2SCRAPPER.Contracts.Repository;
using LEB2SCRAPPER.Infrastructure.Contracts.HttpService;
using LEB2SCRAPPER.Infrastructure.Contracts.Outbound;
using LEB2SCRAPPER.Entity.Exceptions.Leb2Integration;
using LEB2SCRAPPER.Entity.Exceptions.UserCustomException;


namespace LEB2SCRAPPER.Repository.Master;

public class UserRepository : IUserRepository
{
    private readonly IHttpService _httpService;
    private readonly IClientFingerprintProvider _clientFingerprintProvider;
    private static readonly string BaseUrl = "https://leb2-mcs-api-production.leb2.org/public/login/v1/login";

    public UserRepository(
        IHttpService httpService,
        IClientFingerprintProvider clientFingerprintProvider)
    {
        _httpService = httpService;
        _clientFingerprintProvider = clientFingerprintProvider;
    }

    public async Task<User?> GetUserByCredentialsAsync(
        Credentials credentials,
        CancellationToken cancellationToken = default)
    {
        var context = new OutboundRequestContext(
            Leb2OutboundEndpoints.UserLogin,
            _clientFingerprintProvider.CreateForUsername(credentials.Username));
        var response = await _httpService.PostAsync<LoginResponse>(
            BaseUrl,
            credentials,
            context,
            cancellationToken: cancellationToken);

        if (!response.Success)
        {
            throw new UserNotFoundException("Invalid credentials or user not found.");
        }

        if (response.Result is null
            || response.Result.Id <= 0
            || string.IsNullOrWhiteSpace(response.Result.StudentId))
        {
            throw new StructuralParseException(
                "user-login.result",
                "LEB2 returned an incomplete successful login result.");
        }

        User user = new()
        {
            Id = response.Result.Id,
            KmuttId = response.Result.StudentId,
            NameThai = response.Result.FirstnameTh,
            NameEnglish = response.Result.FirstnameEn,
            SurnameThai = response.Result.LastnameTh,
            SurnameEnglish = response.Result.LastnameEn,
        };

        return user;
    }
}
