using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Asp.Versioning;
using LEB2SCRAPPER.Presentation.Filters;
using LEB2SCRAPPER.Service.Contracts.Core;
using LEB2SCRAPPER.Entity.Models.Authentication;
using LEB2SCRAPPER.Entity.Models.Response;
using LEB2SCRAPPER.Infrastructure.Contracts.AccessKey;


namespace LEB2SCRAPPER.Presentation.Controller
{
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    [Route("[controller]")]
    [ApiController]
    public class UserController : ControllerBase
    {
        private readonly IServiceManager _service;
        private readonly AccessKeyRequestContext _accessKeyContext;
        private readonly DeviceBindingRequestContext _deviceBindingContext;

        public UserController(
            IServiceManager service,
            AccessKeyRequestContext accessKeyContext,
            DeviceBindingRequestContext deviceBindingContext)
        {
            _service = service;
            _accessKeyContext = accessKeyContext;
            _deviceBindingContext = deviceBindingContext;
        }

        [HttpPost("login")]
        [AccessKeyAuthorize(AccessKeyRequirement.Provisioned)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Login(
            [FromBody] Credentials credentials,
            CancellationToken cancellationToken)
        {
            var user = await _service.UserService.GetUserByCredentialsAsync(
                credentials,
                _accessKeyContext.Current!,
                cancellationToken);

            if (user == null)
            {
                return NotFound(new ErrorResponse
                {
                    Message = "User not found.",
                    ResponseCode = ApiErrorCodes.ResourceNotFound,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            return Ok(new
            {
                user.Id,
                user.KmuttId,
                user.NameThai,
                user.NameEnglish,
                user.SurnameThai,
                user.SurnameEnglish
            });
        }

        [HttpPost("cookie")]
        [AccessKeyAuthorize(AccessKeyRequirement.Activated)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status502BadGateway)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetCookie(
            [FromBody] Credentials credentials,
            CancellationToken cancellationToken)
        {
            var cookie = await _service.UserService.GetCookieAsync(
                credentials,
                _accessKeyContext.Current!,
                cancellationToken);

            if (string.IsNullOrEmpty(cookie))
            {
                return NotFound(new ErrorResponse
                {
                    Message = "Cookie not found.",
                    ResponseCode = ApiErrorCodes.ResourceNotFound,
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            return Ok(new { Cookie = cookie });
        }

        [HttpPost("logout")]
        [AccessKeyAuthorize(AccessKeyRequirement.Activated)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> Logout(CancellationToken cancellationToken)
        {
            await _service.AccessKeyService.LogoutAsync(
                _accessKeyContext.Current!,
                _deviceBindingContext.Current,
                cancellationToken);

            return NoContent();
        }
    }
}
