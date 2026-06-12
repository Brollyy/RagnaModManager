using System.Text.Json;
using RagnaModManager.Core.Compatibility;
using RagnaModManager.Core.Common;

namespace RagnaModManager.Core.Manifests;

public static class ManifestValidator
{
    private static readonly HashSet<string> SupportedFileTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ue4ss-lua",
        "ue4ss-dll",
        "pak",
        "config",
        "loose-file"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        WriteIndented = true
    };

    public static Result<ModManifest> LoadAndValidate(string manifestPath, bool developerMode = false)
    {
        try
        {
            var json = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<ModManifest>(json, JsonOptions);
            if (manifest is null)
            {
                return Result<ModManifest>.Fail("manifest.json is empty or malformed.");
            }

            var result = Validate(manifest, developerMode);
            return result.Success ? Result<ModManifest>.Ok(manifest) : Result<ModManifest>.Fail(result.Error!);
        }
        catch (JsonException ex)
        {
            return Result<ModManifest>.Fail($"manifest.json is not valid JSON: {ex.Message}");
        }
        catch (IOException ex)
        {
            return Result<ModManifest>.Fail($"Could not read manifest.json: {ex.Message}");
        }
    }

    public static Result Validate(ModManifest manifest, bool developerMode = false)
    {
        if (manifest.SchemaVersion != 1)
        {
            return Result.Fail($"Unsupported manifest schemaVersion '{manifest.SchemaVersion}'. Expected 1.");
        }

        if (!IsSlug(manifest.Id))
        {
            return Result.Fail("Manifest id must be lowercase letters, numbers, dots, underscores, or hyphens.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            return Result.Fail("Manifest name is required.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Version))
        {
            return Result.Fail("Manifest version is required.");
        }

        if (!string.Equals(manifest.Game, "ragnarock", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Fail("Manifest game must be 'ragnarock'.");
        }

        if (manifest.Files.Count == 0)
        {
            return Result.Fail("Manifest must declare at least one file entry.");
        }

        foreach (var file in manifest.Files)
        {
            if (!developerMode && !SupportedFileTypes.Contains(file.Type))
            {
                return Result.Fail($"Unsupported file type '{file.Type}'.");
            }

            if (!PathSafety.IsSafeRelativePath(file.Source))
            {
                return Result.Fail($"Unsafe source path in manifest: '{file.Source}'.");
            }

            if (!string.IsNullOrWhiteSpace(file.Target) && !PathSafety.IsSafeRelativePath(file.Target!))
            {
                return Result.Fail($"Unsafe target path in manifest: '{file.Target}'.");
            }

            if ((file.Type.Equals("ue4ss-lua", StringComparison.OrdinalIgnoreCase) ||
                 file.Type.Equals("ue4ss-dll", StringComparison.OrdinalIgnoreCase)) &&
                string.IsNullOrWhiteSpace(file.ModFolder))
            {
                return Result.Fail($"{file.Type} entries must declare modFolder.");
            }
        }

        if (manifest.Requires is not null)
        {
            foreach (var requirement in manifest.Requires)
            {
                if (!requirement.Key.Equals("manager", StringComparison.OrdinalIgnoreCase) &&
                    !requirement.Key.Equals("ue4ss", StringComparison.OrdinalIgnoreCase))
                {
                    return Result.Fail($"Unsupported requirement key '{requirement.Key}'.");
                }

                var syntax = VersionRequirement.ValidateSyntax(requirement.Value);
                if (!syntax.Success)
                {
                    return syntax;
                }
            }
        }

        return Result.Ok();
    }

    public static void Write(ModManifest manifest, string path)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, JsonOptions));
    }

    private static bool IsSlug(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.All(c => char.IsAsciiLetterLower(c) || char.IsDigit(c) || c is '-' or '_' or '.');
    }
}
