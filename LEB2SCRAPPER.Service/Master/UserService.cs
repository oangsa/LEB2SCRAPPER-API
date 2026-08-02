using LEB2SCRAPPER.Contracts.Repository.Core;
using LEB2SCRAPPER.Entity.Models.AccessKey;
using LEB2SCRAPPER.Entity.Models.Authentication;
using LEB2SCRAPPER.Entity.Models.Users;
using LEB2SCRAPPER.Infrastructure.Contracts.AccessKey;
using LEB2SCRAPPER.Service.Contracts.Master;


namespace LEB2SCRAPPER.Service.Master;

public class UserService(
    ICoreAdapterManager coreAdapterManager,
    IAccessKeyService accessKeyService,
    DeviceBindingRequestContext? deviceBindingRequestContext = null) : IUserService
{
    private readonly IRepositoryManager _repositoryManager = coreAdapterManager.RepositoryManager;
    private readonly DeviceBindingRequestContext? _deviceBindingRequestContext =
        deviceBindingRequestContext;

    public async Task<string?> GetCookieAsync(
        Credentials credentials,
        AccessKeyState accessKeyState,
        CancellationToken cancellationToken = default)
    {
        accessKeyService.EnsureStudentIdentity(
            accessKeyState,
            credentials.Username);
        accessKeyService.EnsureLeb2IdentityInitialized(accessKeyState);

        var cookie = await _repositoryManager.ScrapingRepository.GetCookieAsync(
            credentials,
            cancellationToken);

        return cookie;
    }

    public async Task<User?> GetUserByCredentialsAsync(
        Credentials credentials,
        AccessKeyState accessKeyState,
        CancellationToken cancellationToken = default)
    {
        accessKeyService.EnsureStudentIdentity(
            accessKeyState,
            credentials.Username);

        var user = await _repositoryManager.UserRepository.GetUserByCredentialsAsync(
            credentials,
            cancellationToken);

        if (user is not null)
        {
            var name = string.Join(
                ' ',
                new[]
                {
                    user.NameEnglish,
                    user.SurnameEnglish
                }
                .Where(value => !string.IsNullOrWhiteSpace(value)));

            if (string.IsNullOrWhiteSpace(name))
            {
                name = string.Join(
                    ' ',
                    new[]
                    {
                        user.NameThai,
                        user.SurnameThai
                }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            }

            var deviceBinding = accessKeyService.PrepareDeviceBindingForLogin(
                accessKeyState,
                _deviceBindingRequestContext?.Current);

            if (deviceBinding is null)
            {
                await accessKeyService.RegisterSuccessfulLoginAsync(
                    accessKeyState.KeyId,
                    credentials.Username.Trim(),
                    user.Id,
                    name,
                    cancellationToken);
            }
            else
            {
                await accessKeyService.RegisterSuccessfulLoginWithDeviceAsync(
                    accessKeyState.KeyId,
                    credentials.Username.Trim(),
                    user.Id,
                    name,
                    deviceBinding,
                    cancellationToken);
            }
        }

        return user;
    }
}
