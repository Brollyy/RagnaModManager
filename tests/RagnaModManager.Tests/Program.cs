using System.IO.Compression;
using System.Net;
using System.Text.Json;
using RagnaModManager.Core.Database;
using RagnaModManager.Core.Compatibility;
using RagnaModManager.Core.Deployment;
using RagnaModManager.Core.Logging;
using RagnaModManager.Core.Manifests;
using RagnaModManager.Core.Packages;
using RagnaModManager.Core.Platform;
using RagnaModManager.Platform.Folders;
using RagnaModManager.Platform.Proton;
using RagnaModManager.Platform.Steam;
using RagnaModManager.Ragnarock.Compatibility;
using RagnaModManager.Ragnarock.DeploymentRules;
using RagnaModManager.Ragnarock.Detection;
using RagnaModManager.Ragnarock.Ue4ss;

var tests = new (string Name, Action Body)[]
{
    ("manifest validation rejects unsafe paths", ManifestRejectsUnsafePaths),
    ("package inspector previews valid package", PackageInspectorPreviewsValidPackage),
    ("package inspector rejects zip slip", PackageInspectorRejectsZipSlip),
    ("package importer rejects zip slip", PackageImporterRejectsZipSlip),
    ("import enable deploy disable cleanup cycle works", ImportDeployDisableCleanupCycle),
    ("deployment rollback restores latest backup", DeploymentRollbackRestoresLatestBackup),
    ("deployment rollback ignores other profile backups", DeploymentRollbackIgnoresOtherProfileBackups),
    ("modified deployed files block overwrite", ModifiedDeployedFilesBlockOverwrite),
    ("existing managed mod files can be repaired", ExistingManagedModFilesCanBeRepaired),
    ("unmanaged target files require reconciliation", UnmanagedTargetFilesRequireReconciliation),
    ("removing a mod clears its installation and ue4ss registration", RemovingModClearsInstallation),
    ("switching profiles redeploys from scratch", SwitchingProfilesRedeploysFromScratch),
    ("same target conflicts warn before deployment", SameTargetConflictWarnsBeforeDeployment),
    ("identical legacy entries are coalesced", IdenticalLegacyEntriesAreCoalesced),
    ("declared mod conflicts block deployment", DeclaredModConflictsBlockDeployment),
    ("manager version requirements block unsupported mods", ManagerVersionRequirementBlocksUnsupportedMods),
    ("ue4ss version requirements block unsupported runtime", Ue4ssVersionRequirementBlocksUnsupportedRuntime),
    ("mod dependencies block invalid profiles and order ue4ss mods", ModDependenciesBlockAndOrder),
    ("deployment preserves unmanaged ue4ss mods.txt entries", DeploymentPreservesUnmanagedUe4ssEntries),
    ("dependency cycles block deployment", DependencyCyclesBlockDeployment),
    ("active root ue4ss layout is preferred", ActiveRootUe4ssLayoutIsPreferred),
    ("steam libraryfolders vdf parser finds library paths", SteamLibraryVdfParserFindsLibraryPaths),
    ("folder opener builds platform command", FolderOpenerBuildsPlatformCommand),
    ("launch plan uses optional arguments", LaunchPlanUsesOptionalArguments),
    ("compatibility checker reports usable test install", CompatibilityCheckerReportsUsableInstall),
    ("ue4ss zip install validates and maps layout", Ue4ssInstallMapsLayout),
    ("ue4ss release service caches installs and rolls back versions", Ue4ssReleaseServiceCachesInstallsAndRollsBackVersions),
    ("official catalog loads and orders releases", OfficialCatalogLoadsAndOrdersReleases),
    ("official catalog verifies and imports package", OfficialCatalogVerifiesAndImportsPackage),
    ("official catalog conflicts must be declared by package", OfficialCatalogConflictsMustBeDeclaredByPackage),
    ("official install downloads catalog dependencies first", OfficialInstallDownloadsDependencies),
    ("semantic versions order prereleases correctly", SemanticVersionsOrderPrereleases),
    ("profiles export and import version pins", ProfilesExportAndImportVersionPins),
    ("applied profile snapshots restore unapplied changes", AppliedProfileSnapshotsRestoreChanges),
    ("multiple mod versions can be installed and selected by profile", MultipleModVersionsCanBeSelected)
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        test.Body();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}");
    }
}

return failed == 0 ? 0 : 1;

static void ManifestRejectsUnsafePaths()
{
    var manifest = TestHelpers.ValidManifest("bad-path");
    manifest.Files[0].Source = "../escape.lua";
    var result = ManifestValidator.Validate(manifest);
    Assert(!result.Success, "unsafe manifest source should be rejected");
}

static void PackageImporterRejectsZipSlip()
{
    using var env = TestEnv.Create();
    var zip = Path.Combine(env.Root, "bad.rmod");
    using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
    {
        TestHelpers.AddEntry(archive, "manifest.json", JsonSerializer.Serialize(TestHelpers.ValidManifest("zip-slip"), TestHelpers.JsonOptions));
        TestHelpers.AddEntry(archive, "../evil.txt", "bad");
    }

    var result = env.Importer.Import(zip);
    Assert(!result.Success, "zip-slip package should fail import");
}

static void PackageInspectorPreviewsValidPackage()
{
    using var env = TestEnv.Create();
    var package = env.CreatePackage("inspect-mod", manifest =>
    {
        manifest.Files = [new ManifestFile { Type = "pak", Source = "InspectMod_P.pak", LoadOrder = 400 }];
    }, files => files["InspectMod_P.pak"] = "pak");

    var result = new PackageInspector().Inspect(package);
    Assert(result.Success, result.Error ?? "inspection failed");
    Assert(result.Value!.Manifest.Id == "inspect-mod", "inspection should expose manifest");
    Assert(result.Value.Entries.Any(e => e.Path == "InspectMod_P.pak"), "inspection should list package entries");
}

static void PackageInspectorRejectsZipSlip()
{
    using var env = TestEnv.Create();
    var zip = Path.Combine(env.Root, "bad-inspect.rmod");
    using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
    {
        TestHelpers.AddEntry(archive, "manifest.json", JsonSerializer.Serialize(TestHelpers.ValidManifest("bad-inspect"), TestHelpers.JsonOptions));
        TestHelpers.AddEntry(archive, "../../escape.txt", "bad");
    }

    var result = new PackageInspector().Inspect(zip);
    Assert(!result.Success, "inspector should reject zip-slip package");
}

static void OfficialCatalogLoadsAndOrdersReleases()
{
    const string json = """
    {"schemaVersion":"1","repository":"rmm-registry","mods":[{"id":"demo-mod","name":"Demo","releases":[{"version":"1.0.0","packageUrl":"https://example.test/old.rmod","sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},{"version":"1.2.0","packageUrl":"https://example.test/new.rmod","sha256":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"}]}]}
    """;
    using var http = new HttpClient(new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) }));
    using var env = TestEnv.Create();
    var service = new OfficialCatalogService(env.Paths, env.Database, env.Logger, http);
    var result = service.LoadAsync().GetAwaiter().GetResult();
    Assert(result.Success, result.Error ?? "catalog load failed");
    Assert(result.Value!.Mods[0].Latest!.Version == "1.2.0", "catalog should select the highest release version");
}

