using System.Reflection;

namespace EzCompany.Core.Abstractions;

public sealed class SessionContext : ISessionContext
{
    public SessionContext(ILocalStoragePaths storagePaths)
    {
        MachineId = storagePaths.MachineId;
        Version? asmVer = Assembly.GetEntryAssembly()?.GetName().Version;
        CurrentAppVersion = asmVer is null ? "0.1.0" : $"{asmVer.Major}.{asmVer.Minor}.{Math.Max(0, asmVer.Build)}";
    }

    public bool IsAuthenticated { get; private set; }
    public bool IsDirectorNode { get; private set; }
    public Guid CompanyId { get; private set; }
    public string CompanyName { get; private set; } = string.Empty;
    public string TaxCode { get; private set; } = string.Empty;
    public Guid UserId { get; private set; }
    public string Username { get; private set; } = string.Empty;
    public string FullName { get; private set; } = string.Empty;
    public string Role { get; private set; } = string.Empty;
    public string MachineId { get; }
    public string CurrentAppVersion { get; }
    public int CurrentLanPort { get; set; }

    public void SetCompanyContext(Guid companyId, string companyName, string taxCode, bool isDirectorNode)
    {
        CompanyId = companyId;
        CompanyName = companyName;
        TaxCode = taxCode;
        IsDirectorNode = isDirectorNode;
    }

    public void SetAuthenticatedUser(Guid userId, string username, string fullName, string role)
    {
        UserId = userId;
        Username = username;
        FullName = fullName;
        Role = role;
        IsAuthenticated = true;
    }

    public void ClearAuthentication()
    {
        UserId = Guid.Empty;
        Username = string.Empty;
        FullName = string.Empty;
        Role = string.Empty;
        IsAuthenticated = false;
    }
}
