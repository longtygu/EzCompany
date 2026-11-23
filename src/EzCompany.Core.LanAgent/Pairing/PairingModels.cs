namespace EzCompany.Core.LanAgent.Pairing;

public sealed record PairingInitiateRequest(
    string MachineId,
    string DevicePublicKeyBase64,
    string FullName,
    string Username,
    string ClientNonceBase64,
    string AppVersion,
    int ListenPort = 0);

public sealed record PairingInitiateResponse(
    Guid RequestId,
    Guid CompanyId,
    string CompanyName,
    string TaxCode,
    string RootPublicKeyBase64,
    string CompanyEncryptionPublicKeyBase64,
    string ServerNonceBase64,
    DateTimeOffset ExpiresAtUtc);

public enum PairingRequestState
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Expired = 3
}

public sealed record PairingStatusResponse(
    Guid RequestId,
    PairingRequestState State,
    string? Message,
    string? SignedPolicyJson,
    Guid? UserId);

public sealed class PendingPairingRequest
{
    public required Guid RequestId { get; init; }
    public required string MachineId { get; init; }
    public required string DevicePublicKeyBase64 { get; init; }
    public required string FullName { get; init; }
    public required string Username { get; init; }
    public required string ClientNonceBase64 { get; init; }
    public required string ServerNonceBase64 { get; init; }
    public required string SasCode { get; init; }
    public required string AppVersion { get; init; }
    public required DateTimeOffset RequestedAtUtc { get; init; }
    public required DateTimeOffset ExpiresAtUtc { get; init; }
    public string? RemoteIpAddress { get; init; }
    public int RemoteListenPort { get; init; }

    public PairingRequestState State { get; set; } = PairingRequestState.Pending;
    public string? DecisionMessage { get; set; }
    public string? SignedPolicyJson { get; set; }
    public Guid? AssignedUserId { get; set; }
}