static void SemanticVersionsOrderPrereleases()
{
    Assert(SemanticVersion.Compare("1.2.0-beta.2", "1.2.0-beta.10") < 0, "numeric prerelease identifiers should be numeric");
    Assert(SemanticVersion.Compare("1.2.0", "1.2.0-rc.1") > 0, "stable should follow prerelease");
}

static void ProfilesExportAndImportVersionPins()
{
    using var env = TestEnv.Create();
    env.Database.CreateProfile("stable", "Stable");
    env.Database.SetProfileMod("stable", "demo", true, 5, "1.0.0");
    var path = Path.Combine(env.Root, "stable.json");
    Assert(env.Database.ExportProfile("stable", path).Success, "profile export should succeed");
    var imported = env.Database.ImportProfile(path, "copy", "Copy");
    Assert(imported.Success, imported.Error ?? "profile import should succeed");
    Assert(env.Database.GetProfileMods("copy").Single().Version == "1.0.0", "profile pin should survive export/import");
}

static void AppliedProfileSnapshotsRestoreChanges()
{
    using var env = TestEnv.Create();
    env.Database.SetProfileMod("default", "demo", true, 5, "1.0.0");
    env.Database.CaptureAppliedProfileSnapshot("default");
    env.Database.SetProfileMod("default", "demo", false, 0, "2.0.0");
    env.Database.SetProfileMod("default", "new-mod", true, 10, "1.0.0");

    var restored = env.Database.RestoreAppliedProfileSnapshot("default");
    Assert(restored.Success, restored.Error ?? "snapshot restore should succeed");
    var mods = env.Database.GetProfileMods("default");
    Assert(mods.Count == 1, "restore should remove changes that were not applied");
    Assert(mods[0].ModId == "demo" && mods[0].Enabled && mods[0].Priority == 5 && mods[0].Version == "1.0.0", "restore should recover the applied state");
}

static void MultipleModVersionsCanBeSelected()
{
    using var env = TestEnv.Create();
    var game = env.CreateGame();
    env.InstallFakeUe4ss(game);
    env.Database.UpsertGame(new GameRecord("ragnarock", "Ragnarock", game, null, null, "test"));
    var v1 = env.CreatePackage("versioned", manifest => { manifest.Version = "1.0.0"; manifest.Files = [new ManifestFile { Type = "ue4ss-lua", Source = "Scripts/main.lua", ModFolder = "Versioned" }]; }, files => files["Scripts/main.lua"] = "v1");
    Assert(env.Importer.Import(v1).Success, "first version should import");
    File.Move(v1, v1 + ".old");
    var v2 = env.CreatePackage("versioned", manifest => { manifest.Version = "2.0.0"; manifest.Files = [new ManifestFile { Type = "ue4ss-lua", Source = "Scripts/main.lua", ModFolder = "Versioned" }]; }, files => files["Scripts/main.lua"] = "v2");
    Assert(env.Importer.Import(v2).Success, "second version should import");
    Assert(env.Database.GetModVersions("versioned").Count == 2, "both versions should remain installed");
    env.Database.SetProfileMod("default", "versioned", true, 0, "1.0.0");
    Assert(env.DeploymentService().Deploy(game).Success, "pinned first version should deploy");
    var target = Path.Combine(game, "Ragnarock", "Binaries", "Win64", "ue4ss", "Mods", "Versioned", "scripts", "main.lua");
    Assert(File.ReadAllText(target) == "v1", "profile should deploy its selected version");
    env.Database.SetProfileMod("default", "versioned", true, 0, "2.0.0");
    Assert(env.DeploymentService().Deploy(game).Success, "pinned second version should deploy");
    Assert(File.ReadAllText(target) == "v2", "profile should switch to its selected version");
}

static void OfficialCatalogVerifiesAndImportsPackage()
{
    using var env = TestEnv.Create();
    var source = env.CreatePackage("official-demo", _ => { }, files => files["Scripts/main.lua"] = "print('official')");
    var bytes = File.ReadAllBytes(source);
    var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
    using var http = new HttpClient(new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
    var service = new OfficialCatalogService(env.Paths, env.Database, env.Logger, http);
    var mod = new CatalogMod("official-demo", "Official Demo", "Tester", null, []);
    var release = new CatalogRelease("1.0.0", "https://example.test/demo.rmod", hash);
    var result = service.DownloadAndImportAsync(mod, release).GetAwaiter().GetResult();
    Assert(result.Success, result.Error ?? "official package import failed");
    Assert(env.Database.GetMod("official-demo") is not null, "verified official package should be installed");
    Assert(env.Database.GetProfileMods("default").Single(mod => mod.ModId == "official-demo").Enabled, "verified official package should start enabled");
}

static void OfficialCatalogConflictsMustBeDeclaredByPackage()
{
    using var env = TestEnv.Create();
    var source = env.CreatePackage("official-demo", _ => { }, files => files["Scripts/main.lua"] = "print('official')");
    var bytes = File.ReadAllBytes(source);
    var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
    using var http = new HttpClient(new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
    var service = new OfficialCatalogService(env.Paths, env.Database, env.Logger, http);
    var mod = new CatalogMod("official-demo", "Official Demo", "Tester", null, [], Conflicts: ["other-mod"]);
    var release = new CatalogRelease("1.0.0", "https://example.test/demo.rmod", hash);
    var result = service.DownloadAndImportAsync(mod, release).GetAwaiter().GetResult();
    Assert(!result.Success, "official package missing a catalog conflict should be rejected");
    Assert(result.Error!.Contains("missing registry conflicts", StringComparison.Ordinal), "conflict mismatch should explain the missing declaration");
}

static void OfficialInstallDownloadsDependencies()
{
    using var env = TestEnv.Create();
    var api = File.ReadAllBytes(env.CreatePackage("catalog-api", manifest => manifest.Files = [new ManifestFile { Type = "loose-file", Source = "Scripts/api.lua" }], files => files["Scripts/api.lua"] = "api"));
    var vote = File.ReadAllBytes(env.CreatePackage("catalog-vote", manifest => manifest.Files = [new ManifestFile { Type = "loose-file", Source = "Scripts/vote.lua" }], files => files["Scripts/vote.lua"] = "vote"));
    var apiHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(api));
    var voteHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(vote));
    var catalogJson = $"{{\"schemaVersion\":\"1\",\"repository\":\"rmm-registry\",\"mods\":[{{\"id\":\"catalog-api\",\"name\":\"API\",\"releases\":[{{\"version\":\"1.0.0\",\"packageUrl\":\"https://example.test/api.rmod\",\"sha256\":\"{apiHash}\"}}]}},{{\"id\":\"catalog-vote\",\"name\":\"Vote\",\"dependencies\":{{\"catalog-api\":\">=1.0.0\"}},\"releases\":[{{\"version\":\"1.0.0\",\"packageUrl\":\"https://example.test/vote.rmod\",\"sha256\":\"{voteHash}\"}}]}}]}}";
    using var http = new HttpClient(new FakeHttpHandler(request =>
    {
        if (request.RequestUri!.AbsoluteUri.EndsWith("api.rmod", StringComparison.Ordinal)) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(api) };
        if (request.RequestUri.AbsoluteUri.EndsWith("vote.rmod", StringComparison.Ordinal)) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(vote) };
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(catalogJson) };
    }));
    var service = new OfficialCatalogService(env.Paths, env.Database, env.Logger, http);
    var catalog = service.LoadAsync().GetAwaiter().GetResult();
    Assert(catalog.Success, catalog.Error ?? "catalog with dependencies should load");
    var voteMod = catalog.Value!.Mods.Single(m => m.Id == "catalog-vote");
    var result = service.DownloadAndImportAsync(voteMod, voteMod.Latest!).GetAwaiter().GetResult();
    Assert(result.Success, result.Error ?? "dependent official package should install");
    Assert(env.Database.GetMod("catalog-api") is not null, "dependency should be installed automatically");
    Assert(env.Database.GetMod("catalog-vote") is not null, "requested mod should be installed");
    Assert(env.Database.GetProfileMods("default").Where(mod => mod.ModId is "catalog-api" or "catalog-vote").All(mod => mod.Enabled), "official mods and dependencies should start enabled");
}

