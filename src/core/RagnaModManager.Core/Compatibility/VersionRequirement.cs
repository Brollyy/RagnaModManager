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

        var comparison = SemanticVersion.Compare(currentVersion, required);
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

    private static bool TryParse(string input, out string op, out string required)
    {
        op = "";
        required = "";
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
            required = trimmed[candidate.Length..].Trim();
            return IsVersion(required);
        }

        op = "=";
        required = trimmed;
        return IsVersion(required);
    }

    private static bool IsVersion(string version)
    {
        var core = version.TrimStart('v', 'V').Split('-', '+')[0];
        var parts = core.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length is >= 1 and <= 3 && parts.All(p => int.TryParse(p, out var n) && n >= 0);
    }
}
