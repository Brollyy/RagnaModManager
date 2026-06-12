namespace RagnaModManager.Ragnarock.Detection;

public sealed record RagnarockInstall(string Root, string? ExecutablePath, bool IsValid, IReadOnlyList<string> Diagnostics);
