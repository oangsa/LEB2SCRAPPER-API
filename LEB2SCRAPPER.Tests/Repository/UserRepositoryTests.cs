using LEB2SCRAPPER.Entity.Exceptions.Leb2Integration;
using LEB2SCRAPPER.Entity.Exceptions.UserCustomException;
using LEB2SCRAPPER.Entity.Models.Authentication;
using LEB2SCRAPPER.Entity.Models.Response;
using LEB2SCRAPPER.Infrastructure.Contracts.HttpService;
using LEB2SCRAPPER.Infrastructure.Contracts.Outbound;
using LEB2SCRAPPER.Repository.Master;

namespace LEB2SCRAPPER.Tests.Repository;

public class UserRepositoryTests
{
    [Fact]
    public async Task SuccessfulLoginWithIncompleteResult_ThrowsStructuralFailure()
    {
        var httpService = new StubHttpService(new LoginResponse
        {
            Success = true,
            Result = new Result()
        });
        var repository = new UserRepository(
            httpService,
            new StubFingerprintProvider());

        var exception = await Assert.ThrowsAsync<StructuralParseException>(() =>
            repository.GetUserByCredentialsAsync(CreateCredentials()));

        Assert.Equal("user-login.result", exception.FailureShape);
    }

    [Fact]
    public async Task UnsuccessfulLogin_RemainsAnInvalidCredentialResult()
    {
        var httpService = new StubHttpService(new LoginResponse
        {
            Success = false
        });
        var repository = new UserRepository(
            httpService,
            new StubFingerprintProvider());

        await Assert.ThrowsAsync<UserNotFoundException>(() =>
            repository.GetUserByCredentialsAsync(CreateCredentials()));
    }

    private static Credentials CreateCredentials()
    {
        return new Credentials
        {
            Username = "fake-user",
            Password = "fake-password"
        };
    }

    private sealed class StubHttpService : IHttpService
    {
        private readonly LoginResponse _response;

        public StubHttpService(LoginResponse response)
        {
            _response = response;
        }

        public Task<T> GetAsync<T>(
            string url,
            OutboundRequestContext context,
            Dictionary<string, string>? headers = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<T> PostAsync<T>(
            string url,
            object? body,
            OutboundRequestContext context,
            Dictionary<string, string>? headers = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult((T)(object)_response);
        }
    }

    private sealed class StubFingerprintProvider : IClientFingerprintProvider
    {
        public string CreateForSession(string sessionValue)
        {
            return "session-client";
        }

        public string CreateForUsername(string username)
        {
            return "username-client";
        }
    }
}
