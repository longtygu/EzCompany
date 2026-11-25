namespace EzCompany.Core.Abstractions;

/// <summary>
/// Standard workstation and user roles recognized by Ez Company.
/// Supports single roles (e.g. <c>Director</c>) as well as comma-separated multi-role assignments
/// (e.g. <c>Accounting,Legal</c>) with bilingual Vietnamese/English display formatting.
/// </summary>
public static class CompanyRoles
{
    public const string Director = "Director";
    public const string Accounting = "Accounting";
    public const string Legal = "Legal";
    public const string Hr = "HR";
    public const string Staff = "Staff";

    public static readonly IReadOnlyList<string> All =
    [
        Director,
        Accounting,
        Legal,
        Hr,
        Staff
    ];

    public static string NormalizeSingleRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return Staff;
        }

        string trimmed = role.Trim();
        if (trimmed.Equals("Director", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Manager", StringComparison.OrdinalIgnoreCase))
        {
            return Director;
        }

        if (trimmed.Equals("Accounting", StringComparison.OrdinalIgnoreCase))
        {
            return Accounting;
        }

        if (trimmed.Equals("Legal", StringComparison.OrdinalIgnoreCase))
        {
            return Legal;
        }

        if (trimmed.Equals("HR", StringComparison.OrdinalIgnoreCase))
        {
            return Hr;
        }

        return Staff;
    }

    public static IReadOnlyList<string> ParseRoles(string? rawRole)
    {
        if (string.IsNullOrWhiteSpace(rawRole))
        {
            return [Staff];
        }

        var result = new List<string>();
        foreach (string token in rawRole.Split([',', '/', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string canonical = NormalizeSingleRole(token);
            if (!result.Contains(canonical, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(canonical);
            }
        }

        return result.Count > 0 ? result : [Staff];
    }

    public static string FormatRoleStorage(IEnumerable<string> roles)
    {
        var normalized = roles
            .Select(NormalizeSingleRole)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return normalized.Length == 0 ? Staff : string.Join(",", normalized);
    }

    public static bool HasRole(string? rawRole, string targetRole) =>
        ParseRoles(rawRole).Any(r => string.Equals(r, targetRole, StringComparison.OrdinalIgnoreCase));

    public static string GetBilingualLabel(string? singleRole) => NormalizeSingleRole(singleRole) switch
    {
        Director => "Manager (Giám đốc)",
        Accounting => "Accounting (Kế toán)",
        Legal => "Legal (Pháp chế)",
        Hr => "HR (Nhân sự)",
        _ => "Staff (Nhân viên)"
    };

    public static string FormatRolesBilingual(string? rawRole) =>
        string.Join(" / ", ParseRoles(rawRole).Select(GetBilingualLabel));

    public static IReadOnlyList<string> GetDefaultModulesForRole(string role)
    {
        var modules = new List<string>();
        foreach (string single in ParseRoles(role))
        {
            IEnumerable<string> set = single switch
            {
                Director => ModuleIds.All,
                Accounting => [ModuleIds.AccountingInvoiceXml, ModuleIds.OfficePdfStudio],
                Legal => [ModuleIds.LegalContractDiff, ModuleIds.OfficePdfStudio],
                _ => [ModuleIds.OfficePdfStudio]
            };

            foreach (string mod in set)
            {
                if (!modules.Contains(mod, StringComparer.OrdinalIgnoreCase))
                {
                    modules.Add(mod);
                }
            }
        }

        return modules;
    }

    public static IReadOnlyList<string> GetDefaultPermissionsForRole(string role)
    {
        var perms = new List<string>();
        foreach (string single in ParseRoles(role))
        {
            IEnumerable<string> set = single switch
            {
                Director =>
                [
                    "director.dashboard",
                    "director.pairing",
                    "director.policy.issue",
                    "director.vault.read",
                    "director.db.snapshot",
                    "director.telemetry.read",
                    "accounting.invoice.read",
                    "accounting.invoice.write",
                    "legal.contract.read",
                    "legal.contract.write",
                    "hr.manage",
                    "office.pdf.use"
                ],
                Accounting => ["accounting.invoice.read", "accounting.invoice.write", "office.pdf.use"],
                Legal => ["legal.contract.read", "legal.contract.write", "office.pdf.use"],
                Hr => ["hr.manage", "office.pdf.use"],
                _ => ["office.pdf.use"]
            };

            foreach (string perm in set)
            {
                if (!perms.Contains(perm, StringComparer.OrdinalIgnoreCase))
                {
                    perms.Add(perm);
                }
            }
        }

        return perms;
    }
}
