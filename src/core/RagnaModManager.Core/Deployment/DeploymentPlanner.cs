using RagnaModManager.Core.Common;
using RagnaModManager.Core.Compatibility;
using RagnaModManager.Core.Database;
using RagnaModManager.Core.Manifests;

namespace RagnaModManager.Core.Deployment;

public sealed class DeploymentPlanner
{
    private readonly ManagerDatabase _database;
    private readonly IGameDeploymentRules _rules;

    public DeploymentPlanner(ManagerDatabase database, IGameDeploymentRules rules)
    {
        _database = database;
        _rules = rules;
    }

    public Result<DeploymentPlan> BuildPlan(string gameRoot)
    {
        var profile = _database.GetActiveProfile();
        var profileMods = _database.GetProfileMods(profile.Id).Where(m => m.Enabled).OrderBy(m => m.Priority).ThenBy(m => m.ModId).ToList();
        var mods = _database.GetMods().ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        var items = new List<DeploymentItem>();
        var warnings = new List<string>();
        var affects = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var hooks = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var enabledManifests = new Dictionary<string, ModManifest>(StringComparer.OrdinalIgnoreCase);
        var requirementConflicts = new List<DeploymentConflict>();

        foreach (var profileMod in profileMods)
        {
            if (!mods.TryGetValue(profileMod.ModId, out var mod))
            {
                warnings.Add($"Profile references missing mod '{profileMod.ModId}'.");
                continue;
            }

            var manifestResult = ManifestValidator.LoadAndValidate(mod.ManifestPath);
            if (!manifestResult.Success)
            {
                return Result<DeploymentPlan>.Fail($"Could not deploy {mod.Name}: {manifestResult.Error}");
            }

            var manifest = manifestResult.Value!;
            enabledManifests[manifest.Id] = manifest;
            var manifestItems = new List<DeploymentItem>();

            if (manifest.Requires?.TryGetValue("manager", out var managerRequirement) == true &&
                !VersionRequirement.IsSatisfied(managerRequirement, ManagerCompatibility.Version))
            {
                requirementConflicts.Add(new DeploymentConflict(
                    "manager-requirement",
                    $"{manifest.Id} requires manager {managerRequirement}, current manager is {ManagerCompatibility.Version}.",
                    [],
                    BlocksDeployment: true));
            }

            foreach (var affected in manifest.Affects)
            {
                AddMulti(affects, affected, manifest.Id);
            }

            foreach (var hook in manifest.Hooks)
            {
                AddMulti(hooks, hook, manifest.Id);
            }

            foreach (var file in manifest.Files)
            {
                var source = PathSafety.CombineUnderRoot(mod.InstalledPath, file.Source);
                if (!File.Exists(source) && !Directory.Exists(source))
                {
                    return Result<DeploymentPlan>.Fail($"Manifest source does not exist: {source}");
                }

                foreach (var expandedSource in ExpandSource(source, file))
                {
                    var target = _rules.GetTargetPath(gameRoot, manifest, file, expandedSource);
                    if (!_rules.IsApprovedTarget(gameRoot, target))
                    {
                        return Result<DeploymentPlan>.Fail($"Manifest entry for {manifest.Id} maps outside approved Ragnarock targets: {target}");
                    }

                    var item = new DeploymentItem(manifest.Id, expandedSource, target, "copy", file.Type);
                    items.Add(item);
                    manifestItems.Add(item);
                }
            }

            requirementConflicts.AddRange(_rules.GetRequirementConflicts(gameRoot, manifest, manifestItems));
        }

        var conflicts = DetectConflicts(items, affects, hooks, enabledManifests).Concat(requirementConflicts).ToList();
        return Result<DeploymentPlan>.Ok(new DeploymentPlan(profile.Id, items, conflicts, warnings));
    }

    private IEnumerable<string> ExpandSource(string source, ManifestFile file)
    {
        if (Directory.Exists(source))
        {
            return Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Order();
        }

        if (file.Type.Equals("pak", StringComparison.OrdinalIgnoreCase))
        {
            return _rules.GetRelatedPackageFiles(source).Order();
        }

        return [source];
    }

    private static IReadOnlyList<DeploymentConflict> DetectConflicts(
        IReadOnlyList<DeploymentItem> items,
        Dictionary<string, List<string>> affects,
        Dictionary<string, List<string>> hooks,
        Dictionary<string, ModManifest> enabledManifests)
    {
        var conflicts = new List<DeploymentConflict>();
        foreach (var group in items.GroupBy(i => i.TargetPath, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            conflicts.Add(new DeploymentConflict(
                "same-target",
                $"Multiple enabled mods deploy to the same target path: {group.Key}",
                group.ToList(),
                BlocksDeployment: true));
        }

        foreach (var group in affects.Where(g => g.Value.Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
        {
            conflicts.Add(new DeploymentConflict(
                "same-asset",
                $"Multiple enabled mods declare the same affected asset: {group.Key}",
                items.Where(i => group.Value.Contains(i.ModId, StringComparer.OrdinalIgnoreCase)).ToList(),
                BlocksDeployment: false));
        }

        foreach (var group in hooks.Where(g => g.Value.Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
        {
            conflicts.Add(new DeploymentConflict(
                "same-hook",
                $"Multiple enabled mods declare the same UE4SS hook: {group.Key}",
                items.Where(i => group.Value.Contains(i.ModId, StringComparer.OrdinalIgnoreCase)).ToList(),
                BlocksDeployment: false));
        }

        foreach (var manifest in enabledManifests.Values)
        {
            foreach (var declaredConflict in manifest.Conflicts)
            {
                if (!enabledManifests.ContainsKey(declaredConflict))
                {
                    continue;
                }

                conflicts.Add(new DeploymentConflict(
                    "declared-conflict",
                    $"{manifest.Id} declares a conflict with enabled mod {declaredConflict}.",
                    items.Where(i => i.ModId.Equals(manifest.Id, StringComparison.OrdinalIgnoreCase) ||
                                     i.ModId.Equals(declaredConflict, StringComparison.OrdinalIgnoreCase)).ToList(),
                    BlocksDeployment: true));
            }
        }

        return conflicts;
    }

    private static void AddMulti(Dictionary<string, List<string>> map, string key, string value)
    {
        if (!map.TryGetValue(key, out var values))
        {
            values = [];
            map[key] = values;
        }

        values.Add(value);
    }
}
