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
    public string? CompanyLogoBase64 { get; set; }
    public DateTimeOffset? CompanyLogoUpdatedAtUtc { get; set; }
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
    public string? AvatarBase64 { get; set; }
    public DateTimeOffset? AvatarUpdatedAtUtc { get; set; }
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
    public string? AvatarBase64 { get; set; }
    public DateTimeOffset? AvatarUpdatedAtUtc { get; set; }
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

public sealed class WorkTaskEntity
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string TaskKind { get; set; } = "Self"; // Self | Assigned
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Priority { get; set; } = "Normal"; // Low | Normal | High | Urgent
    public string Status { get; set; } = "Pending"; // Pending | InProgress | HandedOver | Completed | RevisionRequested

    public string AssignerMachineId { get; set; } = string.Empty;
    public string AssignerUsername { get; set; } = string.Empty;
    public string AssignerFullName { get; set; } = string.Empty;
    public string AssignerRole { get; set; } = string.Empty;

    public string AssigneeMachineId { get; set; } = string.Empty;
    public string AssigneeUsername { get; set; } = string.Empty;
    public string AssigneeFullName { get; set; } = string.Empty;
    public string AssigneeRole { get; set; } = string.Empty;

    public DateTimeOffset? DueDateUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }

    public string? AssignmentFileName { get; set; }
    public string? AssignmentVaultPath { get; set; }
    public string? AssignmentFileSha256 { get; set; }

    public string HandoverNotes { get; set; } = string.Empty;
    public string? HandoverFileName { get; set; }
    public string? HandoverVaultPath { get; set; }
    public string? HandoverFileSha256 { get; set; }
    public DateTimeOffset? HandedOverAtUtc { get; set; }
    public string ReviewNotes { get; set; } = string.Empty;

    public string? PendingSyncTargetMachineId { get; set; }
    public string? PendingSyncAction { get; set; }
    public string SubtasksJson { get; set; } = "[]";
    public string RecurrenceRule { get; set; } = "None"; // None | Daily | Weekly | Monthly
}

