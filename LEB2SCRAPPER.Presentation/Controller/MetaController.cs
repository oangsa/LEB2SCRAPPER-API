using Asp.Versioning;
using LEB2SCRAPPER.Entity.Models.Response;
using LEB2SCRAPPER.Infrastructure.Contracts.Compatibility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LEB2SCRAPPER.Presentation.Controller;

[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/meta")]
[ApiController]
public sealed class MetaController : ControllerBase
{
    private readonly ClientCompatibilityOptions _options;

    public MetaController(ClientCompatibilityOptions options)
    {
        _options = options;
    }

    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiMetadataResponse), StatusCodes.Status200OK)]
    public IActionResult Get()
    {
        return Ok(new ApiMetadataResponse
        {
            ApiVersion = 1,
            MinimumClientVersion = _options.MinimumClientVersion,
            LatestClientVersion = _options.LatestClientVersion,
            DownloadUrl = _options.DownloadUrl
        });
    }
}
