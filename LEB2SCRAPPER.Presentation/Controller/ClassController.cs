using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using LEB2SCRAPPER.Service.Contracts.Core;
using LEB2SCRAPPER.Entity.Models.Class;


namespace LEB2SCRAPPER.Presentation.Controller
{
    [Route("[controller]")]
    [ApiController]
    public class ClassController : ControllerBase
    {
        private readonly IServiceManager _service;
        public ClassController(IServiceManager service) => _service = service;

        [HttpGet("{id:int}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GET([FromHeader(Name = "Authorization")] string authorization, int id)
        {
            if (string.IsNullOrEmpty(authorization))
            {
                return BadRequest("Authorization header is required.");
            }

            var classes = await _service.ClassService.GetClassesAsync(id, authorization);
            return Ok(classes ?? new List<ClassInfo>());
        }

    }
}