static void ImportDeployDisableCleanupCycle()
{
    using var env = TestEnv.Create();
    var game = env.CreateGame();
    env.InstallFakeUe4ss(game);
    env.Database.UpsertGame(new GameRecord("ragnarock", "Ragnarock", game, null, null, "test"));

    var package = env.CreatePackage("better-hit-feedback", manifest =>
    {
        manifest.Files =
        [
            new ManifestFile { Type = "ue4ss-lua", Source = "Scripts/", ModFolder = "BetterHitFeedback" },
            new ManifestFile { Type = "pak", Source = "BetterHitFeedback_P.pak", LoadOrder = 500 },
            new ManifestFile { Type = "config", Source = "Config/settings.json", Target = "Mods/BetterHitFeedback/settings.json" }
        ];
    },
    files =>
    {
        files["Scripts/main.lua"] = "print('ok')";
        files["BetterHitFeedback_P.pak"] = "pak";
        files["Config/settings.json"] = "{}";
    });

    var import = env.Importer.Import(package);
    Assert(import.Success, import.Error ?? "import failed");
    env.Database.SetProfileMod("default", "better-hit-feedback", enabled: true, priority: 500);
    var profileJson = File.ReadAllText(Path.Combine(env.Paths.Profiles, "default.json"));
    Assert(profileJson.Contains("\"id\": \"better-hit-feedback\"", StringComparison.Ordinal), "profile JSON should include enabled mod");

    var service = env.DeploymentService();
    var deploy = service.Deploy(game);
    Assert(deploy.Success, deploy.Error ?? "deploy failed");

    var luaTarget = Path.Combine(game, "Ragnarock", "Binaries", "Win64", "ue4ss", "Mods", "BetterHitFeedback", "scripts", "main.lua");
    var pakTarget = Path.Combine(game, "Ragnarock", "Content", "Paks", "~mods", "0500_better-hit-feedback_BetterHitFeedback_P.pak");
    var configTarget = Path.Combine(game, "Ragnarock", "Binaries", "Win64", "ue4ss", "Mods", "BetterHitFeedback", "settings.json");
    Assert(File.Exists(luaTarget), "lua target should exist");
    Assert(File.Exists(pakTarget), "pak target should exist");
    Assert(File.Exists(configTarget), "config target should exist");
    Assert(File.Exists(env.Paths.CurrentDeploymentPath), "deployment/current.json should exist");

    env.Database.SetProfileMod("default", "better-hit-feedback", enabled: false, priority: 500);
    var cleanup = service.Deploy(game);
    Assert(cleanup.Success, cleanup.Error ?? "cleanup deploy failed");
    Assert(File.Exists(luaTarget), "disabled UE4SS lua file should be retained");
    Assert(!File.Exists(pakTarget), "disabled pak file should be removed");
    Assert(File.Exists(configTarget), "disabled UE4SS config file should be retained");
    var modsTxt = File.ReadAllText(Path.Combine(game, "Ragnarock", "Binaries", "Win64", "ue4ss", "Mods", "mods.txt"));
    Assert(modsTxt.Contains("BetterHitFeedback : 0", StringComparison.Ordinal), "disabled UE4SS mod should be marked disabled in mods.txt");

    var reset = service.ResetDeployment();
    Assert(reset.Success, reset.Error ?? "reset failed");
    Assert(!File.Exists(luaTarget), "reset should remove retained UE4SS lua file");
    Assert(!File.Exists(configTarget), "reset should remove retained UE4SS config file");
}

static void DeploymentRollbackRestoresLatestBackup()
{
    using var env = TestEnv.Create();
    var game = env.CreateGame();
    env.Database.UpsertGame(new GameRecord("ragnarock", "Ragnarock", game, null, null, "test"));

    var package = env.CreatePackage("rollback-mod", manifest =>
    {
        manifest.Files = [new ManifestFile { Type = "pak", Source = "RollbackMod_P.pak", LoadOrder = 501 }];
    }, files => files["RollbackMod_P.pak"] = "pak");
    var import = env.Importer.Import(package);
    Assert(import.Success, import.Error ?? "import failed");
    env.Database.SetProfileMod("default", "rollback-mod", enabled: true, priority: 0);

    var service = env.DeploymentService();
    var deploy = service.Deploy(game);
    Assert(deploy.Success, deploy.Error ?? "deploy failed");
    var target = Path.Combine(game, "Ragnarock", "Content", "Paks", "~mods", "0501_rollback-mod_RollbackMod_P.pak");
    Assert(File.Exists(target), "deployed file should exist before cleanup deploy");

    env.Database.SetProfileMod("default", "rollback-mod", enabled: false, priority: 0);
    var cleanup = service.Deploy(game);
    Assert(cleanup.Success, cleanup.Error ?? "cleanup deploy failed");
    Assert(!File.Exists(target), "cleanup deploy should remove current file");

    var rollback = service.RollbackLatest();
    Assert(rollback.Success, rollback.Error ?? "rollback failed");
    Assert(File.Exists(target), "rollback should restore previous deployed file");
    Assert(env.Database.GetDeployedFiles("default").Any(f => f.TargetPath == target), "rollback should restore deployed_files rows");
}

