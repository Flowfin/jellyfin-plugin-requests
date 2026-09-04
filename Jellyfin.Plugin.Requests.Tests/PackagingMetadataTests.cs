using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
    ///
    /// These three are named here because they are the ones this file has always compared, and
    /// <c>version</c> is not an identity field at all - it is per-version, and it is compared
    /// because both packages are cut from one number. The fields a CATALOGUE holds once are read
    /// out of the generator by <see cref="BothPackagesAgreeOnEveryFieldTheGeneratorHoldsOnce"/>,
    /// which is the wider guard; this one stays because it names <c>version</c> and that one may
    /// not.
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
    /// Every field the manifest generator holds once per plugin, compared across the two packaging
    /// files, with the list of fields read out of the generator instead of written here.
    ///
    /// WHAT THIS CATCHES THAT THE THEORY ABOVE DOES NOT. <c>scripts/build-manifest.sh</c> refuses
    /// two packages that disagree on any member of its <c>IDENTITY</c> tuple, and that tuple has
    /// seven members. The theory above compares two of them. A per-line wording in
    /// <c>description</c>, a different <c>owner</c>, a moved <c>imageUrl</c>: each one passed every
    /// check on this board and was refused inside the release, after the tag existed and after both
    /// packages were built. Those two files carry a paragraph of prose each, so the edit that trips
    /// it is the commonest edit they get.
    ///
    /// WHY THE LIST IS READ RATHER THAN RESTATED. A second copy of it drifts against the first, and
    /// the drift is silent in the direction that matters: a field added to the generator is a field
    /// this suite would go on not comparing. So the tuple is parsed out of the script, and a script
    /// whose declaration this cannot find fails here rather than quietly comparing nothing.
    ///
    /// <c>description</c> is why the reader below handles a folded block. Both files write that
    /// field as <c>description: &gt;</c> with the text on the lines beneath, so a reader that took
    /// the rest of the key's own line would compare <c>&gt;</c> with <c>&gt;</c> and pass whatever
    /// the paragraphs said.
    /// </summary>
    [Fact]
    public void BothPackagesAgreeOnEveryFieldTheGeneratorHoldsOnce()
    {
        var fields = IdentityFieldsTheGeneratorDeclares();

        // A guard that compares an empty set passes for the wrong reason, and the reason is
        // invisible. The generator declares seven today; the number is not asserted, only that the
        // reading found something to compare.
        Assert.NotEmpty(fields);

        foreach (var field in fields)
        {
            var fromDefaultLine = ReadValue("build.yaml", field);
            var fromJf12 = ReadValue("build-jf12.yaml", field);

            Assert.True(
                string.Equals(fromDefaultLine, fromJf12, StringComparison.Ordinal),
                FormattableString.Invariant(
                    $"build.yaml and build-jf12.yaml disagree on '{field}', which scripts/build-manifest.sh holds once per plugin and refuses a divergence in. build.yaml says '{fromDefaultLine}' and build-jf12.yaml says '{fromJf12}'."));
        }
    }

    /// <summary>
    /// The generator's own list of the fields a catalogue holds once per plugin, read out of the
    /// line that declares it.
    /// </summary>
    /// <returns>The field names, in the order the script writes them.</returns>
    private static List<string> IdentityFieldsTheGeneratorDeclares()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "scripts", "build-manifest.sh");
        Assert.True(File.Exists(path), FormattableString.Invariant($"{path} was not copied next to the suite."));

        var declaration = File.ReadLines(path)
            .FirstOrDefault(line => line.StartsWith("IDENTITY = (", StringComparison.Ordinal));

        // Not found is a failure rather than an empty list. If the declaration is renamed or
        // reformatted, this check has stopped reading the generator, and it says so instead of
        // passing over nothing.
        Assert.False(
            declaration is null,
            FormattableString.Invariant($"{path} carries no line beginning 'IDENTITY = (', so the fields a catalogue holds once could not be read from the generator."));

        var open = declaration!.IndexOf('(', StringComparison.Ordinal);
        var close = declaration.LastIndexOf(')');
        Assert.True(close > open, FormattableString.Invariant($"the IDENTITY declaration in {path} is not one parenthesised line: {declaration}"));

        return declaration.Substring(open + 1, close - open - 1)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(entry => entry.Trim('"'))
            .Where(entry => entry.Length > 0)
            .ToList();
    }

    /// <summary>
    /// Reads one top-level field out of a packaging file, whether it is written on the key's own
    /// line or as a folded block beneath it.
    /// </summary>
    /// <param name="packagingFile">The file, as copied next to the suite.</param>
    /// <param name="key">The key at column zero.</param>
    /// <returns>The value: the rest of the line unquoted, or the block's lines joined by a space.</returns>
    private static string ReadValue(string packagingFile, string key)
    {
        var path = Path.Combine(AppContext.BaseDirectory, packagingFile);
        Assert.True(File.Exists(path), FormattableString.Invariant($"{path} was not copied next to the suite."));

        var lines = File.ReadAllLines(path);
        var prefix = key + ":";
        var found = new List<string>();

        for (var index = 0; index < lines.Length; index++)
        {
            if (!lines[index].StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var rest = lines[index].Substring(prefix.Length).Trim();
            if (rest != ">" && rest != "|" && rest != ">-" && rest != "|-")
            {
                found.Add(rest.Trim('"'));
                continue;
            }

            // A folded or literal block: every line beneath it that is indented or blank belongs to
            // the value, and the first line at column zero is the next key. Blank lines are kept as
            // nothing rather than dropped from the middle, because two paragraphs that differ only
            // by where they are split are two different texts to the generator.
            var block = new List<string>();
            for (var below = index + 1; below < lines.Length; below++)
            {
                var line = lines[below];
                if (line.Length > 0 && !char.IsWhiteSpace(line[0]))
                {
                    break;
                }

                block.Add(line.Trim());
            }

            found.Add(string.Join(" ", block).Trim());
        }

        // Exactly one, for the reason ReadScalar gives: a key that moved or was written twice fails
        // here rather than handing back the first of two disagreeing values.
        return Assert.Single(found);
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
