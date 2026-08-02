using LEB2SCRAPPER.Entity.Models.Response;
using LEB2SCRAPPER.Infrastructure.Contracts.Outbound;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;

namespace LEB2SCRAPPER.Presentation.Controller;

[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/health/leb2")]
[Route("health/leb2")]
[ApiController]
public class HealthController : ControllerBase
{
    private readonly IOutboundRequestStatusReader _statusReader;

    public HealthController(IOutboundRequestStatusReader statusReader)
    {
        _statusReader = statusReader;
    }

    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Leb2HealthResponse), StatusCodes.Status200OK)]
    public IActionResult Get()
    {
        var snapshot = _statusReader.GetSnapshot();
        var endpoints = snapshot.Endpoints
            .Select(endpoint => new Leb2EndpointHealthResponse
            {
                Name = endpoint.Name,
                Status = endpoint.RetryAt.HasValue
                    ? "unavailable"
                    : "available",
                RetryAt = endpoint.RetryAt?.UtcDateTime,
                RetryAfterSeconds = endpoint.RetryAfterSeconds
            })
            .ToList();

        Response.Headers["Cache-Control"] = "no-store";

        return Ok(new Leb2HealthResponse
        {
            ObservedAt = snapshot.ObservedAt.UtcDateTime,
            Source = "local-observed-state",
            Status = endpoints.Any(endpoint => endpoint.Status == "unavailable")
                ? "degraded"
                : "healthy",
            Endpoints = endpoints
        });
    }
}
