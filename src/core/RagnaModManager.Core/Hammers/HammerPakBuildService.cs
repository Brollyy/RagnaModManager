using System.Security.Cryptography;
using System.Text.Json;
using RagnaModManager.Core.Common;
using RagnaModManager.Core.Logging;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

namespace RagnaModManager.Core.Hammers;

public sealed record HammerPakBuildResult(string PakPath, string Sha256, string GamePakSha256, int BaseRowCount, int AddedRowCount, IReadOnlyList<HammerShaderPak> ShaderPaks);

/// <summary>
/// Merges rows into maintainer-prepared metadata for the exact installed game build,
/// then creates a UE4.27 / PAK V11 overlay. This service never decrypts the game PAK.
/// </summary>
public sealed class HammerPakBuildService
{
    private const string DataTablePath = "Ragnarock/Content/Data/Hammers/DT_Hammers.uasset";
    private readonly AppLogger _logger;

    public HammerPakBuildService(AppLogger logger) => _logger = logger;
    public Result<HammerPakBuildResult> Build(
        string gameRoot,
        IReadOnlyList<HammerLibraryEntry> enabledHammers,
        string metadataDirectory,
        string outputPakPath)
    {
        if (enabledHammers.Count == 0) return Result<HammerPakBuildResult>.Fail("Enable at least one custom hammer before building.");
        var gamePakPath = Path.Combine(gameRoot, "Ragnarock", "Content", "Paks", "Ragnarock-WindowsNoEditor.pak");
        if (!File.Exists(gamePakPath)) return Result<HammerPakBuildResult>.Fail("The installed Ragnarock data PAK could not be found.");
        var metadataTablePath = Path.Combine(metadataDirectory, DataTablePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(metadataTablePath)) return Result<HammerPakBuildResult>.Fail("Matching hammer compatibility data is not available for this game update.");

        var staging = Path.Combine(Path.GetTempPath(), "rmm-hammer-build-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(staging);
            var gamePakSha256 = Sha256(gamePakPath);
            var metadata = JsonSerializer.Deserialize<HammerTableMetadataManifest>(
                File.ReadAllText(Path.Combine(metadataDirectory, "metadata.json")),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (metadata is null || metadata.FormatVersion != 1 || metadata.PakVersion != 11 ||
                !string.Equals(metadata.EngineVersion, "UE4.27", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(metadata.GamePakSha256, gamePakSha256, StringComparison.OrdinalIgnoreCase) ||
                metadata.ExistingAssetPackagePaths is null || metadata.ExistingAssetPackagePaths.Count == 0 ||
                !FileSha256Matches(metadataTablePath, metadata.TableSha256))
                return Result<HammerPakBuildResult>.Fail("The hammer compatibility data doesn't match this game update. No PAK was built.");
            var exportSourcePath = Path.ChangeExtension(metadataTablePath, ".uexp");
            if (!FileSha256Matches(exportSourcePath, metadata.ExportSha256) ||
                string.IsNullOrWhiteSpace(metadata.DataAssetTemplatePath) ||
                Path.IsPathRooted(metadata.DataAssetTemplatePath) ||
                !metadata.DataAssetTemplatePath.StartsWith("Ragnarock/Content/Data/Hammers/", StringComparison.Ordinal) ||
                metadata.DataAssetTemplatePath.Contains("..", StringComparison.Ordinal) ||
                !metadata.DataAssetTemplatePath.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
                return Result<HammerPakBuildResult>.Fail("The matching hammer compatibility data is incomplete.");
            var metadataRoot = Path.GetFullPath(metadataDirectory) + Path.DirectorySeparatorChar;
            var metadataTemplatePath = Path.GetFullPath(Path.Combine(metadataDirectory, metadata.DataAssetTemplatePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!metadataTemplatePath.StartsWith(metadataRoot, StringComparison.Ordinal) ||
                !FileSha256Matches(metadataTemplatePath, metadata.DataAssetTemplateSha256) ||
                !FileSha256Matches(Path.ChangeExtension(metadataTemplatePath, ".uexp"), metadata.DataAssetTemplateExportSha256))
                return Result<HammerPakBuildResult>.Fail("The matching hammer compatibility data is incomplete.");
            var baseFileSet = metadata.ExistingAssetPackagePaths.ToHashSet(StringComparer.OrdinalIgnoreCase);

            var tablePath = Path.Combine(staging, DataTablePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(tablePath)!);
            File.Copy(metadataTablePath, tablePath);
            File.Copy(exportSourcePath, Path.ChangeExtension(tablePath, ".uexp"));

            var tableAsset = new UAsset(tablePath, EngineVersion.VER_UE4_27);
            var tableExport = tableAsset.Exports.OfType<DataTableExport>().SingleOrDefault();
            if (tableExport?.Table?.Data is null)
                return Result<HammerPakBuildResult>.Fail("DT_Hammers could not be parsed as a UE4.27 DataTable. This game build is unsupported.");
            var baseRows = tableExport.Table.Data.ToList();
            var existingNames = baseRows.Select(row => row.Name.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var defaultRow = baseRows.FirstOrDefault(row => row.Name.ToString().Equals("Default", StringComparison.OrdinalIgnoreCase));
            if (defaultRow is null || !HasSupportedRowSchema(defaultRow))
                return Result<HammerPakBuildResult>.Fail("DT_Hammers no longer has the supported CustomizableInfo row schema. No PAK was built.");

            var dependencyValidation = ValidateHammerDependencies(enabledHammers, baseFileSet);
            if (!dependencyValidation.Success) return Result<HammerPakBuildResult>.Fail(dependencyValidation.Error!);

            var writtenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var generatedDataAssets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var hammer in enabledHammers.OrderBy(entry => entry.Manifest.RowName, StringComparer.Ordinal))
            {
                var manifest = hammer.Manifest;
                var rowName = HammerLibraryService.GetStableRowName(manifest.Id);
                if (!rowName.Equals(manifest.RowName, StringComparison.Ordinal) || existingNames.Contains(rowName))
                    return Result<HammerPakBuildResult>.Fail($"{manifest.Name} has a duplicate or unstable DataTable row name '{rowName}'.");
                existingNames.Add(rowName);
                if (baseFileSet.Contains(manifest.DataAssetPath))
                    return Result<HammerPakBuildResult>.Fail($"{manifest.Name} would replace an existing game data asset. Choose a different hammer ID.");
                if (!TryCreateHammerDataAsset(metadataTemplatePath, manifest, staging, out var generatedDataAssetPath, out var dataAssetError))
                    return Result<HammerPakBuildResult>.Fail($"{manifest.Name}: {dataAssetError}");
                generatedDataAssets[manifest.Id] = generatedDataAssetPath;

                var row = (StructPropertyData)defaultRow.Clone();
                row.Name = new FName(tableAsset, rowName);
                SetText(row, "Title", manifest.DisplayName, rowName + "_Title");
                SetText(row, "Description", manifest.Description, rowName + "_Description");
                ((StrPropertyData)row["EntitlementId"]).Value = new FString("");
                if (!string.IsNullOrWhiteSpace(manifest.IconAssetPath))
                    SetIcon(row, tableAsset, manifest.IconAssetPath);

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
                    if (baseFileSet.Contains(file.PackagePath))
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
                {
                    foreach (var file in hammer.Manifest.Assets)
                    {
                        if (file.PackagePath.Equals(hammer.Manifest.DataAssetPath, StringComparison.OrdinalIgnoreCase))
                            continue; // Legacy packages may carry this asset; RMM now synthesizes it from the build template.
                        var source = Path.GetFullPath(Path.Combine(hammer.InstalledPath, file.Source.Replace('/', Path.DirectorySeparatorChar)));
                        var root = Path.GetFullPath(hammer.InstalledPath) + Path.DirectorySeparatorChar;
                        if (!source.StartsWith(root, StringComparison.Ordinal) || !File.Exists(source))
                            return Result<HammerPakBuildResult>.Fail($"{hammer.Manifest.Name} is missing cooked file '{file.Source}'.");
                        writer.WriteFile(ToPakPath(file.PackagePath, Path.GetExtension(file.Source)), File.ReadAllBytes(source));
                    }
                    var generatedDataAsset = generatedDataAssets[hammer.Manifest.Id];
                    writer.WriteFile(ToPakPath(hammer.Manifest.DataAssetPath, ".uasset"), File.ReadAllBytes(generatedDataAsset));
                    writer.WriteFile(ToPakPath(hammer.Manifest.DataAssetPath, ".uexp"), File.ReadAllBytes(Path.ChangeExtension(generatedDataAsset, ".uexp")));
                }
                writer.WriteIndex();
            }

            VerifyBuiltPak(tempPak, baseRows.Count + enabledHammers.Count, enabledHammers);
            File.Copy(tempPak, outputPakPath, overwrite: true);
            var shaderPaks = new List<HammerShaderPak>();
            var shaderHammers = enabledHammers
                .Where(hammer => !string.IsNullOrWhiteSpace(hammer.Manifest.ShaderArchive))
                .OrderBy(hammer => hammer.Manifest.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (shaderHammers.Count > 1000)
                return Result<HammerPakBuildResult>.Fail("Too many custom shader archives are enabled for the reserved Ragnarock shader chunk range.");
            for (var index = 0; index < shaderHammers.Count; index++)
            {
                var hammer = shaderHammers[index];
                var chunkId = 10000 + index;
                var chunkFileName = $"pakchunk{chunkId}-WindowsNoEditor.pak";
                var tempShaderPak = Path.Combine(staging, chunkFileName);
                var runtimeArchivePath = $"Ragnarock/Content/ShaderArchive-Global_Chunk{chunkId}-PCD3D_SM5.ushaderbytecode";
                var shaderArchive = Path.GetFullPath(Path.Combine(
                    hammer.InstalledPath,
                    hammer.Manifest.ShaderArchive!.Replace('/', Path.DirectorySeparatorChar)));
                var hammerRoot = Path.GetFullPath(hammer.InstalledPath) + Path.DirectorySeparatorChar;
                if (!shaderArchive.StartsWith(hammerRoot, StringComparison.Ordinal) || !File.Exists(shaderArchive))
                    return Result<HammerPakBuildResult>.Fail($"{hammer.Manifest.Name} is missing its cooked shader archive.");
                using (var shaderStream = File.Create(tempShaderPak))
                using (var writer = new PakBuilder().Writer(shaderStream, PakVersion.V11, "../../../"))
                {
                    writer.WriteFile(runtimeArchivePath, File.ReadAllBytes(shaderArchive));
                    writer.WriteIndex();
                }
                VerifyShaderPak(tempShaderPak, runtimeArchivePath);
                var outputShaderPak = Path.Combine(pakDirectory, chunkFileName);
                File.Copy(tempShaderPak, outputShaderPak, overwrite: true);
                shaderPaks.Add(new HammerShaderPak(outputShaderPak, chunkId));
            }
            return Result<HammerPakBuildResult>.Ok(new HammerPakBuildResult(
                outputPakPath,
                Sha256(new[] { outputPakPath }.Concat(shaderPaks.Select(shaderPak => shaderPak.PakPath))),
                gamePakSha256,
                baseRows.Count,
                enabledHammers.Count,
                shaderPaks));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or ArgumentException or NotSupportedException or JsonException)
        {
            _logger.Error($"Custom hammer package build failed: {ex}");
            return Result<HammerPakBuildResult>.Fail("RMM couldn't build the hammer package. Check the log for details.");
        }
        catch (Exception ex)
        {
            _logger.Error($"Unexpected custom hammer package build failure: {ex}");
            return Result<HammerPakBuildResult>.Fail("RMM couldn't build the hammer package. Check the log for details.");
        }
        finally
        {
            try
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Error($"Could not clean up hammer build staging directory '{staging}': {ex}");
            }
        }
    }

    private static bool TryCreateHammerDataAsset(
        string templatePath,
        HammerManifest manifest,
        string staging,
        out string generatedPath,
        out string? error)
    {
        generatedPath = "";
        error = null;
        try
        {
            var asset = new UAsset(templatePath, EngineVersion.VER_UE4_27);
            var meshImport = asset.Imports.Select((value, index) => (value, index))
                .FirstOrDefault(item => item.value.ClassName.ToString().Equals("StaticMesh", StringComparison.OrdinalIgnoreCase));
            if (meshImport.value is null || meshImport.value.OuterIndex.Index >= 0 ||
                -meshImport.value.OuterIndex.Index > asset.Imports.Count)
            {
                error = "RMM couldn't prepare the model for this game version.";
                return false;
            }

            var packageImport = asset.Imports[-meshImport.value.OuterIndex.Index - 1];
            if (!packageImport.ClassName.ToString().Equals("Package", StringComparison.OrdinalIgnoreCase))
            {
                error = "RMM couldn't prepare the model for this game version.";
                return false;
            }
            packageImport.ObjectName = new FName(asset, manifest.MeshAssetPath);
            meshImport.value.ObjectName = new FName(asset, manifest.MeshAssetPath[(manifest.MeshAssetPath.LastIndexOf('/') + 1)..]);

            var dataAssetExport = asset.Exports.OfType<NormalExport>().SingleOrDefault(export => export.ClassIndex.Index < 0 &&
                    -export.ClassIndex.Index <= asset.Imports.Count &&
                    asset.Imports[-export.ClassIndex.Index - 1].ObjectName.ToString().Equals("DA_Hammers_C", StringComparison.OrdinalIgnoreCase));
            if (dataAssetExport is null)
            {
                error = "RMM couldn't prepare the hammer data for this game version.";
                return false;
            }
            // The template keeps its original export name unless it is renamed to match
            // the generated package. Unreal then resolves the package import but cannot
            // find the requested object, leaving the hammer visible without a model.
            dataAssetExport.ObjectName = new FName(asset, manifest.DataAssetPath[(manifest.DataAssetPath.LastIndexOf('/') + 1)..]);
            if (!string.IsNullOrWhiteSpace(manifest.SilhouetteAssetPath))
                SetDataAssetReference(asset, dataAssetExport, "Silhouette", manifest.SilhouetteAssetPath);
            if (!string.IsNullOrWhiteSpace(manifest.SymbolAssetPath))
                SetDataAssetReference(asset, dataAssetExport, "Symbol", manifest.SymbolAssetPath);

            var outputPath = Path.Combine(staging, "GeneratedAssets", manifest.Id, "DA_Hammers.uasset");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            asset.Write(outputPath);
            var outputExportPath = Path.ChangeExtension(outputPath, ".uexp");
            if (!File.Exists(outputPath) || !File.Exists(outputExportPath))
            {
                error = "RMM couldn't prepare the hammer data for this game version.";
                return false;
            }
            generatedPath = outputPath;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or ArgumentException or NotSupportedException or NullReferenceException)
        {
            error = "RMM couldn't prepare the hammer data for this game version.";
            return false;
        }
    }

    private static bool HasSupportedRowSchema(StructPropertyData row) =>
        row["Title"] is TextPropertyData &&
        row["Description"] is TextPropertyData &&
        row["Icon"] is ObjectPropertyData &&
        row["EntitlementId"] is StrPropertyData &&
        row["Data"] is ObjectPropertyData;

    private static Result ValidateHammerDependencies(IReadOnlyList<HammerLibraryEntry> hammers, HashSet<string> baseFileSet)
    {
        var customPackages = hammers.SelectMany(hammer => hammer.Manifest.Assets)
            .Select(asset => asset.PackagePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var hammer in hammers)
        {
            foreach (var file in hammer.Manifest.Assets.Where(asset =>
                         Path.GetExtension(asset.Source).Equals(".uasset", StringComparison.OrdinalIgnoreCase) &&
                         !asset.PackagePath.Equals(hammer.Manifest.DataAssetPath, StringComparison.OrdinalIgnoreCase)))
            {
                var source = Path.GetFullPath(Path.Combine(hammer.InstalledPath, file.Source.Replace('/', Path.DirectorySeparatorChar)));
                var root = Path.GetFullPath(hammer.InstalledPath) + Path.DirectorySeparatorChar;
                if (!source.StartsWith(root, StringComparison.Ordinal) || !File.Exists(source))
                    return Result.Fail($"{hammer.Manifest.Name} is missing cooked file '{file.Source}'.");

                var asset = new UAsset(source, EngineVersion.VER_UE4_27);
                foreach (var import in asset.Imports.Where(item =>
                             item.ClassName.ToString().Equals("Package", StringComparison.OrdinalIgnoreCase) && item.OuterIndex.Index == 0))
                {
                    var dependency = import.ObjectName.ToString();
                    if (!dependency.StartsWith("/Game/", StringComparison.Ordinal)) continue;
                    if (!customPackages.Contains(dependency) && !baseFileSet.Contains(dependency))
                        return Result.Fail($"{hammer.Manifest.Name}'s cooked asset '{file.Source}' references missing package '{dependency}'. Include that asset in the hammer package or use a package from this Ragnarock build.");
                }
            }
        }

        return Result.Ok();
    }

    private static void SetText(StructPropertyData row, string propertyName, string text, string stableKey)
    {
        var property = (TextPropertyData)row[propertyName];
        property.Value = new FString(stableKey);
        property.CultureInvariantString = new FString(text);
    }

    private static void SetIcon(StructPropertyData row, UAsset tableAsset, string iconPackagePath)
    {
        var iconProperty = (ObjectPropertyData)row["Icon"];
        var templateImport = iconProperty.ToImport(tableAsset);
        var packageIndex = tableAsset.AddImport(new Import(
            new FName(tableAsset, "/Script/CoreUObject"),
            new FName(tableAsset, "Package"),
            new FPackageIndex(0),
            new FName(tableAsset, iconPackagePath),
            false));
        iconProperty.Value = tableAsset.AddImport(new Import(
            templateImport.ClassPackage,
            templateImport.ClassName,
            packageIndex,
            new FName(tableAsset, iconPackagePath[(iconPackagePath.LastIndexOf('/') + 1)..]),
            false));
    }

    private static void SetDataAssetReference(UAsset asset, NormalExport export, string propertyName, string packagePath)
    {
        var property = (ObjectPropertyData)export.Data.Single(item => item.Name.ToString().Equals(propertyName, StringComparison.Ordinal));
        var templateImport = property.ToImport(asset);
        var packageIndex = asset.AddImport(new Import(
            new FName(asset, "/Script/CoreUObject"),
            new FName(asset, "Package"),
            new FPackageIndex(0),
            new FName(asset, packagePath),
            false));
        property.Value = asset.AddImport(new Import(
            templateImport.ClassPackage,
            new FName(asset, "MaterialInstanceConstant"),
            packageIndex,
            new FName(asset, packagePath[(packagePath.LastIndexOf('/') + 1)..]),
            false));
    }

    private static string ToPakPath(string packagePath, string extension) =>
        "Ragnarock/Content/" + packagePath["/Game/".Length..] + extension;

    private static string Sha256(IEnumerable<string> paths)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in paths.Order(StringComparer.OrdinalIgnoreCase))
        {
            using var stream = File.OpenRead(path);
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                hash.AppendData(buffer, 0, read);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void VerifyShaderPak(string path, string archivePath)
    {
        using var stream = File.OpenRead(path);
        using var reader = new PakBuilder().Reader(stream);
        if (reader.GetVersion() != PakVersion.V11 || !reader.Files().Contains(archivePath, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"The generated shader PAK is missing {archivePath}.");
    }

    private static void VerifyBuiltPak(string path, int expectedRows, IReadOnlyList<HammerLibraryEntry> hammers)
    {
        using var stream = File.OpenRead(path);
        using var reader = new PakBuilder().Reader(stream);
        if (reader.GetVersion() != PakVersion.V11) throw new InvalidDataException("The generated PAK is not V11.");
        var files = reader.Files().ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!files.Contains(DataTablePath) || !files.Contains(DataTablePath.Replace(".uasset", ".uexp", StringComparison.Ordinal)))
            throw new InvalidDataException("The generated PAK is missing a DT_Hammers cooked file.");
        foreach (var hammer in hammers)
        {
            foreach (var file in hammer.Manifest.Assets)
                if (!files.Contains(ToPakPath(file.PackagePath, Path.GetExtension(file.Source))))
                    throw new InvalidDataException($"The generated PAK is missing {hammer.Manifest.Name}'s {file.Source}.");
            if (!string.IsNullOrWhiteSpace(hammer.Manifest.IconAssetPath) &&
                !files.Contains(ToPakPath(hammer.Manifest.IconAssetPath, ".uasset")))
                throw new InvalidDataException($"The generated PAK is missing {hammer.Manifest.Name}'s icon asset.");
            if (!string.IsNullOrWhiteSpace(hammer.Manifest.SilhouetteAssetPath) &&
                !files.Contains(ToPakPath(hammer.Manifest.SilhouetteAssetPath, ".uasset")))
                throw new InvalidDataException($"The generated PAK is missing {hammer.Manifest.Name}'s silhouette material.");
            if (!string.IsNullOrWhiteSpace(hammer.Manifest.SymbolAssetPath) &&
                !files.Contains(ToPakPath(hammer.Manifest.SymbolAssetPath, ".uasset")))
                throw new InvalidDataException($"The generated PAK is missing {hammer.Manifest.Name}'s symbol material.");
            if (!files.Contains(ToPakPath(hammer.Manifest.DataAssetPath, ".uasset")) ||
                !files.Contains(ToPakPath(hammer.Manifest.DataAssetPath, ".uexp")))
                throw new InvalidDataException($"The generated PAK is missing {hammer.Manifest.Name}'s generated hammer data.");
        }
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
            {
                var row = export.Table.Data.FirstOrDefault(item => item.Name.ToString() == hammer.Manifest.RowName);
                if (row is null)
                    throw new InvalidDataException($"Generated DT_Hammers is missing {hammer.Manifest.Name}'s row.");
                var dataImport = ((ObjectPropertyData)row["Data"]).ToImport(asset);
                var packageImport = dataImport.OuterIndex.Index < 0
                    ? asset.Imports[-dataImport.OuterIndex.Index - 1]
                    : null;
                if (packageImport is null || !packageImport.ObjectName.ToString().Equals(hammer.Manifest.DataAssetPath, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Generated DT_Hammers points {hammer.Manifest.Name} at the wrong data asset.");
                var dataBytes = reader.Get(stream, ToPakPath(hammer.Manifest.DataAssetPath, ".uasset")) ??
                                throw new InvalidDataException($"Could not verify {hammer.Manifest.Name}'s generated hammer data.");
                var dataExportBytes = reader.Get(stream, ToPakPath(hammer.Manifest.DataAssetPath, ".uexp")) ??
                                      throw new InvalidDataException($"Could not verify {hammer.Manifest.Name}'s generated hammer data.");
                var dataPath = Path.Combine(checkDirectory, hammer.Manifest.Id + ".uasset");
                File.WriteAllBytes(dataPath, dataBytes);
                File.WriteAllBytes(Path.ChangeExtension(dataPath, ".uexp"), dataExportBytes);
                var dataAsset = new UAsset(dataPath, EngineVersion.VER_UE4_27);
                if (!dataAsset.Imports.Any(item => item.ClassName.ToString().Equals("Package", StringComparison.OrdinalIgnoreCase) &&
                                                   item.ObjectName.ToString().Equals(hammer.Manifest.MeshAssetPath, StringComparison.OrdinalIgnoreCase)) ||
                    !dataAsset.Exports.Any(item => item.ObjectName.ToString().Equals(hammer.Manifest.DataAssetPath[(hammer.Manifest.DataAssetPath.LastIndexOf('/') + 1)..], StringComparison.Ordinal) &&
                                                   item.ClassIndex.Index < 0 && -item.ClassIndex.Index <= dataAsset.Imports.Count &&
                                                   dataAsset.Imports[-item.ClassIndex.Index - 1].ObjectName.ToString().Equals("DA_Hammers_C", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException($"Generated hammer data for {hammer.Manifest.Name} does not reference its model correctly.");
                VerifyDataAssetReference(dataAsset, hammer.Manifest, "Silhouette", hammer.Manifest.SilhouetteAssetPath);
                VerifyDataAssetReference(dataAsset, hammer.Manifest, "Symbol", hammer.Manifest.SymbolAssetPath);
                if (!string.IsNullOrWhiteSpace(hammer.Manifest.IconAssetPath))
                {
                    var icon = ((ObjectPropertyData)row["Icon"]).ToImport(asset);
                    var iconPackage = icon.OuterIndex.Index < 0 ? asset.Imports[-icon.OuterIndex.Index - 1] : null;
                    if (icon.ClassName.ToString() != "Texture2D" || iconPackage is null ||
                        !iconPackage.ObjectName.ToString().Equals(hammer.Manifest.IconAssetPath, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"Generated DT_Hammers does not reference {hammer.Manifest.Name}'s icon correctly.");
                }
            }
        }
        finally
        {
            if (Directory.Exists(checkDirectory)) Directory.Delete(checkDirectory, recursive: true);
        }
    }

    private static void VerifyDataAssetReference(UAsset asset, HammerManifest manifest, string propertyName, string? packagePath)
    {
        if (string.IsNullOrWhiteSpace(packagePath)) return;
        var export = asset.Exports.OfType<NormalExport>().Single(item => item.ObjectName.ToString().Equals(
            manifest.DataAssetPath[(manifest.DataAssetPath.LastIndexOf('/') + 1)..], StringComparison.Ordinal));
        var importedObject = ((ObjectPropertyData)export.Data.Single(item => item.Name.ToString().Equals(propertyName, StringComparison.Ordinal))).ToImport(asset);
        var importedPackage = importedObject.OuterIndex.Index < 0 ? asset.Imports[-importedObject.OuterIndex.Index - 1] : null;
        if (importedObject.ClassName.ToString() != "MaterialInstanceConstant" || importedPackage is null ||
            !importedPackage.ObjectName.ToString().Equals(packagePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Generated hammer data for {manifest.Name} does not reference its {propertyName} material correctly.");
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool FileSha256Matches(string path, string expected)
    {
        if (!File.Exists(path)) return false;
        try { return Sha256(path).Equals(expected, StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }
}
