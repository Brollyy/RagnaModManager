using RagnaModManager.Core.Manifests;

namespace RagnaModManager.Core.Packages;

public sealed record PackageInspection(
    string ArchivePath,
    ModManifest Manifest,
    IReadOnlyList<PackageEntryInfo> Entries,
    long TotalUncompressedBytes);

public sealed record PackageEntryInfo(string Path, long CompressedBytes, long UncompressedBytes);