static void DeploymentRollbackIgnoresOtherProfileBackups()
{
    using var env = TestEnv.Create();
    var game = env.CreateGame();
    env.Database.UpsertGame(new GameRecord("ragnarock", "Ragnarock", game, null, null, "test"));
    env.Database.CreateProfile("development", "Development");

    var defaultPackage = env.CreatePackage("default-rollback-mod", manifest =>
    {
        manifest.Files = [new ManifestFile { Type = "pak", Source = "DefaultRollback_P.pak", LoadOrder = 401 }];
    }, files => files["DefaultRollback_P.pak"] = "default");
    var devPackage = env.CreatePackage("dev-rollback-mod", manifest =>
    {
        manifest.Files = [new ManifestFile { Type = "pak", Source = "DevRollback_P.pak", LoadOrder = 402 }];
    }, files => files["DevRollback_P.pak"] = "dev");

    Assert(env.Importer.Import(defaultPackage).Success, "default package import should succeed");
    Assert(env.Importer.Import(devPackage).Success, "development package import should succeed");
    env.Database.SetProfileMod("default", "default-rollback-mod", true, 0);
    env.Database.SetProfileMod("development", "dev-rollback-mod", true, 0);

    var service = env.DeploymentService();
    Assert(service.Deploy(game).Success, "default deploy should succeed");
    var defaultTarget = Path.Combine(game, "Ragnarock", "Content", "Paks", "~mods", "0401_default-rollback-mod_DefaultRollback_P.pak");
    Assert(File.Exists(defaultTarget), "default target should be deployed");

    env.Database.SetActiveProfile("development");
    Assert(service.Deploy(game).Success, "development deploy should succeed");
    var devTarget = Path.Combine(game, "Ragnarock", "Content", "Paks", "~mods", "0402_dev-rollback-mod_DevRollback_P.pak");
    Assert(!File.Exists(defaultTarget), "development deploy should remove default target");
    Assert(File.Exists(devTarget), "development target should be deployed");

    env.Database.SetActiveProfile("default");
    env.Database.SetProfileMod("default", "default-rollback-mod", false, 0);
    Assert(service.Deploy(game).Success, "default cleanup deploy should succeed");
    Assert(!File.Exists(defaultTarget), "default cleanup should remove default target");
    Assert(!File.Exists(devTarget), "default cleanup should remove development target");

    var rollback = service.RollbackLatest("default");
    Assert(rollback.Success, rollback.Error ?? "default rollback should succeed");
    Assert(File.Exists(defaultTarget), "default rollback should restore default target");
    Assert(!File.Exists(devTarget), "default rollback should not restore development target");
    Assert(env.Database.GetDeployedFiles("default").All(f => f.ProfileId == "default"), "default rollback should restore default profile rows");
}

static void ModifiedDeployedFilesBlockOverwrite()
{
    using var env = TestEnv.Create();
    var game = env.CreateGame();
    env.Database.UpsertGame(new GameRecord("ragnarock", "Ragnarock", game, null, null, "test"));

    var package = env.CreatePackage("overwrite-mod", manifest =>
    {
        manifest.Files = [new ManifestFile { Type = "pak", Source = "OverwriteMod_P.pak", LoadOrder = 502 }];
    }, files => files["OverwriteMod_P.pak"] = "pak");
    var import = env.Importer.Import(package);
    Assert(import.Success, import.Error ?? "import failed");
    env.Database.SetProfileMod("default", "overwrite-mod", enabled: true, priority: 0);

    var service = env.DeploymentService();
    var deploy = service.Deploy(game);
    Assert(deploy.Success, deploy.Error ?? "deploy failed");
    var target = Path.Combine(game, "Ragnarock", "Content", "Paks", "~mods", "0502_overwrite-mod_OverwriteMod_P.pak");
    File.WriteAllText(target, "changed outside manager");

    var redeploy = service.Deploy(game);
    Assert(!redeploy.Success, "redeploy should fail when an owned target was modified outside the manager");
}

static void ExistingManagedModFilesCanBeRepaired()
{
    using var env = TestEnv.Create();
    var game = env.CreateGame();
    env.InstallFakeUe4ss(game);
    env.Database.UpsertGame(new GameRecord("ragnarock", "Ragnarock", game, null, null, "test"));

    var package = env.CreatePackage("repairable-mod", manifest =>
    {
        manifest.Files = [new ManifestFile { Type = "ue4ss-lua", Source = "Scripts/main.lua", ModFolder = "RepairableMod" }];
    }, files => files["Scripts/main.lua"] = "print('new')");
    Assert(env.Importer.Import(package).Success, "repairable package import should succeed");
    env.Database.SetProfileMod("default", "repairable-mod", true, 0);

    var target = Path.Combine(game, "Ragnarock", "Binaries", "Win64", "ue4ss", "Mods", "RepairableMod", "scripts", "main.lua");
    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    File.WriteAllText(target, "print('old')");

    var deploy = env.DeploymentService().Deploy(game);
    Assert(deploy.Success, deploy.Error ?? "managed existing file should be repairable");
    Assert(File.ReadAllText(target) == "print('new')", "repair should replace the existing managed mod file");
}

static void UnmanagedTargetFilesRequireReconciliation()
{
    using var env = TestEnv.Create();
    var game = env.CreateGame();
    env.Database.UpsertGame(new GameRecord("ragnarock", "Ragnarock", game, null, null, "test"));

    var package = env.CreatePackage("reconcile-mod", manifest =>
    {
        manifest.Files = [new ManifestFile { Type = "pak", Source = "ReconcileMod_P.pak", LoadOrder = 503 }];
    }, files => files["ReconcileMod_P.pak"] = "manager package");
    Assert(env.Importer.Import(package).Success, "reconciliation package should import");
    env.Database.SetProfileMod("default", "reconcile-mod", true, 0);
    var target = Path.Combine(game, "Ragnarock", "Content", "Paks", "~mods", "0503_reconcile-mod_ReconcileMod_P.pak");
    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    File.WriteAllText(target, "existing game file");

    var service = env.DeploymentService();
    var preview = service.Preview(game);
    Assert(preview.Success, preview.Error ?? "reconciliation preview failed");
    Assert(preview.Value!.Conflicts.Any(c => c.Kind == "unmanaged-file" && c.BlocksDeployment), "unmanaged target should be reported before deployment");
    Assert(!service.Deploy(game).Success, "unmanaged target should require consent");
    var reconciled = service.Deploy(game, allowUnmanagedFiles: true);
    Assert(reconciled.Success, reconciled.Error ?? "consented reconciliation should succeed");
    Assert(File.ReadAllText(target) == "manager package", "reconciliation should overwrite the selected target");
}

