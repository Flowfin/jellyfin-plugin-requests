using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Xunit;

using PluginUnderTest = global::Jellyfin.Plugin.Requests.Plugin;

namespace Jellyfin.Plugin.Requests.Tests;

/// <summary>
/// The packaging metadata against the assembly it packages. Both files are copied next to the suite
/// by the test project, so what is read here is the file the release path reads rather than a second
/// copy of the number.
/// </summary>
public class PackagingMetadataTests
{
    /// <summary>
    /// The version is set once, in Directory.Build.props, and each packaging file repeats it. A
    /// repeat is a thing that can be forgotten: the assembly says one number, the catalogue entry
    /// claims another, and the server reports the first while the update check reads the second.
    /// Nothing in a build refuses that, because neither file is compiled.
    /// </summary>
    /// <param name="packagingFile">The packaging metadata file to read.</param>
    [Theory]
    [InlineData("build.yaml")]
    [InlineData("build-jf12.yaml")]
    public void PluginVersionMatchesThePackagingMetadata(string packagingFile)
    {
        var assemblyVersion = typeof(PluginUnderTest).Assembly.GetName().Version;

        Assert.NotNull(assemblyVersion);
        Assert.Equal(
            assemblyVersion!.ToString(),
            ReadScalar(packagingFile, "version"),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// A catalogue holds one entry per plugin, so the identity fields have to be word-identical
    /// across the packages. If they diverge, the entry flips between two texts depending on which
    /// package was published last, and a server that moves between the two server lines can end up
    /// treating this as a second, unrelated plugin.
    /// </summary>
    /// <param name="field">The identity field both packaging files must agree on.</param>
    [Theory]
    [InlineData("name")]
    [InlineData("guid")]
    [InlineData("version")]
    public void BothPackagesClaimTheSameIdentity(string field)
    {
        Assert.Equal(
            ReadScalar("build.yaml", field),
            ReadScalar("build-jf12.yaml", field),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Reads one top-level scalar out of a packaging file. Deliberately not a YAML parser: the two
    /// files this reads are flat lists of scalars, and a parser would be a dependency added to the
    /// suite for four fields.
    /// </summary>
    /// <param name="packagingFile">The file, as copied next to the suite.</param>
    /// <param name="key">The key at column zero.</param>
    /// <returns>The value, with surrounding quotation marks removed.</returns>
    private static string ReadScalar(string packagingFile, string key)
    {
        var path = Path.Combine(AppContext.BaseDirectory, packagingFile);
        Assert.True(File.Exists(path), FormattableString.Invariant($"{path} was not copied next to the suite."));

        var prefix = key + ":";
        var found = new List<string>();

        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith(prefix, StringComparison.Ordinal))
            {
                found.Add(line.Substring(prefix.Length).Trim().Trim('"'));
            }
        }

        // Exactly one, so a key that moved or was written twice fails here rather than quietly
        // handing back the first of two disagreeing values.
        return Assert.Single(found);
    }
}
