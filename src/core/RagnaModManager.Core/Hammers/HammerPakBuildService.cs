using System.Security.Cryptography;
using System.Text.RegularExpressions;
using RagnaModManager.Core.Common;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

namespace RagnaModManager.Core.Hammers;

public sealed record HammerPakBuildResult(string PakPath, string Sha256, string GamePakSha256, int BaseRowCount, int AddedRowCount);

/// <summary>
/// Merges rows into the installed DT_Hammers, then creates a UE4.27 / PAK V11 overlay.
/// The base game table is read from the user's encrypted install and is never bundled.
/// </summary>
public sealed class HammerPakBuildService
{
    private const string DataTablePath = "Ragnarock/Content/Data/Hammers/DT_Hammers.uasset";
    private static readonly Regex HexKey = new("(?i)(?<![0-9a-f])(?:0x)?[0-9a-f]{64}(?![0-9a-f])", RegexOptions.Compiled);

    public Result<HammerPakBuildResult> Build(
        string gameRoot,
        IReadOnlyList<HammerLibraryEntry> enabledHammers,
        string keyCandidateFile,
        string outputPakPath)
    {
        if (enabledHammers.Count == 0) return Result<HammerPakBuildResult>.Fail("Enable at least one custom hammer before building.");
        if (!File.Exists(keyCandidateFile)) return Result<HammerPakBuildResult>.Fail("The AES key candidate file could not be found.");
        var gamePakPath = Path.Combine(gameRoot, "Ragnarock", "Content", "Paks", "Ragnarock-WindowsNoEditor.pak");
        if (!File.Exists(gamePakPath)) return Result<HammerPakBuildResult>.Fail("The installed Ragnarock data PAK could not be found.");

        var staging = Path.Combine(Path.GetTempPath(), "rmm-hammer-build-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(staging);
            var keyResult = FindPakKey(gamePakPath, keyCandidateFile);
            if (!keyResult.Success || keyResult.Value is null) return Result<HammerPakBuildResult>.Fail(keyResult.Error!);
            var gamePakSha256 = Sha256(gamePakPath);

            using var sourceStream = File.OpenRead(gamePakPath);
            using var sourcePak = new PakBuilder().Key(keyResult.Value).Reader(sourceStream);
            if (sourcePak.GetVersion() != PakVersion.V11)
                return Result<HammerPakBuildResult>.Fail($"This game uses PAK {sourcePak.GetVersion()}, but the hammer merge adapter supports PAK V11.");

            var baseFileSet = sourcePak.Files().ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!baseFileSet.Contains(DataTablePath))
                return Result<HammerPakBuildResult>.Fail("The installed game PAK does not contain DT_Hammers at the supported package path.");
            var tableBytes = sourcePak.Get(sourceStream, DataTablePath);
            var exportBytes = sourcePak.Get(sourceStream, DataTablePath.Replace(".uasset", ".uexp", StringComparison.Ordinal));
            if (tableBytes is null || exportBytes is null)
                return Result<HammerPakBuildResult>.Fail("Could not read both cooked files for the installed DT_Hammers asset.");

            var tablePath = Path.Combine(staging, DataTablePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(tablePath)!);
            File.WriteAllBytes(tablePath, tableBytes);
            File.WriteAllBytes(Path.ChangeExtension(tablePath, ".uexp"), exportBytes);

            var tableAsset = new UAsset(tablePath, EngineVersion.VER_UE4_27);
            var tableExport = tableAsset.Exports.OfType<DataTableExport>().SingleOrDefault();
            if (tableExport?.Table?.Data is null)
                return Result<HammerPakBuildResult>.Fail("DT_Hammers could not be parsed as a UE4.27 DataTable. This game build is unsupported.");
            var baseRows = tableExport.Table.Data.ToList();
            var existingNames = baseRows.Select(row => row.Name.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var defaultRow = baseRows.FirstOrDefault(row => row.Name.ToString().Equals("Default", StringComparison.OrdinalIgnoreCase));
            if (defaultRow is null || !HasSupportedRowSchema(defaultRow))
                return Result<HammerPakBuildResult>.Fail("DT_Hammers no longer has the supported CustomizableInfo row schema. No PAK was built.");

            var writtenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var hammer in enabledHammers.OrderBy(entry => entry.Manifest.RowName, StringComparer.Ordinal))
            {
                var manifest = hammer.Manifest;
                var rowName = HammerLibraryService.GetStableRowName(manifest.Id);
                if (!rowName.Equals(manifest.RowName, StringComparison.Ordinal) || existingNames.Contains(rowName))
                    return Result<HammerPakBuildResult>.Fail($"{manifest.Name} has a duplicate or unstable DataTable row name '{rowName}'.");
                existingNames.Add(rowName);

                var row = (StructPropertyData)defaultRow.Clone();
                row.Name = new FName(tableAsset, rowName);
                SetText(row, "Title", manifest.DisplayName, rowName + "_Title");
                SetText(row, "Description", manifest.Description, rowName + "_Description");
                ((StrPropertyData)row["EntitlementId"]).Value = new FString("");

                var dataReference = (ObjectPropertyData)row["Data"];
                var defaultDataImport = dataReference.ToImport(tableAsset);
                var packagePath = manifest.DataAssetPath;
                var assetName = packagePath[(packagePath.LastIndexOf('/') + 1)..];
                var packageIndex = tableAsset.AddImport(new Import(
                    new FName(tableAsset, "/Script/CoreUObject"),
                    new FName(tableAsset, "Package"),
                    new FPackageIndex(0),
                    new FName(tableAsset, packagePath),
                    false));
                dataReference.Value = tableAsset.AddImport(new Import(
                    defaultDataImport.ClassPackage,
                    defaultDataImport.ClassName,
                    packageIndex,
                    new FName(tableAsset, assetName),
                    false));
                tableExport.Table.Data.Add(row);

                foreach (var file in manifest.Assets)
                {
                    var extension = Path.GetExtension(file.Source);
                    var pakPath = ToPakPath(file.PackagePath, extension);
                    if (!writtenFiles.Add(pakPath))
                        return Result<HammerPakBuildResult>.Fail($"More than one hammer package writes '{pakPath}'.");
                    if (baseFileSet.Contains(pakPath))
                        return Result<HammerPakBuildResult>.Fail($"{manifest.Name} would replace existing game asset '{pakPath}'. Choose a unique package path.");
                }
            }

            tableAsset.Write(tablePath);
            var verifiedTable = new UAsset(tablePath, EngineVersion.VER_UE4_27);
            var verifiedExport = verifiedTable.Exports.OfType<DataTableExport>().SingleOrDefault();
            if (verifiedExport?.Table?.Data.Count != baseRows.Count + enabledHammers.Count || !verifiedTable.VerifyBinaryEquality())
                return Result<HammerPakBuildResult>.Fail("The merged DT_Hammers failed its cooked asset round-trip check.");
            if (baseRows.Any(row => !verifiedExport.Table.Data.Any(current => current.Name.ToString() == row.Name.ToString())))
                return Result<HammerPakBuildResult>.Fail("The merged DT_Hammers did not retain every installed stock and DLC row.");

            var pakDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPakPath))!;
            Directory.CreateDirectory(pakDirectory);
            var tempPak = Path.Combine(staging, "RMM_CustomHammers_P.pak");
            using (var outputStream = File.Create(tempPak))
            using (var writer = new PakBuilder().Writer(outputStream, PakVersion.V11, "../../../"))
            {
                writer.WriteFile(DataTablePath, File.ReadAllBytes(tablePath));
                writer.WriteFile(DataTablePath.Replace(".uasset", ".uexp", StringComparison.Ordinal), File.ReadAllBytes(Path.ChangeExtension(tablePath, ".uexp")));
                foreach (var hammer in enabledHammers)
                foreach (var file in hammer.Manifest.Assets)
                {
                    var source = Path.GetFullPath(Path.Combine(hammer.InstalledPath, file.Source.Replace('/', Path.DirectorySeparatorChar)));
                    var root = Path.GetFullPath(hammer.InstalledPath) + Path.DirectorySeparatorChar;
                    if (!source.StartsWith(root, StringComparison.Ordinal) || !File.Exists(source))
                        return Result<HammerPakBuildResult>.Fail($"{hammer.Manifest.Name} is missing cooked file '{file.Source}'.");
                    writer.WriteFile(ToPakPath(file.PackagePath, Path.GetExtension(file.Source)), File.ReadAllBytes(source));
                }
                writer.WriteIndex();
            }

            VerifyBuiltPak(tempPak, baseRows.Count + enabledHammers.Count, enabledHammers);
            File.Copy(tempPak, outputPakPath, overwrite: true);
            return Result<HammerPakBuildResult>.Ok(new HammerPakBuildResult(
                outputPakPath,
                Sha256(outputPakPath),
                gamePakSha256,
                baseRows.Count,
                enabledHammers.Count));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            return Result<HammerPakBuildResult>.Fail($"Could not build custom hammer PAK: {ex.Message}");
        }
        catch (Exception ex)
        {
            return Result<HammerPakBuildResult>.Fail($"Could not build custom hammer PAK: {ex.Message}");
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private static Result<byte[]> FindPakKey(string gamePakPath, string candidateFile)
    {
        var info = new FileInfo(candidateFile);
        if (info.Length > 1024 * 1024) return Result<byte[]>.Fail("The AES key candidate file is larger than 1 MiB.");
        var contents = File.ReadAllText(candidateFile);
        var candidates = HexKey.Matches(contents)
            .Select(match => match.Value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? match.Value[2..] : match.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(256);
        foreach (var candidate in candidates)
        {
            byte[] key;
            try { key = Convert.FromHexString(candidate); }
            catch (FormatException) { continue; }
            try
            {
                using var stream = File.OpenRead(gamePakPath);
                using var reader = new PakBuilder().Key(key).Reader(stream);
                if (reader.GetVersion() == PakVersion.V11 && reader.Files().Contains(DataTablePath, StringComparer.OrdinalIgnoreCase))
                    return Result<byte[]>.Ok(key);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException or NotSupportedException or DllNotFoundException) { }
            catch (Exception) { }
        }
        return Result<byte[]>.Fail("No AES key candidate in that file opened this game's V11 PAK with DT_Hammers. Choose the matching local key list.");
    }

    private static bool HasSupportedRowSchema(StructPropertyData row) =>
        row["Title"] is TextPropertyData &&
        row["Description"] is TextPropertyData &&
        row["Icon"] is ObjectPropertyData &&
        row["EntitlementId"] is StrPropertyData &&
        row["Data"] is ObjectPropertyData;

    private static void SetText(StructPropertyData row, string propertyName, string text, string stableKey)
    {
        var property = (TextPropertyData)row[propertyName];
        property.Value = new FString(stableKey);
        property.CultureInvariantString = new FString(text);
    }

    private static string ToPakPath(string packagePath, string extension) =>
        "Ragnarock/Content/" + packagePath["/Game/".Length..] + extension;

    private static void VerifyBuiltPak(string path, int expectedRows, IReadOnlyList<HammerLibraryEntry> hammers)
    {
        using var stream = File.OpenRead(path);
        using var reader = new PakBuilder().Reader(stream);
        if (reader.GetVersion() != PakVersion.V11) throw new InvalidDataException("The generated PAK is not V11.");
        var files = reader.Files().ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!files.Contains(DataTablePath) || !files.Contains(DataTablePath.Replace(".uasset", ".uexp", StringComparison.Ordinal)))
            throw new InvalidDataException("The generated PAK is missing a DT_Hammers cooked file.");
        foreach (var hammer in hammers)
        foreach (var file in hammer.Manifest.Assets)
            if (!files.Contains(ToPakPath(file.PackagePath, Path.GetExtension(file.Source))))
                throw new InvalidDataException($"The generated PAK is missing {hammer.Manifest.Name}'s {file.Source}.");
        var tableBytes = reader.Get(stream, DataTablePath) ?? throw new InvalidDataException("Could not read merged DT_Hammers from generated PAK.");
        var expBytes = reader.Get(stream, DataTablePath.Replace(".uasset", ".uexp", StringComparison.Ordinal)) ?? throw new InvalidDataException("Could not read merged DT_Hammers export data from generated PAK.");
        var checkDirectory = Path.Combine(Path.GetTempPath(), "rmm-hammer-pak-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(checkDirectory);
            var table = Path.Combine(checkDirectory, "DT_Hammers.uasset");
            File.WriteAllBytes(table, tableBytes);
            File.WriteAllBytes(Path.ChangeExtension(table, ".uexp"), expBytes);
            var asset = new UAsset(table, EngineVersion.VER_UE4_27);
            var export = asset.Exports.OfType<DataTableExport>().SingleOrDefault();
            if (export?.Table?.Data.Count != expectedRows)
                throw new InvalidDataException($"Generated DT_Hammers has {export?.Table?.Data.Count ?? 0} rows; expected {expectedRows}.");
            foreach (var hammer in hammers)
                if (!export.Table.Data.Any(row => row.Name.ToString() == hammer.Manifest.RowName))
                    throw new InvalidDataException($"Generated DT_Hammers is missing {hammer.Manifest.Name}'s row.");
        }
        finally
        {
            if (Directory.Exists(checkDirectory)) Directory.Delete(checkDirectory, recursive: true);
        }
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
