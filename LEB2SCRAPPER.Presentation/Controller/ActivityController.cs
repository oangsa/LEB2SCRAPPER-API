using LEB2SCRAPPER.Entity.Models.Response;
using LEB2SCRAPPER.Infrastructure.Contracts.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using LEB2SCRAPPER.Service.Contracts.Core;
using LEB2SCRAPPER.Entity.DataTransferModels.Activity;
using LEB2SCRAPPER.Entity.Models;
using LEB2SCRAPPER.Presentation.Filters;


namespace LEB2SCRAPPER.Presentation.Controller
{
    [Route("[controller]")]
    [ApiController]
    public class ActivityController : ControllerBase
    {
        private readonly IServiceManager _service;
        private readonly ILeb2SessionCredential _sessionCredential;

        public ActivityController(
            IServiceManager service,
            ILeb2SessionCredential sessionCredential)
        {
            _service = service;
            _sessionCredential = sessionCredential;
        }

        [HttpPost]
        [Authorize]
        [ValidateModel] // Custom validation filter
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status502BadGateway)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> POST(
            [FromBody] ActivityDto activityDto,
            CancellationToken cancellationToken)
        {
            var activities = await _service.ActivityService.GetActivitiesAsync(
                activityDto.UserId,
                activityDto.ClassId,
                _sessionCredential.Value!,
                cancellationToken);

            return Ok(activities ?? new ());
        }

    }
}
