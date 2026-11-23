namespace EzCompany.Core.LanAgent.Discovery;

public interface ILanDiscoveryService
{
    IReadOnlyCollection<DiscoveredPeer> GetPeers();
    IReadOnlyCollection<DiscoveredPeer> GetPairingOpenDirectors();
    void RefreshAdvertisement(bool? pairingOpenOverride = null);
    event EventHandler? PeersChanged;
}
