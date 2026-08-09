using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Requests.Api;

/// <summary>
/// A deliberate defect, planted so the C# leg of the code scan can be watched reporting one. This
/// branch is not for merge and this file exists on no other branch.
/// <para>
/// Both shapes take a value straight off an HTTP request and hand it to a sink. That is the shape
/// the earlier attempts on #24 could not produce, because this plugin had no controller, no request
/// handler and no external caller for a query to find a source in.
/// </para>
/// </summary>
[SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "Planted on a proof branch that is not for merge.")]
[SuppressMessage("Security", "CA3006:Review code for process command injection vulnerabilities", Justification = "Planted on a proof branch that is not for merge.")]
public sealed class ScanProofController : RequestsControllerBase
{
    /// <summary>
    /// Reads whatever path the caller names.
    /// </summary>
    /// <param name="name">Straight off the request.</param>
    /// <returns>The file.</returns>
    [HttpGet("ScanProofRead")]
    public IActionResult Read([FromQuery] string name)
        => Ok(System.IO.File.ReadAllText(Path.Combine("/var/lib/jellyfin", name)));

    /// <summary>
    /// Runs whatever the caller names.
    /// </summary>
    /// <param name="name">Straight off the request.</param>
    /// <returns>Nothing.</returns>
    [HttpGet("ScanProofRun")]
    public IActionResult Run([FromQuery] string name)
    {
        using var started = Process.Start("/bin/sh", "-c \"" + name + "\"");
        return Ok();
    }
}

// A second line, so this branch differs from its base and a pull request can exist.
