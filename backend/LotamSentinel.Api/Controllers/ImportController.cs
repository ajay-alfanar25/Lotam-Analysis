using LotamSentinel.Api.Dtos;
using LotamSentinel.Application.Ingestion;
using Microsoft.AspNetCore.Mvc;

namespace LotamSentinel.Api.Controllers;

public record ImportByPathRequest(string FilePath);

[ApiController]
[Route("api/import")]
public class ImportController : ControllerBase
{
    private readonly LotamIngestionService _ingestion;
    public ImportController(LotamIngestionService ingestion) => _ingestion = ingestion;

    /// <summary>Ingests a LOTAM PDF already present on the server's filesystem -
    /// the intended path for the two PDFs supplied for V1 (see README "How to
    /// import the two LOTAM PDFs").</summary>
    [HttpPost("path")]
    public async Task<ActionResult<ImportResultDto>> ImportByPath([FromBody] ImportByPathRequest request)
    {
        if (!System.IO.File.Exists(request.FilePath))
            return NotFound($"File not found on server: {request.FilePath}");

        var release = await _ingestion.IngestAsync(request.FilePath);
        return Ok(new ImportResultDto(release.Id, release.ProcessingStatus.ToString(), release.ProcessingError));
    }

    /// <summary>Ingests an uploaded LOTAM PDF (multipart/form-data).</summary>
    [HttpPost("upload")]
    [RequestSizeLimit(50_000_000)]
    public async Task<ActionResult<ImportResultDto>> Upload(IFormFile file)
    {
        if (file.Length == 0) return BadRequest("Empty file.");

        var isPdf = Path.GetExtension(file.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
            || file.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase);
        if (!isPdf) return BadRequest("Only PDF files are accepted for LOTAM import.");

        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_{file.FileName}");
        await using (var stream = System.IO.File.Create(tempPath))
        {
            await file.CopyToAsync(stream);
        }

        try
        {
            var release = await _ingestion.IngestAsync(tempPath);
            return Ok(new ImportResultDto(release.Id, release.ProcessingStatus.ToString(), release.ProcessingError));
        }
        finally
        {
            System.IO.File.Delete(tempPath);
        }
    }
}
