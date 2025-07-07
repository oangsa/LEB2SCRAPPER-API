using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using LEB2SCRAPPER.Service.Contracts.Core;
using LEB2SCRAPPER.Entity.DataTransferModels.Activity;
using LEB2SCRAPPER.Entity.Models;


namespace LEB2SCRAPPER.Presentation.Controller
{
    [Route("[controller]")]
    [ApiController]
    public class ActivityController : ControllerBase
    {
        private readonly IServiceManager _service;
        public ActivityController(IServiceManager service) => _service = service;

        [HttpPost]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> POST([FromBody] ActivityDto activityDto, [FromHeader(Name = "Authorization")] string authorization)
        {
            if (activityDto == null || activityDto.UserId <= 0 || activityDto.ClassId <= 0)
            {
                return BadRequest("Invalid activity data.");
            }

            var activities = await _service.ActivityService.GetActivitiesAsync(activityDto.UserId, activityDto.ClassId, authorization);

            return Ok(activities ?? new ());
        }

    }
}
