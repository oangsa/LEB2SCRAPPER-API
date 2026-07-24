using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using LEB2SCRAPPER.Service.Contracts.Core;
using LEB2SCRAPPER.Entity.Models.Authentication;
using LEB2SCRAPPER.Entity.Models.Response;


namespace LEB2SCRAPPER.Presentation.Controller
{
    [Route("[controller]")]
    [ApiController]
    public class UserController : ControllerBase
    {
        private readonly IServiceManager _service;
        public UserController(IServiceManager service) => _service = service;

        [HttpPost("login")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Login(
            [FromBody] Credentials credentials,
            CancellationToken cancellationToken)
        {
            var user = await _service.UserService.GetUserByCredentialsAsync(
                credentials,
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
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationErrorResponse), StatusCodes.Status400BadRequest)]
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
    }
}
