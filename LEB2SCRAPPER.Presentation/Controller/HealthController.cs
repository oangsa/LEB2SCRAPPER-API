using LEB2SCRAPPER.Entity.Models.Response;
using LEB2SCRAPPER.Infrastructure.Contracts.Outbound;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LEB2SCRAPPER.Presentation.Controller;

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
            Status = endpoints.Any(endpoint => endpoint.Status == "unavailable")
                ? "degraded"
                : "healthy",
            Endpoints = endpoints
        });
    }
}
