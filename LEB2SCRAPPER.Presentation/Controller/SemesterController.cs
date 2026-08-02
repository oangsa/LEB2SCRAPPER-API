using LEB2SCRAPPER.Entity.Models.Response;
using LEB2SCRAPPER.Entity.Models.Semester;
using LEB2SCRAPPER.Presentation.Filters;
using LEB2SCRAPPER.Infrastructure.Contracts.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using LEB2SCRAPPER.Service.Contracts.Core;


namespace LEB2SCRAPPER.Presentation.Controller
{
    [Route("[controller]")]
    [ApiController]
    public class SemesterController : ControllerBase
    {
        private readonly IServiceManager _service;
        private readonly ILeb2SessionCredential _sessionCredential;

        public SemesterController(
            IServiceManager service,
            ILeb2SessionCredential sessionCredential)
        {
            _service = service;
            _sessionCredential = sessionCredential;
        }

        [HttpGet]
        [AccessKeyAuthorize(AccessKeyRequirement.Activated)]
        [Authorize]
        [ProducesResponseType(typeof(List<SemesterInfo>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status502BadGateway)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GET(CancellationToken cancellationToken)
        {
            var semesters = await _service.SemesterService.GetSemestersAsync(
                _sessionCredential.Value!,
                cancellationToken);

            return Ok(semesters ?? new List<SemesterInfo>());
        }

    }
}
