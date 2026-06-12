namespace RagnaModManager.Core.Profiles;

public sealed record ProfileDocument(string Id, string Name, IReadOnlyList<ProfileModDocument> Mods);

public sealed record ProfileModDocument(string Id, bool Enabled, int Priority);