static void RemovingModClearsInstallation()
{
    using var env = TestEnv.Create();
    var game = env.CreateGame();
    env.InstallFakeUe4ss(game);
    env.Database.UpsertGame(new GameRecord("ragnarock", "Ragnarock", game, null, null, "test"));
    var package = env.CreatePackage("remove-mod", manifest =>
    {
        manifest.Files = [new ManifestFile { Type = "ue4ss-lua", Source = "Scripts/main.lua", ModFolder = "RagnaCustomsApi" }];
    }, files => files["Scripts/main.lua"] = "print('remove')");
    Assert(env.Importer.Import(package).Success, "remove package import should succeed");
    Assert(env.Database.GetMod("remove-mod") is not null, "remove package should be installed");
    env.Database.SetProfileMod("default", "remove-mod", true, 0);
    Assert(env.DeploymentService().Deploy(game).Success, "package should deploy before removal");
    var modsRoot = Path.Combine(game, "Ragnarock", "Binaries", "Win64", "ue4ss", "Mods");
    var modsFile = Path.Combine(modsRoot, "mods.txt");
    var installedFile = Path.Combine(modsRoot, "RagnaCustomsApi", "scripts", "main.lua");
    Assert(File.Exists(installedFile), "package file should be deployed");
    Assert(File.ReadAllLines(modsFile).Contains("RagnaCustomsApi : 1"), "UE4SS should list the deployed folder as enabled");

    var result = env.DeploymentService().RemoveMod("remove-mod", game);
    Assert(result.Success, result.Error ?? "mod removal should succeed");
    Assert(env.Database.GetMod("remove-mod") is null, "removed mod should not remain in the database");
    Assert(!env.Database.GetProfileMods("default").Any(m => m.ModId == "remove-mod"), "removed mod should leave profile entries");
    Assert(!File.Exists(installedFile), "removal should delete the deployed package file");
    Assert(!Directory.Exists(Path.Combine(modsRoot, "RagnaCustomsApi")), "removal should delete empty package folders");
    Assert(!File.ReadAllLines(modsFile).Any(line => line.StartsWith("RagnaCustomsApi :", StringComparison.OrdinalIgnoreCase)), "removal should clear the UE4SS registration by deployed folder name");
}

static void SwitchingProfilesRedeploysFromScratch()
{
    using var env = TestEnv.Create();
    var game = env.CreateGame();
    env.Database.UpsertGame(new GameRecord("ragnarock", "Ragnarock", game, null, null, "test"));

    var defaultPackage = env.CreatePackage("default-profile-mod", manifest =>
    {
        manifest.Files = [new ManifestFile { Type = "pak", Source = "Default_P.pak", LoadOrder = 300 }];
    }, files => files["Default_P.pak"] = "default");
    var devPackage = env.CreatePackage("dev-profile-mod", manifest =>
    {
        manifest.Files = [new ManifestFile { Type = "pak", Source = "Dev_P.pak", LoadOrder = 301 }];
    }, files => files["Dev_P.pak"] = "dev");

    Assert(env.Importer.Import(defaultPackage).Success, "default profile package import should succeed");
    env.Database.SetProfileMod("default", "default-profile-mod", true, 0);
    var service = env.DeploymentService();
    Assert(service.Deploy(game).Success, "default profile deploy should succeed");
    var defaultTarget = Path.Combine(game, "Ragnarock", "Content", "Paks", "~mods", "0300_default-profile-mod_Default_P.pak");
    Assert(File.Exists(defaultTarget), "default profile file should be deployed");

    env.Database.CreateProfile("development", "Development");
    Assert(env.Importer.Import(devPackage).Success, "development profile package import should succeed");
    env.Database.SetProfileMod("development", "dev-profile-mod", true, 0);
    env.Database.SetActiveProfile("development");
    Assert(service.Deploy(game).Success, "development profile deploy should succeed");
    var devTarget = Path.Combine(game, "Ragnarock", "Content", "Paks", "~mods", "0301_dev-profile-mod_Dev_P.pak");
    Assert(!File.Exists(defaultTarget), "switching profiles should remove previous profile deployed file");
    Assert(File.Exists(devTarget), "development profile file should be deployed");
}

static void SameTargetConflictWarnsBeforeDeployment()
{
    using var env = TestEnv.Create();
    var game = env.CreateGame();
    env.Database.UpsertGame(new GameRecord("ragnarock", "Ragnarock", game, null, null, "test"));

    foreach (var id in new[] { "first-mod", "second-mod" })
    {
        var package = env.CreatePackage(id, manifest =>
        {
            manifest.Files = [new ManifestFile { Type = "loose-file", Source = "shared.txt", Target = "Ragnarock/Content/Paks/~mods/shared.txt" }];
        }, files => files["shared.txt"] = id);
        var import = env.Importer.Import(package);
        Assert(import.Success, import.Error ?? "import failed");
        env.Database.SetProfileMod("default", id, enabled: true, priority: 0);
    }

    var preview = env.DeploymentService().Preview(game);
    Assert(preview.Success, preview.Error ?? "preview failed");
    Assert(preview.Value!.CanDeploy, "same target conflict should be advisory");
    Assert(preview.Value.Conflicts.Any(c => c.Kind == "same-target" && !c.BlocksDeployment), "same target conflict should warn");
    Assert(!env.DeploymentService().Deploy(game).Success, "advisory conflict should require acknowledgement");
    Assert(env.DeploymentService().Deploy(game, allowWarnings: true).Success, "acknowledged advisory conflict should deploy");
}

static void IdenticalLegacyEntriesAreCoalesced()
{
    using var env = TestEnv.Create();
    var game = env.CreateGame();
    var exe = Path.Combine(game, "Ragnarock", "Binaries", "Win64");
    Directory.CreateDirectory(Path.Combine(exe, "Mods"));
    File.WriteAllText(Path.Combine(exe, "UE4SS.dll"), "root-dll");
    File.WriteAllText(Path.Combine(exe, "UE4SS-settings.ini"), "[Overrides]\nModsFolderPath =\n");
    env.Database.UpsertGame(new GameRecord("ragnarock", "Ragnarock", game, null, null, "test"));

    var package = env.CreatePackage("legacy-package", manifest =>
    {
        manifest.Files =
        [
            new ManifestFile { Type = "config", Source = "Scripts/main.lua", Target = "Mods/LegacyPackage/Scripts/main.lua" },
            new ManifestFile { Type = "loose-file", Source = "Scripts/main.lua", Target = "Ragnarock/Binaries/Win64/Mods/LegacyPackage/Scripts/main.lua" },
        ];
    }, files => files["Scripts/main.lua"] = "print('legacy')");
    Assert(env.Importer.Import(package).Success, "legacy package import should succeed");
    env.Database.SetProfileMod("default", "legacy-package", true, 0);

    var preview = env.DeploymentService().Preview(game);
    Assert(preview.Success, preview.Error ?? "preview failed");
    Assert(preview.Value!.CanDeploy, "identical same-mod source/target entries should not conflict");
    Assert(preview.Value.Items.Count == 1, "identical deployment entries should be coalesced");
    Assert(preview.Value.Warnings.Any(w => w.Contains("Coalesced 1", StringComparison.Ordinal)), "coalescing should be reported");
    Assert(env.DeploymentService().Deploy(game).Success, "coalesced legacy package should deploy");
    Assert(File.ReadAllText(Path.Combine(exe, "Mods", "LegacyPackage", "Scripts", "main.lua")) == "print('legacy')", "deployed content should match source");
}

