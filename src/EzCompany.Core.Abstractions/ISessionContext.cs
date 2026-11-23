namespace EzCompany.Core.Abstractions;

/// <summary>
/// Holds runtime identity and active workstation state for the running process.
/// </summary>
public interface ISessionContext
{
    bool IsAuthenticated { get; }
    bool IsDirectorNode { get; }
    Guid CompanyId { get; }
    string CompanyName { get; }
    string TaxCode { get; }
    Guid UserId { get; }
    string Username { get; }
    string FullName { get; }
    string Role { get; }
    string MachineId { get; }
    string CurrentAppVersion { get; }
    int CurrentLanPort { get; set; }

    void SetCompanyContext(Guid companyId, string companyName, string taxCode, bool isDirectorNode);
    void SetAuthenticatedUser(Guid userId, string username, string fullName, string role);
    void ClearAuthentication();
}
