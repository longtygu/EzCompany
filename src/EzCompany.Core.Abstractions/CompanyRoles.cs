namespace EzCompany.Core.Abstractions;

/// <summary>
/// Standard workstation and user roles recognized by Ez Company.
/// </summary>
public static class CompanyRoles
{
    public const string Director = "Director";
    public const string Accounting = "Accounting";
    public const string Legal = "Legal";
    public const string Staff = "Staff";

    public static readonly IReadOnlyList<string> All =
    [
        Director,
        Accounting,
        Legal,
        Staff
    ];

    public static IReadOnlyList<string> GetDefaultModulesForRole(string role) => role switch
    {
        Director => ModuleIds.All,
        Accounting => [ModuleIds.AccountingInvoiceXml, ModuleIds.OfficePdfStudio],
        Legal => [ModuleIds.LegalContractDiff, ModuleIds.OfficePdfStudio],
        _ => [ModuleIds.OfficePdfStudio]
    };

    public static IReadOnlyList<string> GetDefaultPermissionsForRole(string role) => role switch
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
            "office.pdf.use"
        ],
        Accounting => ["accounting.invoice.read", "accounting.invoice.write", "office.pdf.use"],
        Legal => ["legal.contract.read", "legal.contract.write", "office.pdf.use"],
        _ => ["office.pdf.use"]
    };
}