static void DeclaredModConflictsBlockDeployment()
{
    using var env = TestEnv.Create();
    var game = env.CreateGame();
    env.Database.UpsertGame(new GameRecord("ragnarock", "Ragnarock", game, null, null, "test"));

    var first = env.CreatePackage("conflict-a", manifest =>
    {
        manifest.Conflicts = ["conflict-b"];
        manifest.Files = [new ManifestFile { Type = "pak", Source = "A_P.pak", LoadOrder = 100 }];
    }, files => files["A_P.pak"] = "a");
    var second = env.CreatePackage("conflict-b", manifest =>
    {
        manifest.Files = [new ManifestFile { Type = "pak", Source = "B_P.pak", LoadOrder = 101 }];
    }, files => files["B_P.pak"] = "b");
    Assert(env.Importer.Import(first).Success, "first import should succeed");
    Assert(env.Importer.Import(second).Success, "second import should succeed");
    env.Database.SetProfileMod("default", "conflict-a", true, 0);
    env.Database.SetProfileMod("default", "conflict-b", true, 0);

    var preview = env.DeploymentService().Preview(game);
    Assert(preview.Success, preview.Error ?? "preview failed");
    Assert(preview.Value!.Conflicts.Any(c => c.Kind == "declared-conflict" && !c.BlocksDeployment), "declared conflict should warn instead of blocking");
    Assert(env.DeploymentService().Deploy(game, allowWarnings: true).Success, "acknowledged declared conflict should deploy");
}

static void ManagerVersionRequirementBlocksUnsupportedMods()
{
    using var env = TestEnv.Create();
    var game = env.CreateGame();
    env.Database.UpsertGame(new GameRecord("ragnarock", "Ragnarock", game, null, null, "test"));

    var package = env.CreatePackage("future-manager", manifest =>
    {
        manifest.Requires = new Dictionary<string, string> { ["manager"] = ">=99.0.0" };
        manifest.Files = [new ManifestFile { Type = "pak", Source = "Future_P.pak", LoadOrder = 100 }];
    }, files => files["Future_P.pak"] = "pak");
    Assert(env.Importer.Import(package).Success, "import should accept syntactically valid future manager requirement");
    env.Database.SetProfileMod("default", "future-manager", true, 0);

    var preview = env.DeploymentService().Preview(game);
    Assert(preview.Success, preview.Error ?? "preview failed");
    Assert(preview.Value!.Conflicts.Any(c => c.Kind == "manager-requirement" && c.BlocksDeployment), "manager requirement should block deployment");
}

static void Ue4ssVersionRequirementBlocksUnsupportedRuntime()
{
    using var env = TestEnv.Create();
    var game = env.CreateGame();
    env.InstallFakeUe4ss(game);
    File.WriteAllText(Path.Combine(game, "Ragnarock", "Binaries", "Win64", "ue4ss", "UE4SS-version.txt"), "2.5.0");
    env.Database.UpsertGame(new GameRecord("ragnarock", "Ragnarock", game, null, null, "test"));

    var package = env.CreatePackage("ue4ss-version", manifest =>
    {
        manifest.Requires = new Dictionary<string, string> { ["ue4ss"] = ">=3.0.0" };
        manifest.Files = [new ManifestFile { Type = "ue4ss-lua", Source = "Scripts/main.lua", ModFolder = "Versioned" }];
    }, files => files["Scripts/main.lua"] = "print('version')");
    Assert(env.Importer.Import(package).Success, "import should accept valid UE4SS requirement");
    env.Database.SetProfileMod("default", "ue4ss-version", true, 0);

    var preview = env.DeploymentService().Preview(game);
    Assert(preview.Success, preview.Error ?? "preview failed");
    Assert(preview.Value!.Conflicts.Any(c => c.Kind == "ue4ss-version" && c.BlocksDeployment), "UE4SS version requirement should block deployment");
}

static void ModDependenciesBlockAndOrder()
{
    using var env = TestEnv.Create();
    var game = env.CreateGame();
    env.InstallFakeUe4ss(game);
    env.Database.UpsertGame(new GameRecord("ragnarock", "Ragnarock", game, null, null, "test"));

    var apiPackage = env.CreatePackage("ragnacustoms-api", manifest =>
    {
        manifest.Version = "0.2.0";
        manifest.Files = [new ManifestFile { Type = "ue4ss-lua", Source = "Scripts/main.lua", ModFolder = "RagnaCustomsApi" }];
    }, files => files["Scripts/main.lua"] = "print('api')");
    var votePackage = env.CreatePackage("ragnacustoms-vote", manifest =>
    {
        manifest.Dependencies = new Dictionary<string, string> { ["ragnacustoms-api"] = ">=0.2.0" };
        manifest.Files = [new ManifestFile { Type = "ue4ss-lua", Source = "Scripts/main.lua", ModFolder = "RagnaCustomsVote" }];
    }, files => files["Scripts/main.lua"] = "print('vote')");

    Assert(env.Importer.Import(apiPackage).Success, "api import should succeed");
    Assert(env.Importer.Import(votePackage).Success, "vote import should succeed");
    env.Database.SetProfileMod("default", "ragnacustoms-vote", true, 0);
    var disabledPreview = env.DeploymentService().Preview(game);
    Assert(disabledPreview.Success, disabledPreview.Error ?? "preview failed");
    Assert(disabledPreview.Value!.Conflicts.Any(c => c.Kind == "disabled-dependency"), "disabled dependency should block deployment");

    env.Database.SetProfileMod("default", "ragnacustoms-api", true, 999);
    var service = env.DeploymentService();
    var deploy = service.Deploy(game);
    Assert(deploy.Success, deploy.Error ?? "dependency-aware deployment should succeed");
    var lines = File.ReadAllLines(Path.Combine(game, "Ragnarock", "Binaries", "Win64", "ue4ss", "Mods", "mods.txt"));
    Assert(lines.SequenceEqual(["RagnaCustomsApi : 1", "RagnaCustomsVote : 1"]), "dependency should load before consumer regardless of priority");
}

static void DependencyCyclesBlockDeployment()
{
    using var env = TestEnv.Create();
    var game = env.CreateGame();
    env.InstallFakeUe4ss(game);
    env.Database.UpsertGame(new GameRecord("ragnarock", "Ragnarock", game, null, null, "test"));

    foreach (var (id, dependency) in new[] { ("cycle-a", "cycle-b"), ("cycle-b", "cycle-a") })
    {
        var package = env.CreatePackage(id, manifest =>
        {
            manifest.Dependencies = new Dictionary<string, string> { [dependency] = ">=1.0.0" };
            manifest.Files = [new ManifestFile { Type = "ue4ss-lua", Source = "Scripts/main.lua", ModFolder = id }];
        }, files => files["Scripts/main.lua"] = id);
        Assert(env.Importer.Import(package).Success, $"{id} import should succeed");
        env.Database.SetProfileMod("default", id, true, 0);
    }

    var preview = env.DeploymentService().Preview(game);
    Assert(preview.Success, preview.Error ?? "preview failed");
    Assert(preview.Value!.Conflicts.Any(c => c.Kind == "dependency-cycle" && c.BlocksDeployment), "dependency cycle should block deployment");
}

