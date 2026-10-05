using LotamSentinel.Api.Dtos;
using LotamSentinel.Application.Comparison;
using Microsoft.AspNetCore.Mvc;

namespace LotamSentinel.Api.Controllers;

[ApiController]
[Route("api/comparisons")]
public class ComparisonsController : ControllerBase
{
    private readonly ComparisonService _comparisonService;
    public ComparisonsController(ComparisonService comparisonService) => _comparisonService = comparisonService;

    /// <summary>Runs (or re-runs, idempotently) the deterministic comparator for
    /// one explicit (previous, current) release pair. Re-running clears any
    /// prior ChangeRecord rows for that exact pair first.</summary>
    [HttpPost("run")]
    public async Task<ActionResult<RunComparisonResultDto>> Run([FromBody] RunComparisonRequest request)
    {
        var count = await _comparisonService.RunComparisonAsync(request.PreviousReleaseId, request.CurrentReleaseId);
        return Ok(new RunComparisonResultDto(count));
    }
}
