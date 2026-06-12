namespace RagnaModManager.Ragnarock.Compatibility;

public sealed record CompatibilityReport(
    bool CanManage,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Info);