static void DeploymentPreservesUnmanagedUe4ssEntries()
{
    using var env = TestEnv.Create();
    var game = env.CreateGame();
    env.InstallFakeUe4ss(game);
    env.Database.UpsertGame(new GameRecord("ragnarock", "Ragnarock", game, null, null, "test"));
    var modsFile = Path.Combine(game, "Ragnarock", "Binaries", "Win64", "ue4ss", "Mods", "mods.txt");
    File.WriteAllLines(modsFile, ["RagnaLoader : 1", "ManualDisabled : 0", "# user-managed comment"]);

    var package = env.CreatePackage("managed-mod", manifest =>
    {
        manifest.Files = [new ManifestFile { Type = "ue4ss-lua", Source = "Scripts/main.lua", ModFolder = "ManagedMod" }];
    }, files => files["Scripts/main.lua"] = "print('managed')");
    Assert(env.Importer.Import(package).Success, "managed package import should succeed");
    env.Database.SetProfileMod("default", "managed-mod", true, 0);

    var service = env.DeploymentService();
    Assert(service.Deploy(game).Success, "first deployment should succeed");
    Assert(service.Deploy(game).Success, "repeated deployment should succeed");
    var lines = File.ReadAllLines(modsFile);
    Assert(lines.SequenceEqual([
        "ManagedMod : 1",
        "RagnaLoader : 1",
        "ManualDisabled : 0",
        "# user-managed comment",
    ]), "unmanaged mods.txt entries should survive repeated deployment without duplication");
}

static void ActiveRootUe4ssLayoutIsPreferred()
{
    using var env = TestEnv.Create();
    var game = env.CreateGame();
    env.InstallFakeUe4ss(game);
    var exe = Path.Combine(game, "Ragnarock", "Binaries", "Win64");
    File.WriteAllText(Path.Combine(exe, "UE4SS.dll"), "active-root-dll");
    File.WriteAllText(Path.Combine(exe, "UE4SS-version.txt"), "3.0.1");
    Directory.CreateDirectory(Path.Combine(exe, "ActiveMods"));
    File.WriteAllText(Path.Combine(exe, "UE4SS-settings.ini"), "[Overrides]\nModsFolderPath = ActiveMods\n");
    env.Database.UpsertGame(new GameRecord("ragnarock", "Ragnarock", game, null, null, "test"));

    var package = env.CreatePackage("root-layout", manifest =>
    {
        manifest.Files = [new ManifestFile { Type = "ue4ss-lua", Source = "Scripts/main.lua", ModFolder = "RootLayout" }];
    }, files => files["Scripts/main.lua"] = "print('root')");
    Assert(env.Importer.Import(package).Success, "root package import should succeed");
    env.Database.SetProfileMod("default", "root-layout", true, 0);

    var status = new Ue4ssService().Detect(game);
    Assert(status.Layout == "legacy-exe-folder", "root layout should be selected when both layouts exist");
    Assert(status.ModsPath == Path.Combine(exe, "ActiveMods"), "configured root ModsFolderPath should be used");
    Assert(env.DeploymentService().Deploy(game).Success, "root layout deploy should succeed");
    Assert(File.Exists(Path.Combine(exe, "ActiveMods", "RootLayout", "scripts", "main.lua")), "mod should deploy to active root Mods path");
}

static void Ue4ssInstallMapsLayout()
{
    using var env = TestEnv.Create();
    var game = env.CreateGame();
    var zip = Path.Combine(env.Root, "ue4ss.zip");
    using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
    {
        TestHelpers.AddEntry(archive, "UE4SS.dll", "dll");
        TestHelpers.AddEntry(archive, "UE4SS-settings.ini", "settings");
        TestHelpers.AddEntry(archive, "xinput1_3.dll", "proxy");
    }

    var service = new Ue4ssService();
    var result = service.InstallFromZip(game, zip);
    Assert(result.Success, result.Error ?? "ue4ss install failed");
    var status = service.Detect(game);
    Assert(status.Installed, "ue4ss status should be installed");
    var exeFolder = Path.Combine(game, "Ragnarock", "Binaries", "Win64");
    var expectedDll = OperatingSystem.IsLinux()
        ? Path.Combine(exeFolder, "UE4SS.dll")
        : Path.Combine(exeFolder, "ue4ss", "UE4SS.dll");
    Assert(File.Exists(expectedDll), "UE4SS.dll should be in the platform-compatible layout");
}

static void Ue4ssReleaseServiceCachesInstallsAndRollsBackVersions()
{
    using var env = TestEnv.Create();
    var game = env.CreateGame();
    var releasesJson = """
        [
          {
            "tag_name": "v3.0.1",
            "name": "v3.0.1",
            "draft": false,
            "prerelease": false,
            "published_at": "2024-02-14T19:59:38Z",
            "assets": [
              { "name": "zDEV-UE4SS_v3.0.1.zip", "browser_download_url": "https://example.invalid/dev.zip" },
              { "name": "UE4SS_v3.0.1.zip", "browser_download_url": "https://example.invalid/ue4ss-3.0.1.zip" }
            ]
          },
          {
            "tag_name": "v2.5.2",
            "name": "v2.5.2",
            "draft": false,
            "prerelease": false,
            "published_at": "2023-09-01T00:00:00Z",
            "assets": [
              { "name": "UE4SS_v2.5.2.zip", "browser_download_url": "https://example.invalid/ue4ss-2.5.2.zip" }
            ]
          }
        ]
        """;
    var client = new HttpClient(new FakeHttpHandler(request =>
    {
        var uri = request.RequestUri?.ToString() ?? "";
        if (uri.EndsWith("/releases", StringComparison.Ordinal))
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(releasesJson) };
        }

        if (uri.EndsWith(".zip", StringComparison.Ordinal))
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(TestHelpers.CreateUe4ssZipBytes()) };
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }));
    var service = new Ue4ssReleaseService(env.Paths, client);

    var initialCheck = service.CheckForUpdates(game).GetAwaiter().GetResult();
    Assert(initialCheck.Success, initialCheck.Error ?? "initial update check failed");
    Assert(initialCheck.Value!.UpdateAvailable, "missing UE4SS should make latest available");
    Assert(initialCheck.Value.Latest!.Version == "3.0.1", "latest release should be selected by version");

    var available = service.FetchAvailableReleases().GetAwaiter().GetResult();
    Assert(available.Success, available.Error ?? "fetch releases failed");
    var latest = available.Value![0];
    var older = available.Value![1];

    var latestDownload = service.DownloadRelease(latest).GetAwaiter().GetResult();
    Assert(latestDownload.Success, latestDownload.Error ?? "latest download failed");
    Assert(File.Exists(latestDownload.Value!.CachedArchivePath), "latest archive should be cached");
    Assert(service.InstallCachedRelease(game, latestDownload.Value).Success, "latest cached release should install");
    Assert(new Ue4ssService().Detect(game).Version == "3.0.1", "latest installed version should be recorded");

    var currentCheck = service.CheckForUpdates(game).GetAwaiter().GetResult();
    Assert(currentCheck.Success, currentCheck.Error ?? "current update check failed");
    Assert(!currentCheck.Value!.UpdateAvailable, "latest installed version should be up to date");

    var olderDownload = service.DownloadRelease(older).GetAwaiter().GetResult();
    Assert(olderDownload.Success, olderDownload.Error ?? "older download failed");
    Assert(service.InstallCachedRelease(game, olderDownload.Value!).Success, "older cached release should reinstall");
    Assert(new Ue4ssService().Detect(game).Version == "2.5.2", "older cached version should be installable");
    Assert(service.GetCachedReleases().Count == 2, "both downloaded versions should remain cached");

    var rollbackCheck = service.CheckForUpdates(game).GetAwaiter().GetResult();
    Assert(rollbackCheck.Success, rollbackCheck.Error ?? "rollback update check failed");
    Assert(rollbackCheck.Value!.UpdateAvailable, "older installed version should report latest as available");
}

