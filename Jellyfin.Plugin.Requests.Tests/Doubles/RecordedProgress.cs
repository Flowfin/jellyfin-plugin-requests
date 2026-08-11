using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Requests.Tests.Doubles;

/// <summary>
/// The progress a scheduled task reports, as a list a test can read. The server shows this on its
/// task page, so a run that reports nothing looks stuck to whoever is watching it.
/// </summary>
internal sealed class RecordedProgress : IProgress<double>
{
    private readonly List<double> _reported = [];

    /// <summary>
    /// Gets everything reported, in order.
    /// </summary>
    public IReadOnlyList<double> Reported => _reported;

    /// <inheritdoc />
    public void Report(double value) => _reported.Add(value);
}
