using LEB2SCRAPPER.Entity.Models.Response;
using LEB2SCRAPPER.Presentation.Filters;
using LEB2SCRAPPER.Infrastructure.Contracts.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using LEB2SCRAPPER.Service.Contracts.Core;
using LEB2SCRAPPER.Entity.Models.Class;


namespace LEB2SCRAPPER.Presentation.Controller
{
    [Route("[controller]")]
    [ApiController]
    public class ClassController : ControllerBase
    {
        private readonly IServiceManager _service;
        private readonly ILeb2SessionCredential _sessionCredential;

        public ClassController(
            IServiceManager service,
            ILeb2SessionCredential sessionCredential)
        {
            _service = service;
            _sessionCredential = sessionCredential;
        }

        [HttpGet("{id:int}")]
        [AccessKeyAuthorize(AccessKeyRequirement.Activated)]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status502BadGateway)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GET(int id, CancellationToken cancellationToken)
        {
            var classes = await _service.ClassService.GetClassesAsync(
                id,
                _sessionCredential.Value!,
                cancellationToken);

            return Ok(classes ?? new List<ClassInfo>());
        }

    }
}
