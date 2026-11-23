namespace EzCompany.Core.Abstractions;

/// <summary>
/// Stable module identifiers used in signed policy licenses (<c>policy.lic</c>) and dynamic module loading.
/// </summary>
public static class ModuleIds
{
    public const string AccountingInvoiceXml = "accounting.invoice_xml";
    public const string LegalContractDiff = "legal.contract_diff";
    public const string OfficePdfStudio = "office.pdf_studio";

    public static readonly IReadOnlyList<string> All =
    [
        AccountingInvoiceXml,
        LegalContractDiff,
        OfficePdfStudio
    ];
}
