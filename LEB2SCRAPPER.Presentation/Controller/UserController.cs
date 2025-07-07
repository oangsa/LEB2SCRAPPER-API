using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using LEB2SCRAPPER.Service.Contracts.Core;
using LEB2SCRAPPER.Entity.Models.Authentication;


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
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Login([FromBody] Credentials credentials)
        {
            if (credentials == null)
            {
                return BadRequest("Invalid credentials.");
            }

            var user = await _service.UserService.GetUserByCredentialsAsync(credentials);
            if (user == null)
            {
                return NotFound("User not found.");
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
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetCookie([FromBody] Credentials credentials)
        {
            if (credentials == null)
            {
                return BadRequest("Invalid credentials.");
            }

            var cookie = await _service.UserService.GetCookieAsync(credentials);
            if (string.IsNullOrEmpty(cookie))
            {
                return NotFound("Cookie not found.");
            }

            return Ok(new { Cookie = cookie });
        }
    }
}
