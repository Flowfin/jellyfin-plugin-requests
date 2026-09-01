// The assembly reference table of one file, printed and never judged here.
//
// WHAT IT IS FOR. A plugin package claims a `targetAbi`, and the assembly inside it carries the
// versions of the assemblies it was compiled against. Those two are independent: the packaging
// metadata is edited by hand and the reference table is stamped by the compiler from whatever SDK
// the build resolved. Where the table sits above the claim, the reference does not bind on a server
// of the claimed floor - the assembly loads, GetTypes() throws, and the server reports the plugin
// NotSupported. `0.2.0.0` shipped in exactly that state, which is the measurement on #152.
//
// THE COMPARISON IS NOT HERE. This prints `name<TAB>version`, one reference per line, and exits
// zero. Which names matter and which versions are too high is `scripts/check-package-abi.sh`, so
// the rule lives in one place that a proof harness can drive over fixtures, and this stays a reader
// with nothing to get wrong.
//
// A FILE IT CANNOT READ IS AN ERROR RATHER THAN AN EMPTY LIST. An unreadable assembly and an
// assembly that references nothing print the same nothing, and the caller has to be able to tell
// them apart: a package whose reference set could not be read must be refused, not passed.
//
// usage: dotnet run --project tools/package-abi -- <assembly>
using System;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Jellyfin.Plugin.Requests.PackageAbi
{
    internal static class Program
    {
        internal static int Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.Error.WriteLine("package-abi: one argument, the assembly to read.");
                return 2;
            }

            var path = args[0];
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"package-abi: {path} does not exist.");
                return 2;
            }

            try
            {
                using var file = File.OpenRead(path);
                using var image = new PEReader(file);
                if (!image.HasMetadata)
                {
                    Console.Error.WriteLine($"package-abi: {path} carries no managed metadata, so it has no reference table to read.");
                    return 2;
                }

                var metadata = image.GetMetadataReader();
                foreach (var handle in metadata.AssemblyReferences)
                {
                    var reference = metadata.GetAssemblyReference(handle);
                    Console.WriteLine($"{metadata.GetString(reference.Name)}\t{reference.Version}");
                }
            }
            catch (BadImageFormatException error)
            {
                Console.Error.WriteLine($"package-abi: {path} is not an assembly this reader can open: {error.Message}");
                return 2;
            }

            return 0;
        }
    }
}
