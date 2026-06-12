using RagnaModManager.Core.Common;

namespace RagnaModManager.Core.Compatibility;

public static class VersionRequirement
{
    public static Result ValidateSyntax(string requirement)
    {
        return TryParse(requirement, out _, out _) ? Result.Ok() : Result.Fail($"Unsupported version requirement: {requirement}");
    }

    public static bool IsSatisfied(string requirement, string currentVersion)
    {
        if (!TryParse(requirement, out var op, out var required))
        {
            return false;
        }

        var current = ParseVersion(currentVersion);
        var comparison = current.CompareTo(required);
        return op switch
        {
            ">=" => comparison >= 0,
            ">" => comparison > 0,
            "<=" => comparison <= 0,
            "<" => comparison < 0,
            "=" or "==" => comparison == 0,
            _ => false
        };
    }

    private static bool TryParse(string input, out string op, out Version required)
    {
        op = "";
        required = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var trimmed = input.Trim();
        foreach (var candidate in new[] { ">=", "<=", "==", ">", "<", "=" })
        {
            if (!trimmed.StartsWith(candidate, StringComparison.Ordinal))
            {
                continue;
            }

            op = candidate;
            return Version.TryParse(NormalizeVersion(trimmed[candidate.Length..].Trim()), out required!);
        }

        op = "=";
        return Version.TryParse(NormalizeVersion(trimmed), out required!);
    }

    private static Version ParseVersion(string version)
    {
        return Version.TryParse(NormalizeVersion(version), out var parsed) ? parsed : new Version(0, 0, 0);
    }

    private static string NormalizeVersion(string version)
    {
        var core = version.Split('-', '+')[0];
        var parts = core.Split('.', StringSplitOptions.RemoveEmptyEntries).ToList();
        while (parts.Count < 3)
        {
            parts.Add("0");
        }

        return string.Join('.', parts.Take(3));
    }
}