static void SteamLibraryVdfParserFindsLibraryPaths()
{
    var paths = SteamLibraryDiscoverer.ParseLibraryFoldersVdf("""
        "libraryfolders"
        {
            "0"
            {
                "path" "/home/user/.steam/steam"
            }
            "1"
            {
                "path" "/mnt/games/SteamLibrary"
            }
        }
        """);
    Assert(paths.Count == 2, "expected two parsed library paths");
    Assert(paths[1] == "/mnt/games/SteamLibrary", "second library path should parse");
}

static void LaunchPlanUsesOptionalArguments()
{
    var plan = new ProtonLaunch().BuildPlan("/games/Ragnarock.exe", preferSteamProtocol: false, "--custom");
    Assert(plan.GameArguments == "--custom", "direct launch should include the custom arguments without adding defaults");
    Assert(plan.DisplayCommand.EndsWith("/games/Ragnarock.exe --custom", StringComparison.Ordinal), "display command should show direct arguments");
    var expectedSteamOptions = OperatingSystem.IsWindows()
        ? "--custom"
        : $"{ProtonLaunch.RequiredSteamLaunchOptions} --custom";
    Assert(plan.SteamLaunchOptions == expectedSteamOptions, "Steam launch options should include the platform-specific defaults and custom arguments");
}

static void CompatibilityCheckerReportsUsableInstall()
{
    using var env = TestEnv.Create();
    var game = env.CreateGame();
    var report = new RagnarockCompatibilityChecker().Check(game);
    Assert(report.CanManage, "test install should be manageable");
    Assert(report.Warnings.Any(w => w.Contains("UE4SS", StringComparison.OrdinalIgnoreCase)), "missing UE4SS should be a warning");
}

static void FolderOpenerBuildsPlatformCommand()
{
    var command = new FolderOpener().BuildCommand(Path.GetTempPath());
    Assert(!string.IsNullOrWhiteSpace(command.FileName), "folder opener should provide executable");
    Assert(command.DisplayCommand.Contains(Path.GetFullPath(Path.GetTempPath()), StringComparison.Ordinal), "display command should include target path");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

internal sealed class TestEnv : IDisposable
{
    public string Root { get; }
    public AppPaths Paths { get; }
    public ManagerDatabase Database { get; }
    public PackageImporter Importer { get; }
    public AppLogger Logger { get; }

    private TestEnv(string root)
    {
        Root = root;
        Paths = AppPaths.Create(Path.Combine(root, "data"));
        Logger = new AppLogger(Paths.Logs);
        Database = new ManagerDatabase(Paths);
        Database.Initialize();
        Importer = new PackageImporter(Paths, Database, Logger);
    }

    public static TestEnv Create() => new(Path.Combine(Path.GetTempPath(), "rmm-tests-" + Guid.NewGuid().ToString("N")));

    public string CreateGame()
    {
        var game = Path.Combine(Root, "RagnarockGame");
        Directory.CreateDirectory(Path.Combine(game, "Ragnarock", "Binaries", "Win64"));
        Directory.CreateDirectory(Path.Combine(game, "Ragnarock", "Content", "Paks"));
        File.WriteAllText(Path.Combine(game, "Ragnarock", "Binaries", "Win64", "Ragnarock-Win64-Shipping.exe"), "");
        var install = new RagnarockDetector().Validate(game);
        if (!install.IsValid)
        {
            throw new InvalidOperationException(string.Join("; ", install.Diagnostics));
        }

        return game;
    }

    public void InstallFakeUe4ss(string game)
    {
        var ue4ss = Path.Combine(game, "Ragnarock", "Binaries", "Win64", "ue4ss");
        Directory.CreateDirectory(Path.Combine(ue4ss, "Mods"));
        File.WriteAllText(Path.Combine(ue4ss, "UE4SS.dll"), "dll");
        File.WriteAllText(Path.Combine(ue4ss, "UE4SS-version.txt"), "3.0.0");
    }

    public string CreatePackage(string id, Action<ModManifest> configure, Action<Dictionary<string, string>> configureFiles)
    {
        var manifest = TestHelpers.ValidManifest(id);
        configure(manifest);
        var files = new Dictionary<string, string> { ["manifest.json"] = JsonSerializer.Serialize(manifest, TestHelpers.JsonOptions) };
        configureFiles(files);

        var package = Path.Combine(Root, id + ".rmod");
        using var archive = ZipFile.Open(package, ZipArchiveMode.Create);
        foreach (var file in files)
        {
            TestHelpers.AddEntry(archive, file.Key, file.Value);
        }

        return package;
    }

    public DeploymentService DeploymentService()
    {
        var rules = new RagnarockDeploymentRules();
        var planner = new DeploymentPlanner(Database, rules);
        return new DeploymentService(Paths, Database, planner, rules, Logger);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}

internal static class TestHelpers
{
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static ModManifest ValidManifest(string id) => new()
    {
        SchemaVersion = 1,
        Id = id,
        Name = id,
        Version = "1.0.0",
        Author = "Tester",
        Game = "ragnarock",
        Files = [new ManifestFile { Type = "ue4ss-lua", Source = "Scripts/main.lua", ModFolder = "TestMod" }]
    };

    public static void AddEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    public static byte[] CreateUe4ssZipBytes()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, "UE4SS.dll", "dll");
            AddEntry(archive, "UE4SS-settings.ini", "settings");
            AddEntry(archive, "xinput1_3.dll", "proxy");
        }

        return stream.ToArray();
    }
}

internal sealed class FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(respond(request));
    }
}