public sealed class WorkTaskHistoryEntity
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string ActorMachineId { get; set; } = string.Empty;
    public string ActorUsername { get; set; } = string.Empty;
    public string ActorFullName { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string? AttachmentFileName { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
}

public sealed class PasswordResetRequestEntity
{
    public Guid RequestId { get; set; }
    public Guid CompanyId { get; set; }
    public string MachineId { get; set; } = string.Empty;
    public string DevicePublicKeyBase64 { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? RequestedNewUsername { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string State { get; set; } = "Pending"; // Pending | Approved | Rejected | Expired
    public DateTimeOffset RequestedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? DecidedAtUtc { get; set; }
    public string? DecidedByAdminName { get; set; }
    public string? DecisionMessage { get; set; }
    public string? SignedResetTicketJson { get; set; }
    public string? RemoteIpAddress { get; set; }
}

public sealed class ChatConversationEntity
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string ConversationType { get; set; } = "Group"; // Group | Direct
    public string Name { get; set; } = string.Empty;
    public string MemberMachineIdsJson { get; set; } = "[]";
    public string MemberNamesJson { get; set; } = "[]";
    public Guid? PinnedTaskId { get; set; }
    public string? PinnedTaskSummary { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class ChatMessageEntity
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Guid CompanyId { get; set; }
    public string SenderMachineId { get; set; } = string.Empty;
    public string SenderUsername { get; set; } = string.Empty;
    public string SenderFullName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string MessageType { get; set; } = "Text"; // Text | Sticker | File | PinnedTask
    public string? AttachmentFileName { get; set; }
    public string? AttachmentVaultPath { get; set; }
    public string? AttachmentSha256 { get; set; }
    public long AttachmentSizeBytes { get; set; }
    public Guid? LinkedTaskId { get; set; }
    public DateTimeOffset SentAtUtc { get; set; }
    public string? PendingSyncTargetsJson { get; set; }
    public Guid? ReplyToMessageId { get; set; }
    public string? ReplyPreviewText { get; set; }
}

public sealed class TimelinePlanEntity
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "Plan"; // Plan | Milestone | Meeting | Deadline
    public DateTimeOffset StartDateUtc { get; set; }
    public DateTimeOffset EndDateUtc { get; set; }
    public string Status { get; set; } = "Planned"; // Planned | InProgress | Done
    public string CreatedByMachineId { get; set; } = string.Empty;
    public string CreatedByUsername { get; set; } = string.Empty;
    public string CreatedByFullName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
}

public sealed class EmployeeProfileEntity
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string PositionTitle { get; set; } = string.Empty;
    public string RolesCsv { get; set; } = "Staff";
    public string? LinkedMachineId { get; set; }
    public string? LinkedUsername { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string NationalId { get; set; } = string.Empty;
    public DateTimeOffset JoinDateUtc { get; set; }
    public string EmploymentStatus { get; set; } = "Active"; // Active | OnLeave | Inactive
    public decimal BaseSalaryVnd { get; set; } = 0m;
    public decimal AllowanceVnd { get; set; } = 0m;
    public double StandardWorkDaysPerMonth { get; set; } = 26.0;
    public decimal InsuranceDeductionVnd { get; set; } = 0m;
    public string Notes { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class AttendanceRecordEntity
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid EmployeeProfileId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string MachineId { get; set; } = string.Empty;
    public string DateKey { get; set; } = string.Empty; // yyyy-MM-dd
    public DateTimeOffset? CheckInUtc { get; set; }
    public DateTimeOffset? CheckOutUtc { get; set; }
    public double WorkedHours { get; set; }
    public double OvertimeHours { get; set; } = 0.0;
    public double WorkDayUnits { get; set; } = 1.0;
    public bool IsManualEntry { get; set; } = false;
    public string Status { get; set; } = "Present"; // Present | Late | HalfDay | Leave | Absent
    public string Notes { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class LeaveRequestEntity
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid EmployeeProfileId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string MachineId { get; set; } = string.Empty;
    public string RequestType { get; set; } = "Leave"; // Leave | HalfDay | Late | Overtime
    public string StartDateKey { get; set; } = string.Empty; // yyyy-MM-dd
    public string EndDateKey { get; set; } = string.Empty; // yyyy-MM-dd
    public double OvertimeHours { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending"; // Pending | Approved | Rejected
    public string ReviewedByName { get; set; } = string.Empty;
    public DateTimeOffset? ReviewedAtUtc { get; set; }
    public string ReviewNotes { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class CashbookEntryEntity
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string EntryDateKey { get; set; } = string.Empty; // yyyy-MM-dd
    public string EntryType { get; set; } = "Expense"; // Income | Expense
    public string Category { get; set; } = string.Empty;
    public decimal AmountVnd { get; set; }
    public string Description { get; set; } = string.Empty;
    public string ReferenceCode { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = "Cash"; // Cash | BankTransfer
    public Guid? LinkedExpenseRequestId { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class ExpenseRequestEntity
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string RequesterMachineId { get; set; } = string.Empty;
    public string RequesterUsername { get; set; } = string.Empty;
    public string RequesterFullName { get; set; } = string.Empty;
    public string RequestType { get; set; } = "Advance"; // Advance | Reimbursement | Payment
    public string Category { get; set; } = string.Empty;
    public decimal AmountVnd { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? ReceiptFileName { get; set; }
    public string? ReceiptVaultPath { get; set; }
    public string Status { get; set; } = "Pending"; // Pending | Approved | Disbursed | Rejected
    public string ApprovedByName { get; set; } = string.Empty;
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public string ReviewNotes { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class ContractRecordEntity
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string CounterpartyName { get; set; } = string.Empty;
    public string ContractType { get; set; } = "Customer"; // Customer | Vendor | Labor | NDA | Lease | Other
    public string SignedDateKey { get; set; } = string.Empty;
    public string StartDateKey { get; set; } = string.Empty;
    public string ExpiryDateKey { get; set; } = string.Empty;
    public decimal ValueVnd { get; set; }
    public string Status { get; set; } = "Active"; // Active | ExpiringSoon | Expired | Terminated
    public string OwnerName { get; set; } = string.Empty;
    public string? VaultFilePath { get; set; }
    public string? FileName { get; set; }
    public string Notes { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class SharedDocumentEntity
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = "General"; // Policy | Template | Notice | General
    public string Description { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string? VaultRelativePath { get; set; }
    public string FileSha256 { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string UploadedByMachineId { get; set; } = string.Empty;
    public string UploadedByName { get; set; } = string.Empty;
    public DateTimeOffset UploadedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
