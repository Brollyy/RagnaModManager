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
        var allProfileMods = _database.GetProfileMods(profile.Id);
        var profileMods = allProfileMods.Where(m => m.Enabled).OrderBy(m => m.Priority).ThenBy(m => m.ModId).ToList();
        var mods = _database.GetMods().ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        var items = new List<DeploymentItem>();
        var warnings = new List<string>();
        var affects = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var hooks = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var enabledManifests = new Dictionary<string, ModManifest>(StringComparer.OrdinalIgnoreCase);
        var requirementConflicts = new List<DeploymentConflict>();

        foreach (var profileMod in profileMods)
        {
            if (!mods.TryGetValue(profileMod.ModId, out var currentMod))
            {
                warnings.Add($"Profile references missing mod '{profileMod.ModId}'.");
                continue;
            }

            var mod = string.IsNullOrWhiteSpace(profileMod.Version) ? currentMod : _database.GetMod(profileMod.ModId, profileMod.Version);
            if (mod is null)
            {
                warnings.Add($"Profile pins missing version '{profileMod.ModId} {profileMod.Version}'.");
                requirementConflicts.Add(new DeploymentConflict(
                    "profile-version",
                    $"Profile pins {profileMod.ModId} to version {profileMod.Version}, but that version is not installed.",
                    [], BlocksDeployment: true, RelatedModId: profileMod.ModId));
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

                    var item = new DeploymentItem(manifest.Id, expandedSource, target, "copy", file.Type, file.EffectiveModFolder);
                    items.Add(item);
                    manifestItems.Add(item);
                }
            }

            requirementConflicts.AddRange(_rules.GetRequirementConflicts(gameRoot, manifest, manifestItems));
        }

        var expandedItemCount = items.Count;
        items = items
            .GroupBy(
                item => string.Join('\0', item.ModId, Path.GetFullPath(item.SourcePath), Path.GetFullPath(item.TargetPath), item.Method),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        if (items.Count < expandedItemCount)
        {
            warnings.Add($"Coalesced {expandedItemCount - items.Count} identical legacy deployment entries.");
        }

        var selectedMods = allProfileMods
            .Select(p => (Profile: p, Mod: mods.TryGetValue(p.ModId, out var current) ? (string.IsNullOrWhiteSpace(p.Version) ? current : _database.GetMod(p.ModId, p.Version)) : null))
            .Where(x => x.Mod is not null)
            .ToDictionary(x => x.Profile.ModId, x => x.Mod!, StringComparer.OrdinalIgnoreCase);
        var dependencyConflicts = DetectDependencyConflicts(profileMods, selectedMods, enabledManifests);
        var (loadOrder, cycle) = BuildDependencyOrder(profileMods, enabledManifests);
        if (cycle is not null)
        {
            dependencyConflicts.Add(new DeploymentConflict(
                "dependency-cycle",
                $"Enabled mods contain a dependency cycle: {cycle}.",
                items.Where(i => enabledManifests.ContainsKey(i.ModId)).ToList(),
                BlocksDeployment: true));
        }

        var conflicts = DetectConflicts(items, affects, hooks, enabledManifests)
            .Concat(requirementConflicts)
            .Concat(dependencyConflicts)
            .ToList();
        return Result<DeploymentPlan>.Ok(new DeploymentPlan(profile.Id, items, conflicts, warnings, loadOrder));
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

    private static List<DeploymentConflict> DetectDependencyConflicts(
        IReadOnlyList<ProfileModRecord> profileMods,
        IReadOnlyDictionary<string, ModRecord> installedMods,
        IReadOnlyDictionary<string, ModManifest> enabledManifests)
    {
        var conflicts = new List<DeploymentConflict>();
        var enabled = profileMods.Select(m => m.ModId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var manifest in enabledManifests.Values)
        {
            foreach (var dependency in manifest.Dependencies)
            {
                if (!installedMods.TryGetValue(dependency.Key, out var installed))
                {
                    conflicts.Add(new DeploymentConflict(
                        "missing-dependency",
                        $"{manifest.Id} requires {dependency.Key} {dependency.Value}, but it is not installed.",
                        [],
                        BlocksDeployment: true,
                        RelatedModId: dependency.Key));
                }
                else if (!enabled.Contains(dependency.Key))
                {
                    conflicts.Add(new DeploymentConflict(
                        "disabled-dependency",
                        $"{manifest.Id} requires {dependency.Key} {dependency.Value}, but it is disabled in this profile.",
                        [],
                        BlocksDeployment: true,
                        RelatedModId: dependency.Key));
                }
                else if (!VersionRequirement.IsSatisfied(dependency.Value, installed.Version))
                {
                    conflicts.Add(new DeploymentConflict(
                        "dependency-version",
                        $"{manifest.Id} requires {dependency.Key} {dependency.Value}, installed version is {installed.Version}.",
                        [],
                        BlocksDeployment: true,
                        RelatedModId: dependency.Key));
                }
            }
        }

        return conflicts;
    }

    private static (IReadOnlyList<string> Order, string? Cycle) BuildDependencyOrder(
        IReadOnlyList<ProfileModRecord> profileMods,
        IReadOnlyDictionary<string, ModManifest> manifests)
    {
        var ordered = new List<string>();
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new List<string>();
        string? cycle = null;

        bool Visit(string id)
        {
            if (visited.Contains(id)) return true;
            if (!visiting.Add(id))
            {
                var start = stack.FindIndex(value => value.Equals(id, StringComparison.OrdinalIgnoreCase));
                cycle = string.Join(" -> ", stack.Skip(Math.Max(0, start)).Append(id));
                return false;
            }

            stack.Add(id);
            if (manifests.TryGetValue(id, out var manifest))
            {
                foreach (var dependency in manifest.Dependencies.Keys.Order(StringComparer.OrdinalIgnoreCase))
                {
                    if (manifests.ContainsKey(dependency) && !Visit(dependency)) return false;
                }
            }

            stack.RemoveAt(stack.Count - 1);
            visiting.Remove(id);
            visited.Add(id);
            ordered.Add(id);
            return true;
        }

        foreach (var mod in profileMods.OrderBy(m => m.Priority).ThenBy(m => m.ModId, StringComparer.OrdinalIgnoreCase))
        {
            if (!Visit(mod.ModId)) break;
        }

        return (ordered, cycle);
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
