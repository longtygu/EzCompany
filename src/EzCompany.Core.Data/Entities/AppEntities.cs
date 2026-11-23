namespace EzCompany.Core.Data.Entities;

public sealed class CompanyProfileEntity
{
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string TaxCode { get; set; } = string.Empty;
    public string RootPublicKeyBase64 { get; set; } = string.Empty;
    public string CompanyEncryptionPublicKeyBase64 { get; set; } = string.Empty;
    public bool IsDirectorNode { get; set; }
    public bool IsGenesisCompleted { get; set; }
    public int AppSchemaVersion { get; set; } = 1;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class LocalUserEntity
{
    public Guid UserId { get; set; }
    public Guid CompanyId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? LastLoginUtc { get; set; }
}

public sealed class PairedDeviceEntity
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string MachineId { get; set; } = string.Empty;
    public string DevicePublicKeyBase64 { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public long CurrentPolicyVersion { get; set; }
    public string CurrentPolicyJson { get; set; } = string.Empty;
    public string? PendingPolicyJson { get; set; }
    public string? LastKnownIp { get; set; }
    public int LastKnownPort { get; set; }
    public bool IsOnline { get; set; }
    public string AppVersion { get; set; } = "0.1.0";
    public int SchemaVersion { get; set; } = 1;
    public long DbSizeBytes { get; set; }
    public long VaultSizeBytes { get; set; }
    public double FreeDiskPercent { get; set; }
    public DateTimeOffset? LastSeenUtc { get; set; }
    public DateTimeOffset? LastBackupUtc { get; set; }
    public DateTimeOffset? LastTelemetrySyncUtc { get; set; }
    public DateTimeOffset PairedAtUtc { get; set; }
    public string PairedByAdminName { get; set; } = string.Empty;
}

public sealed class AuditLogEntity
{
    public Guid Id { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public string MachineId { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string ModuleId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? TargetEntity { get; set; }
    public string? DetailsJson { get; set; }
    public string Severity { get; set; } = "Info";
}

public sealed class AccountingInvoiceEntity
{
    public Guid Id { get; set; }
    public string InvoiceSeries { get; set; } = string.Empty;
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTimeOffset IssueDate { get; set; }
    public string SellerTaxCode { get; set; } = string.Empty;
    public string SellerName { get; set; } = string.Empty;
    public string BuyerTaxCodeOrCccd { get; set; } = string.Empty;
    public string BuyerName { get; set; } = string.Empty;
    public decimal SubTotal { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string AmountInWords { get; set; } = string.Empty;
    public bool HasMathMismatch { get; set; }
    public decimal MathDifference { get; set; }
    public bool IsDuplicate { get; set; }
    public string ValidationNotes { get; set; } = string.Empty;
    public string VaultRelativePath { get; set; } = string.Empty;
    public string FileSha256 { get; set; } = string.Empty;
    public string ImportedByUsername { get; set; } = string.Empty;
    public DateTimeOffset ImportedAtUtc { get; set; }
}

public sealed class LegalContractReviewEntity
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string RevisedFileName { get; set; } = string.Empty;
    public string OriginalVaultPath { get; set; } = string.Empty;
    public string RevisedVaultPath { get; set; } = string.Empty;
    public string OriginalSha256 { get; set; } = string.Empty;
    public string RevisedSha256 { get; set; } = string.Empty;
    public int AddedCount { get; set; }
    public int DeletedCount { get; set; }
    public int ModifiedCount { get; set; }
    public string ReviewerNotes { get; set; } = string.Empty;
    public string ReportVaultPath { get; set; } = string.Empty;
    public string ReviewedByUsername { get; set; } = string.Empty;
    public DateTimeOffset ReviewedAtUtc { get; set; }
}

public sealed class OfficeDocumentRecordEntity
{
    public Guid Id { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string SourceFilesJson { get; set; } = "[]";
    public string OutputVaultPath { get; set; } = string.Empty;
    public string OutputSha256 { get; set; } = string.Empty;
    public int PageCount { get; set; }
    public string Notes { get; set; } = string.Empty;
    public string CreatedByUsername { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}
