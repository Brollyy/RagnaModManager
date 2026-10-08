using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

const string tablePakPath = "Ragnarock/Content/Data/Hammers/DT_Hammers.uasset";
if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: HammerMetadataTool <Ragnarock-WindowsNoEditor.pak> <local-key-candidates.txt> <output.zip>");
    return 2;
}

var gamePak = Path.GetFullPath(args[0]);
var keyList = Path.GetFullPath(args[1]);
var output = Path.GetFullPath(args[2]);
if (!File.Exists(gamePak) || !File.Exists(keyList))
{
    Console.Error.WriteLine("The game PAK or key candidate file could not be found.");
    return 2;
}

try
{
    var key = FindKey(gamePak, keyList);
    if (key is null)
    {
        Console.Error.WriteLine("No candidate opened this game's PAK with the expected hammer table.");
        return 1;
    }

    var gamePakSha256 = HashFile(gamePak);
    var metadataDirectory = Path.Combine(Path.GetTempPath(), "rmm-hammer-metadata-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(metadataDirectory);
    try
    {
        var tableOutput = Path.Combine(metadataDirectory, "Ragnarock", "Content", "Data", "Hammers", "DT_Hammers.uasset");
        Directory.CreateDirectory(Path.GetDirectoryName(tableOutput)!);
        using (var sourceStream = File.OpenRead(gamePak))
        using (var reader = new PakBuilder().Key(key).Reader(sourceStream))
        {
            if (reader.GetVersion() != PakVersion.V11)
            {
                Console.Error.WriteLine("Unsupported PAK version: " + reader.GetVersion() + ".");
                return 1;
            }
            var tableBytes = reader.Get(sourceStream, tablePakPath);
            var exportBytes = reader.Get(sourceStream, tablePakPath.Replace(".uasset", ".uexp", StringComparison.Ordinal));
            if (tableBytes is null || exportBytes is null)
            {
                Console.Error.WriteLine("The PAK does not contain both cooked DT_Hammers files.");
                return 1;
            }
            File.WriteAllBytes(tableOutput, tableBytes);
            File.WriteAllBytes(Path.ChangeExtension(tableOutput, ".uexp"), exportBytes);

            var tableAsset = new UAsset(tableOutput, EngineVersion.VER_UE4_27);
            var tableExport = tableAsset.Exports.OfType<DataTableExport>().SingleOrDefault();
            if (tableExport?.Table?.Data is null)
            {
                Console.Error.WriteLine("DT_Hammers could not be read as a supported game table.");
                return 1;
            }
            string? templatePakPath = null;
            byte[]? templateBytes = null;
            byte[]? templateExportBytes = null;
            foreach (var row in tableExport.Table.Data.Where(row => !row.Name.ToString().Equals("Default", StringComparison.OrdinalIgnoreCase)))
            {
                if (row is not StructPropertyData rowData || rowData["Data"] is not ObjectPropertyData dataReference)
                    continue;
                var dataImport = dataReference.ToImport(tableAsset);
                if (dataImport.OuterIndex.Index >= 0 || -dataImport.OuterIndex.Index > tableAsset.Imports.Count)
                    continue;
                var packagePath = tableAsset.Imports[-dataImport.OuterIndex.Index - 1].ObjectName.ToString();
                if (!packagePath.StartsWith("/Game/", StringComparison.Ordinal))
                    continue;
                var candidatePakPath = "Ragnarock/Content/" + packagePath["/Game/".Length..] + ".uasset";
                var candidateBytes = reader.Get(sourceStream, candidatePakPath);
                var candidateExportBytes = reader.Get(sourceStream, candidatePakPath.Replace(".uasset", ".uexp", StringComparison.Ordinal));
                if (candidateBytes is null || candidateExportBytes is null)
                    continue;
                var candidatePath = Path.Combine(metadataDirectory, ".template-probe.uasset");
                File.WriteAllBytes(candidatePath, candidateBytes);
                File.WriteAllBytes(Path.ChangeExtension(candidatePath, ".uexp"), candidateExportBytes);
                try
                {
                    var candidateAsset = new UAsset(candidatePath, EngineVersion.VER_UE4_27);
                    var hasHammerClass = candidateAsset.Exports.Any(export => export.ClassIndex.Index < 0 &&
                        -export.ClassIndex.Index <= candidateAsset.Imports.Count &&
                        candidateAsset.Imports[-export.ClassIndex.Index - 1].ObjectName.ToString().Equals("DA_Hammers_C", StringComparison.OrdinalIgnoreCase));
                    if (hasHammerClass && candidateAsset.Imports.Any(import => import.ClassName.ToString().Equals("StaticMesh", StringComparison.OrdinalIgnoreCase)))
                    {
                        templatePakPath = candidatePakPath;
                        templateBytes = candidateBytes;
                        templateExportBytes = candidateExportBytes;
                        break;
                    }
                }
                finally
                {
                    File.Delete(candidatePath);
                    File.Delete(Path.ChangeExtension(candidatePath, ".uexp"));
                }
            }
            if (templatePakPath is null || templateBytes is null || templateExportBytes is null)
            {
                Console.Error.WriteLine("No stock hammer data asset with a model reference was found in DT_Hammers.");
                return 1;
            }
            var templateOutput = Path.Combine(metadataDirectory, templatePakPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(templateOutput)!);
            File.WriteAllBytes(templateOutput, templateBytes);
            File.WriteAllBytes(Path.ChangeExtension(templateOutput, ".uexp"), templateExportBytes);

            var assetPaths = reader.Files()
                .Where(path => path.StartsWith("Ragnarock/Content/", StringComparison.OrdinalIgnoreCase) &&
                               path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
                .Select(path => "/Game/" + Path.ChangeExtension(path["Ragnarock/Content/".Length..], null).Replace('\\', '/'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var metadata = new
            {
                formatVersion = 1,
                gamePakSha256,
                engineVersion = "UE4.27",
                pakVersion = 11,
                tablePath = tablePakPath,
                tableSha256 = HashFile(tableOutput),
                exportSha256 = HashFile(Path.ChangeExtension(tableOutput, ".uexp")),
                dataAssetTemplatePath = templatePakPath,
                dataAssetTemplateSha256 = HashFile(templateOutput),
                dataAssetTemplateExportSha256 = HashFile(Path.ChangeExtension(templateOutput, ".uexp")),
                existingAssetPackagePaths = assetPaths
            };
            File.WriteAllText(Path.Combine(metadataDirectory, "metadata.json"), JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        if (File.Exists(output)) File.Delete(output);
        ZipFile.CreateFromDirectory(metadataDirectory, output, CompressionLevel.Optimal, includeBaseDirectory: false);
        Console.WriteLine("Prepared metadata for Ragnarock PAK SHA-256 " + gamePakSha256 + ".");
        Console.WriteLine("Archive: " + output);
        Console.WriteLine("Archive SHA-256: " + HashFile(output));
        return 0;
    }
    finally
    {
        if (Directory.Exists(metadataDirectory)) Directory.Delete(metadataDirectory, recursive: true);
    }
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or ArgumentException or NotSupportedException or DllNotFoundException)
{
    Console.Error.WriteLine("Metadata preparation failed: " + ex.Message);
    return 1;
}

static byte[]? FindKey(string gamePakPath, string keyCandidatesPath)
{
    if (new FileInfo(keyCandidatesPath).Length > 1024 * 1024) throw new InvalidDataException("The local key candidate file exceeds 1 MiB.");
    var regex = new Regex("(?i)(?<![0-9a-f])(?:0x)?[0-9a-f]{64}(?![0-9a-f])", RegexOptions.Compiled);
    foreach (var match in regex.Matches(File.ReadAllText(keyCandidatesPath)).Cast<Match>()
                 .Select(item => item.Value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? item.Value[2..] : item.Value)
                 .Distinct(StringComparer.OrdinalIgnoreCase)
                 .Take(256))
    {
        byte[] key;
        try { key = Convert.FromHexString(match); }
        catch (FormatException) { continue; }
        try
        {
            using var stream = File.OpenRead(gamePakPath);
            using var reader = new PakBuilder().Key(key).Reader(stream);
            if (reader.GetVersion() == PakVersion.V11 && reader.Files().Contains(tablePakPath, StringComparer.OrdinalIgnoreCase))
                return key;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException or NotSupportedException or DllNotFoundException) { }
        catch (Exception) { }
    }
    return null;
}

static string HashFile(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream));
}
