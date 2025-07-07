using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using LEB2SCRAPPER.Service.Contracts.Core;
using LEB2SCRAPPER.Entity.DataTransferModels.Activity;
using LEB2SCRAPPER.Entity.Models;
using LEB2SCRAPPER.Presentation.Filters;
using LEB2SCRAPPER.Entity.ValidationAttributes;


namespace LEB2SCRAPPER.Presentation.Controller
{
    [Route("[controller]")]
    [ApiController]
    public class ActivityController : ControllerBase
    {
        private readonly IServiceManager _service;
        public ActivityController(IServiceManager service) => _service = service;

        [HttpPost]
        [ValidateModel] // Custom validation filter
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> POST([FromBody] ActivityDto activityDto, [FromHeader(Name = "Authorization")][RequiredHeader] string authorization)
        {
            var activities = await _service.ActivityService.GetActivitiesAsync(activityDto.UserId, activityDto.ClassId, authorization);

            return Ok(activities ?? new ());
        }

    }
}
