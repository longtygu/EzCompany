using EzCompany.Core.Abstractions;
using Microsoft.Extensions.Options;
using Velopack;
using Velopack.Exceptions;
using Velopack.Sources;

namespace EzCompany.Core.Updater;

public sealed record UpdateCheckResult(
    bool IsUpdateAvailable,
    bool IsAllowedByPolicy,
    string CurrentVersion,
    string? CandidateVersion,
    string Message,
    UpdateInfo? VelopackUpdateInfo = null);

/// <summary>
/// Wraps Velopack public GitHub Releases (<see cref="GithubSource"/> without embedded PAT)
/// gated by <see cref="UpdatePolicyGate"/>.
/// </summary>
public sealed class VelopackUpdateService
{
    private readonly UpdaterOptions _options;
    private readonly UpdatePolicyGate _gate;
    private readonly IPermissionService _permissions;
    private readonly ISessionContext _session;
    private readonly IAuditLogger _audit;

    public VelopackUpdateService(
        IOptions<UpdaterOptions> options,
        UpdatePolicyGate gate,
        IPermissionService permissions,
        ISessionContext session,
        IAuditLogger audit)
    {
        _options = options.Value;
        _gate = gate;
        _permissions = permissions;
        _session = session;
        _audit = audit;
    }

    public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken ct = default)
    {
        string current = _session.CurrentAppVersion;
        if (string.IsNullOrWhiteSpace(_options.GitHubRepositoryUrl) ||
            _options.GitHubRepositoryUrl.Contains("<OWNER>", StringComparison.OrdinalIgnoreCase))
        {
            return new UpdateCheckResult(
                false,
                false,
                current,
                null,
                "Chưa cấu hình GitHubRepositoryUrl thực tế trong appsettings.json (hiện đang dùng placeholder mã nguồn mở).");
        }

        PolicySnapshot? policy = _permissions.CurrentPolicy;
        string channel = policy?.AllowedUpdateChannel ?? _options.DefaultChannel;
        bool allowPrerelease = string.Equals(channel, "beta", StringComparison.OrdinalIgnoreCase);

        try
        {
            var source = new GithubSource(_options.GitHubRepositoryUrl, accessToken: null, prerelease: allowPrerelease);
            var mgr = new UpdateManager(source);
            if (!mgr.IsInstalled)
            {
                return new UpdateCheckResult(false, false, current, null, "Đang chạy chế độ phát triển (chưa cài đặt qua gói Velopack).");
            }

            UpdateInfo? updateInfo = await mgr.CheckForUpdatesAsync().ConfigureAwait(false);
            if (updateInfo is null)
            {
                return new UpdateCheckResult(false, false, current, null, $"Ứng dụng đã ở phiên bản mới nhất ({current}).");
            }

            string candidateVer = updateInfo.TargetFullRelease.Version.ToString();
            UpdateApprovalDecision decision = _gate.Evaluate(
                current,
                candidateVer,
                _session.IsDirectorNode,
                policy?.MaxApprovedVersion ?? current,
                channel);

            return new UpdateCheckResult(
                true,
                decision.IsAllowed,
                current,
                candidateVer,
                decision.Reason,
                decision.IsAllowed ? updateInfo : null);
        }
        catch (NotInstalledException)
        {
            return new UpdateCheckResult(false, false, current, null, "Đang chạy trực tiếp từ mã nguồn / chưa đóng gói Velopack.");
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(false, false, current, null, $"Không thể kiểm tra cập nhật: {ex.Message}");
        }
    }

    public async Task<string> DownloadAndApplyUpdateAsync(UpdateCheckResult check, CancellationToken ct = default)
    {
        if (!check.IsUpdateAvailable || !check.IsAllowedByPolicy || check.VelopackUpdateInfo is null)
        {
            return check.Message;
        }

        PolicySnapshot? policy = _permissions.CurrentPolicy;
        string channel = policy?.AllowedUpdateChannel ?? _options.DefaultChannel;
        bool allowPrerelease = string.Equals(channel, "beta", StringComparison.OrdinalIgnoreCase);

        var source = new GithubSource(_options.GitHubRepositoryUrl, accessToken: null, prerelease: allowPrerelease);
        var mgr = new UpdateManager(source);

        await mgr.DownloadUpdatesAsync(check.VelopackUpdateInfo, cancelToken: ct).ConfigureAwait(false);
        await _audit.LogAsync(
            "VELOPACK_UPDATE_DOWNLOADED",
            "core.updater",
            check.CandidateVersion,
            check.Message,
            "Info",
            ct).ConfigureAwait(false);

        mgr.ApplyUpdatesAndRestart(check.VelopackUpdateInfo);
        return "Đang khởi động lại để hoàn tất cập nhật...";
    }
}
