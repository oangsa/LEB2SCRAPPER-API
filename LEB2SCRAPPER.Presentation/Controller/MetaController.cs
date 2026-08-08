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
    private readonly ILatestClientVersionProvider _latestClientVersionProvider;

    public MetaController(
        ClientCompatibilityOptions options,
        ILatestClientVersionProvider latestClientVersionProvider)
    {
        _options = options;
        _latestClientVersionProvider = latestClientVersionProvider;
    }

    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiMetadataResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var latestClientVersion =
            await _latestClientVersionProvider.GetLatestVersionAsync(cancellationToken)
            ?? _options.LatestClientVersion;

        return Ok(new ApiMetadataResponse
        {
            ApiVersion = 1,
            MinimumClientVersion = _options.MinimumClientVersion,
            LatestClientVersion = latestClientVersion,
            DownloadUrl = _options.DownloadUrl
        });
    }
}
