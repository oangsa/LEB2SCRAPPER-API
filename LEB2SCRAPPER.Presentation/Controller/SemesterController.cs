using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using LEB2SCRAPPER.Service.Contracts.Core;


namespace LEB2SCRAPPER.Presentation.Controller
{
    [Route("[controller]")]
    [ApiController]
    public class SemesterController : ControllerBase
    {
        private readonly IServiceManager _service;
        public SemesterController(IServiceManager service) => _service = service;

        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GET([FromHeader(Name = "Authorization")] string authorization)
        {
            if (string.IsNullOrEmpty(authorization))
            {
                return BadRequest("Authorization header is required.");
            }

            var semesters = await _service.SemesterService.GetSemestersAsync(authorization);
            return Ok(semesters ?? new List<int>());
        }

    }
}
