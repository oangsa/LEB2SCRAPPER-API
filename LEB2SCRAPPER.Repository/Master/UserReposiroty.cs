using LEB2SCRAPPER.Entity.Models.Users;
using LEB2SCRAPPER.Entity.Models.Authentication;
using LEB2SCRAPPER.Entity.Models.Response;
using LEB2SCRAPPER.Contracts.Repository;
using LEB2SCRAPPER.Infrastructure.Contracts.HttpService;
using LEB2SCRAPPER.Infrastructure.HttpService;
using LEB2SCRAPPER.Entity.Exceptions.UserCustomException;


namespace LEB2SCRAPPER.Repository.Master;

public class UserRepository : IUserRepository
{
    private readonly IHttpService _httpService;
    private static readonly string BaseUrl = "https://leb2-mcs-api-production.leb2.org/public/login/v1/login";

    public UserRepository()
    {
        _httpService = new HttpService();
    }

    public async Task<User?> GetUserByCredentialsAsync(Credentials credentials)
    {
        var response = await _httpService.PostAsync<LoginResponse>(BaseUrl, credentials);

        if (!response.Success || response.Result == null)
            throw new UserNotFoundException("Invalid credentials or user not found.");

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
