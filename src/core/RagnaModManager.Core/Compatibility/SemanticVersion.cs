namespace RagnaModManager.Core.Compatibility;

/// <summary>Small SemVer 2.0 comparator used for registry and compatibility checks.</summary>
public static class SemanticVersion
{
    public static int Compare(string? left, string? right)
    {
        var a = Parse(left);
        var b = Parse(right);
        for (var i = 0; i < 3; i++)
        {
            var comparison = a.Core[i].CompareTo(b.Core[i]);
            if (comparison != 0) return comparison;
        }

        if (a.PreRelease.Count == 0 && b.PreRelease.Count == 0) return 0;
        if (a.PreRelease.Count == 0) return 1;
        if (b.PreRelease.Count == 0) return -1;
        for (var i = 0; i < Math.Max(a.PreRelease.Count, b.PreRelease.Count); i++)
        {
            if (i >= a.PreRelease.Count) return -1;
            if (i >= b.PreRelease.Count) return 1;
            var ai = a.PreRelease[i];
            var bi = b.PreRelease[i];
            if (int.TryParse(ai, out var an) && int.TryParse(bi, out var bn))
            {
                var comparison = an.CompareTo(bn);
                if (comparison != 0) return comparison;
            }
            else if (int.TryParse(ai, out _)) return -1;
            else if (int.TryParse(bi, out _)) return 1;
            else
            {
                var comparison = string.CompareOrdinal(ai, bi);
                if (comparison != 0) return comparison;
            }
        }
        return 0;
    }

    public static bool IsNewer(string candidate, string installed) => Compare(candidate, installed) > 0;

    private static (int[] Core, List<string> PreRelease) Parse(string? value)
    {
        var normalized = (value ?? "").Trim().TrimStart('v', 'V');
        var buildless = normalized.Split('+', 2)[0];
        var parts = buildless.Split('-', 2);
        var core = parts[0].Split('.');
        var numbers = new int[3];
        for (var i = 0; i < 3 && i < core.Length; i++) int.TryParse(core[i], out numbers[i]);
        var pre = parts.Length == 2
            ? parts[1].Split('.', StringSplitOptions.RemoveEmptyEntries).ToList()
            : [];
        return (numbers, pre);
    }
}
