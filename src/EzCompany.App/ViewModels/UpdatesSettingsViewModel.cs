using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Updater;
using Microsoft.Extensions.Options;

namespace EzCompany.App.ViewModels;

public sealed partial class UpdatesSettingsViewModel : ObservableObject
{
    private readonly VelopackUpdateService _updater;
    private readonly SafeMigrationService _migration;
    private readonly IPermissionService _permissions;
    private readonly ISessionContext _session;
    private readonly UpdaterOptions _options;

    private UpdateCheckResult? _lastCheck;

    public UpdatesSettingsViewModel(
        VelopackUpdateService updater,
        SafeMigrationService migration,
        IPermissionService permissions,
        ISessionContext session,
        IOptions<UpdaterOptions> options)
    {
        _updater = updater;
        _migration = migration;
        _permissions = permissions;
        _session = session;
        _options = options.Value;
    }

    public ObservableCollection<string> PreMigrationBackups { get; } = [];

    [ObservableProperty] private string _versionText = string.Empty;
    [ObservableProperty] private string _policyGateText = string.Empty;
    [ObservableProperty] private string _repoUrlText = string.Empty;
    [ObservableProperty] private string _updateStatus = "Nhấn 'Kiểm tra cập nhật' để đối chiếu GitHub Releases với MaxApprovedVersion.";
    [ObservableProperty] private bool _canInstallUpdate;

    public void Load()
    {
        PolicySnapshot? pol = _permissions.CurrentPolicy;
        VersionText = $"Phiên bản hiện tại: {_session.CurrentAppVersion}";
        PolicyGateText = _session.IsDirectorNode
            ? $"Vai trò Giám đốc: Được phép kiểm thử bản mới trước (Kênh: {pol?.AllowedUpdateChannel ?? _options.DefaultChannel})"
            : $"Kênh: {pol?.AllowedUpdateChannel ?? _options.DefaultChannel} • Phiên bản phê duyệt tối đa (MaxApprovedVersion): {pol?.MaxApprovedVersion ?? _session.CurrentAppVersion}";
        RepoUrlText = $"Nguồn phát hành công khai: {_options.GitHubRepositoryUrl}";

        PreMigrationBackups.Clear();
        foreach (string dir in _migration.ListPreMigrationBackups())
        {
            PreMigrationBackups.Add(Path.GetFileName(dir));
        }
    }

    [RelayCommand]
    private async Task CheckForUpdateAsync()
    {
        _lastCheck = await _updater.CheckForUpdateAsync();
        UpdateStatus = _lastCheck.Message;
        CanInstallUpdate = _lastCheck.IsUpdateAvailable && _lastCheck.IsAllowedByPolicy;
    }

    [RelayCommand]
    private async Task InstallUpdateAsync()
    {
        if (_lastCheck is null)
        {
            return;
        }

        UpdateStatus = await _updater.DownloadAndApplyUpdateAsync(_lastCheck);
    }
}
