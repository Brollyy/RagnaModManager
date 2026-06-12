using System.Text.Json.Serialization;

namespace RagnaModManager.Core.Manifests;

public sealed class ModManifest
{
    public int SchemaVersion { get; set; }
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string? Author { get; set; }
    public string Game { get; set; } = "";
    public string? Description { get; set; }
    public Dictionary<string, string>? Requires { get; set; }
    public List<string> Conflicts { get; set; } = [];
    public List<ManifestFile> Files { get; set; } = [];
    public List<string> Affects { get; set; } = [];
    public List<string> Hooks { get; set; } = [];
}

public sealed class ManifestFile
{
    public string Type { get; set; } = "";
    public string Source { get; set; } = "";
    public string? Target { get; set; }
    public string? ModFolder { get; set; }
    public int? LoadOrder { get; set; }

    [JsonIgnore]
    public string EffectiveModFolder => string.IsNullOrWhiteSpace(ModFolder) ? "" : ModFolder!;
}
