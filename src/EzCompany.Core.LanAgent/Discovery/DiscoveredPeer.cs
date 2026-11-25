namespace EzCompany.Core.LanAgent.Discovery;

public sealed record DiscoveredPeer(
    string CompanyId,
    string CompanyName,
    string NodeRole,
    string MachineId,
    string PubKeyHash,
    string IpAddress,
    int Port,
    bool PairingOpen,
    string AppVersion,
    DateTimeOffset LastSeenUtc,
    string UserFullName = "",
    string Username = "",
    string UserRole = "")
{
    public string Endpoint => $"{IpAddress}:{Port}";
    public bool IsDirector => string.Equals(NodeRole, "director", StringComparison.OrdinalIgnoreCase);
}
