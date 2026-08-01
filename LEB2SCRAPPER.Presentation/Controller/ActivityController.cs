using System.ComponentModel.DataAnnotations;
using LEB2SCRAPPER.Presentation.Filters;
using LEB2SCRAPPER.Entity.Models.Activity;
using LEB2SCRAPPER.Entity.Models.Response;
using LEB2SCRAPPER.Infrastructure.Contracts.Authentication;
using LEB2SCRAPPER.Service.Contracts.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace LEB2SCRAPPER.Presentation.Controller;

[Route("[controller]")]
[ApiController]
[AccessKeyAuthorize(AccessKeyRequirement.Activated)]
public class ActivityController : ControllerBase
{
    private const string UserIdHeaderName = "X-LEB2-USER-ID";
    private readonly IServiceManager _service;
    private readonly ILeb2SessionCredential _sessionCredential;

    public ActivityController(
        IServiceManager service,
        ILeb2SessionCredential sessionCredential)
    {
        _service = service;
        _sessionCredential = sessionCredential;
    }

    [HttpGet("{semesterId:int}/{classId:int}")]
    [Authorize]
    [ProducesResponseType(typeof(List<Activity>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetByClass(
        [FromRoute]
        [Range(1, int.MaxValue)]
        int semesterId,
        [FromRoute]
        [Range(1, int.MaxValue)]
        int classId,
        [FromHeader(Name = UserIdHeaderName)]
        [BindRequired]
        [Range(1, int.MaxValue)]
        int userId,
        CancellationToken cancellationToken)
    {
        var activities = await _service.ActivityService.GetActivitiesAsync(
            userId,
            semesterId,
            classId,
            _sessionCredential.Value!,
            cancellationToken);

        return Ok(activities ?? new List<Activity>());
    }

    [HttpGet("{semesterId:int}")]
    [Authorize]
    [ProducesResponseType(typeof(List<Activity>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetBySemester(
        [FromRoute]
        [Range(1, int.MaxValue)]
        int semesterId,
        [FromHeader(Name = UserIdHeaderName)]
        [BindRequired]
        [Range(1, int.MaxValue)]
        int userId,
        CancellationToken cancellationToken)
    {
        var activities = await _service.ActivityService.GetActivitiesBySemesterAsync(
            userId,
            semesterId,
            _sessionCredential.Value!,
            cancellationToken);

        return Ok(activities);
    }

    [HttpGet("{semesterId:int}/snapshot")]
    [Authorize]
    [ProducesResponseType(typeof(SemesterSnapshotResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetSemesterSnapshot(
        [FromRoute]
        [Range(1, int.MaxValue)]
        int semesterId,
        [FromHeader(Name = UserIdHeaderName)]
        [BindRequired]
        [Range(1, int.MaxValue)]
        int userId,
        CancellationToken cancellationToken)
    {
        var snapshot = await _service.ActivityService.GetSemesterSnapshotAsync(
            userId,
            semesterId,
            _sessionCredential.Value!,
            cancellationToken);

        return Ok(snapshot);
    }
}
