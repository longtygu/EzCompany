using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EzCompany.App.Services;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using EzCompany.Core.LanAgent.Discovery;
using EzCompany.Core.LanAgent.Models;
using EzCompany.Core.LanAgent.Pairing;
using EzCompany.Core.LanAgent.Recovery;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace EzCompany.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly GenesisBootstrapService _bootstrap;
    private readonly ILocalStoragePaths _paths;
    private readonly ILanDiscoveryService _discovery;
    private readonly LanPairingClient _pairingClient;
    private readonly LanAccountRecoveryClient _recoveryClient;
    private readonly ThemeService _theme;
    private readonly ILogger<MainWindowViewModel> _logger;

    public MainWindowViewModel(
        GenesisBootstrapService bootstrap,
        ILocalStoragePaths paths,
        ILanDiscoveryService discovery,
        LanPairingClient pairingClient,
        LanAccountRecoveryClient recoveryClient,
        ThemeService theme,
        ILogger<MainWindowViewModel> logger)
    {
        _bootstrap = bootstrap;
        _paths = paths;
        _discovery = discovery;
        _pairingClient = pairingClient;
        _recoveryClient = recoveryClient;
        _theme = theme;
        _logger = logger;

        ProfileBadge = $"Profile: {paths.ProfileName}  •  MachineId: {paths.MachineId}";
        _discovery.PeersChanged += (_, _) => LanPeersRefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    public ObservableCollection<DiscoveredPeer> DiscoveredDirectors { get; } = [];

    [ObservableProperty] private DiscoveredPeer? _selectedDirectorPeer;
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private bool _isInitialized;
    [ObservableProperty] private int _selectedBootstrapTab;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _isError;
    [ObservableProperty] private string _profileBadge = string.Empty;

    // Initialized Login state
    [ObservableProperty] private bool _isDirectorNode;
    [ObservableProperty] private bool _isRecoveryMode;
    [ObservableProperty] private string _companyDisplayName = string.Empty;
    [ObservableProperty] private string _companyTaxCode = string.Empty;
    [ObservableProperty] private string _nodeRoleBadge = string.Empty;
    [ObservableProperty] private string _loginUsername = string.Empty;

    // Initialized Employee Reset via LAN
    [ObservableProperty] private string _employeeResetManualEndpoint = string.Empty;
    [ObservableProperty] private string _employeeResetNewUsername = string.Empty;
    [ObservableProperty] private string _employeeResetReason = "Quên mật khẩu đăng nhập";
    [ObservableProperty] private string _employeeResetStatusText = string.Empty;

    // Initialized Director Account Recovery via .recovery
    [ObservableProperty] private string _directorRecoverFilePath = string.Empty;
    [ObservableProperty] private string _directorRecoverNewUsername = string.Empty;

    // Branch 1: Genesis Director
    [ObservableProperty] private string _genesisCompanyName = string.Empty;
    [ObservableProperty] private string _genesisTaxCode = string.Empty;
    [ObservableProperty] private string _genesisFullName = string.Empty;
    [ObservableProperty] private string _genesisUsername = string.Empty;
    [ObservableProperty] private string _genesisRecoveryOutputPath = string.Empty;

    // Branch 2: Secondary Director / Recovery
    [ObservableProperty] private string _restoreRecoveryFilePath = string.Empty;
    [ObservableProperty] private string _restoreFullName = string.Empty;
    [ObservableProperty] private string _restoreUsername = string.Empty;

    // Branch 3: Join LAN
    [ObservableProperty] private string _lanManualEndpoint = string.Empty;
    [ObservableProperty] private string _lanFullName = string.Empty;
    [ObservableProperty] private string _lanUsername = string.Empty;
    [ObservableProperty] private string _lanSasCode = string.Empty;
    [ObservableProperty] private string _lanPairingStatus = "Đang quét mạng LAN tìm máy Giám đốc mở ghép đôi...";

    public event EventHandler? AuthenticationCompleted;
    public event EventHandler? LanPeersRefreshRequested;

    public async Task LoadStateAsync()
    {
        IsLoading = true;
        try
        {
            CompanyProfileEntity? company = await _bootstrap.GetCompanyProfileAsync();
            if (company is { IsGenesisCompleted: true })
            {
                IsInitialized = true;
                IsDirectorNode = company.IsDirectorNode;
                CompanyDisplayName = company.CompanyName;
                CompanyTaxCode = $"MST: {company.TaxCode}";
                NodeRoleBadge = company.IsDirectorNode ? "Nút Giám đốc (Director)" : "Nút Nhân viên (Workstation)";

                LocalUserEntity? primaryUser = await _bootstrap.GetPrimaryLocalUserAsync();
                if (primaryUser is not null)
                {
                    if (string.IsNullOrWhiteSpace(LoginUsername))
                    {
                        LoginUsername = primaryUser.Username;
                    }

                    if (string.IsNullOrWhiteSpace(EmployeeResetNewUsername))
                    {
                        EmployeeResetNewUsername = primaryUser.Username;
                    }

                    if (string.IsNullOrWhiteSpace(DirectorRecoverNewUsername))
                    {
                        DirectorRecoverNewUsername = primaryUser.Username;
                    }
                }

                RefreshDiscoveredDirectors();
            }
            else
            {
                IsInitialized = false;
                RefreshDiscoveredDirectors();
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void RefreshDiscoveredDirectors()
    {
        string? selectedId = SelectedDirectorPeer?.MachineId;
        DiscoveredDirectors.Clear();

        foreach (DiscoveredPeer peer in _discovery.GetPeers().Where(p => p.IsDirector))
        {
            DiscoveredDirectors.Add(peer);
        }

        SelectedDirectorPeer = DiscoveredDirectors.FirstOrDefault(p => p.MachineId == selectedId)
                               ?? DiscoveredDirectors.FirstOrDefault(p => p.PairingOpen)
                               ?? DiscoveredDirectors.FirstOrDefault();

        LanPairingStatus = DiscoveredDirectors.Count == 0
            ? "Chưa phát hiện máy Giám đốc trên LAN (có thể nhập trực tiếp IP:Port bên dưới)."
            : $"Tìm thấy {DiscoveredDirectors.Count} máy Giám đốc trên LAN.";
    }

    [RelayCommand]
    private void ToggleTheme() => _theme.ToggleTheme();

    [RelayCommand]
    private void OpenRecoveryMode()
    {
        ClearStatus();
        RefreshDiscoveredDirectors();
        IsRecoveryMode = true;
    }

    [RelayCommand]
    private void CancelRecoveryMode()
    {
        ClearStatus();
        IsRecoveryMode = false;
    }

    [RelayCommand]
    private void BrowseDirectorRecoverFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Chọn tệp Master Recovery Kit (.recovery)",
            Filter = "Ez Company Recovery Kit (*.recovery)|*.recovery"
        };

        if (dialog.ShowDialog() == true)
        {
            DirectorRecoverFilePath = dialog.FileName;
        }
    }

    [RelayCommand]
    private void BrowseGenesisRecoveryOutput()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Lưu tệp Master Recovery Kit",
            Filter = "Ez Company Recovery Kit (*.recovery)|*.recovery",
            FileName = $"ezcompany-master-{DateTime.Now:yyyyMMdd}.recovery"
        };

        if (dialog.ShowDialog() == true)
        {
            GenesisRecoveryOutputPath = dialog.FileName;
        }
    }

    [RelayCommand]
    private void BrowseRestoreRecoveryFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Chọn tệp Master Recovery Kit (.recovery)",
            Filter = "Ez Company Recovery Kit (*.recovery)|*.recovery"
        };

        if (dialog.ShowDialog() == true)
        {
            RestoreRecoveryFilePath = dialog.FileName;
        }
    }

    public async Task ExecuteGenesisAsync(string loginPassword, string recoveryPassword)
    {
        ClearStatus();
        if (string.IsNullOrWhiteSpace(GenesisCompanyName) ||
            string.IsNullOrWhiteSpace(GenesisTaxCode) ||
            string.IsNullOrWhiteSpace(GenesisFullName) ||
            string.IsNullOrWhiteSpace(GenesisUsername) ||
            string.IsNullOrEmpty(loginPassword) ||
            string.IsNullOrEmpty(recoveryPassword) ||
            string.IsNullOrWhiteSpace(GenesisRecoveryOutputPath))
        {
            SetError("Vui lòng nhập đầy đủ thông tin công ty, mật khẩu đăng nhập, mật khẩu khôi phục (tối thiểu 8 ký tự) và chọn nơi lưu tệp .recovery.");
            return;
        }

        IsBusy = true;
        try
        {
            await _bootstrap.InitializeGenesisDirectorAsync(
                GenesisCompanyName,
                GenesisTaxCode,
                GenesisFullName,
                GenesisUsername,
                loginPassword,
                recoveryPassword,
                GenesisRecoveryOutputPath);

            _logger.LogInformation("Genesis Director initialized for profile {Profile} (User={Username}).", _paths.ProfileName, GenesisUsername);
            AuthenticationCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Genesis Director initialization failed.");
            SetError($"Không thể khởi tạo: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ExecuteRestoreDirectorAsync(string recoveryPassword, string loginPassword)
    {
        ClearStatus();
        if (string.IsNullOrWhiteSpace(RestoreRecoveryFilePath) ||
            string.IsNullOrEmpty(recoveryPassword) ||
            string.IsNullOrWhiteSpace(RestoreFullName) ||
            string.IsNullOrWhiteSpace(RestoreUsername) ||
            string.IsNullOrEmpty(loginPassword))
        {
            SetError("Vui lòng chọn tệp .recovery và nhập đầy đủ thông tin tài khoản Giám đốc 2.");
            return;
        }

        IsBusy = true;
        try
        {
            await _bootstrap.RestoreSecondaryDirectorAsync(
                RestoreRecoveryFilePath,
                recoveryPassword,
                RestoreFullName,
                RestoreUsername,
                loginPassword);

            _logger.LogInformation("Secondary Director restored for profile {Profile} (User={Username}).", _paths.ProfileName, RestoreUsername);
            AuthenticationCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Secondary Director restore failed.");
            SetError($"Khôi phục thất bại: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ExecuteJoinLanAsync(string localPassword)
    {
        ClearStatus();
        string? endpoint = !string.IsNullOrWhiteSpace(LanManualEndpoint)
            ? LanManualEndpoint.Trim()
            : SelectedDirectorPeer?.Endpoint;

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            SetError("Vui lòng chọn máy Giám đốc trên LAN hoặc nhập địa chỉ IP:Port.");
            return;
        }

        if (string.IsNullOrWhiteSpace(LanFullName) ||
            string.IsNullOrWhiteSpace(LanUsername) ||
            string.IsNullOrEmpty(localPassword))
        {
            SetError("Vui lòng nhập Họ tên, Tên đăng nhập và Mật khẩu cục bộ.");
            return;
        }

        IsBusy = true;
        LanSasCode = string.Empty;
        try
        {
            StartedPairingSession started = await _pairingClient.InitiatePairingAsync(
                endpoint,
                LanFullName,
                LanUsername);

            _logger.LogInformation("Initiated LAN pairing to {Endpoint} with SAS {SasCode}.", endpoint, started.SasCode);
            LanSasCode = $"SAS: {started.SasCode}";
            LanPairingStatus = $"Đang chờ Giám đốc ({started.DirectorMetadata.CompanyName}) đối chiếu mã SAS {started.SasCode} và bấm Duyệt...";

            PairingStatusResponse finalStatus = await _pairingClient.WaitAndCompleteOnboardingAsync(
                endpoint,
                started,
                LanFullName,
                LanUsername,
                localPassword);

            if (finalStatus.State == PairingRequestState.Approved)
            {
                _logger.LogInformation("LAN pairing approved for {Username}@{MachineId}.", LanUsername, _paths.MachineId);
                AuthenticationCompleted?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                _logger.LogWarning("LAN pairing ended with state {State}: {Message}", finalStatus.State, finalStatus.Message);
                SetError(finalStatus.Message ?? $"Ghép đôi không thành công ({finalStatus.State}).");
                LanPairingStatus = "Yêu cầu ghép đôi không được duyệt hoặc đã hết hạn.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LAN pairing failed against {Endpoint}.", endpoint);
            SetError($"Lỗi ghép đôi LAN: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ExecuteOfflineLoginAsync(string password)
    {
        ClearStatus();
        if (string.IsNullOrWhiteSpace(LoginUsername) || string.IsNullOrEmpty(password))
        {
            SetError("Vui lòng nhập tên đăng nhập và mật khẩu.");
            return;
        }

        IsBusy = true;
        try
        {
            AuthenticationResult result = await _bootstrap.AuthenticateOfflineAsync(LoginUsername, password);
            if (!result.Succeeded)
            {
                _logger.LogWarning("Offline login failed for {Username}: {Reason}", LoginUsername, result.Message);
                SetError(result.Message);
                return;
            }

            _logger.LogInformation("Offline login succeeded for {Username} (PolicyLocked={Locked}).", LoginUsername, result.IsPolicyLocked);
            AuthenticationCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during offline login.");
            SetError($"Lỗi đăng nhập: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ExecuteEmployeePasswordResetAsync(string newPassword, string confirmPassword)
    {
        ClearStatus();
        string? endpoint = !string.IsNullOrWhiteSpace(EmployeeResetManualEndpoint)
            ? EmployeeResetManualEndpoint.Trim()
            : SelectedDirectorPeer?.Endpoint;

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            SetError("Vui lòng chọn máy Giám đốc đang online trên LAN hoặc nhập IP:Port.");
            return;
        }

        if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 6)
        {
            SetError("Mật khẩu mới phải có ít nhất 6 ký tự.");
            return;
        }

        if (newPassword != confirmPassword)
        {
            SetError("Mật khẩu mới và mật khẩu xác nhận không khớp.");
            return;
        }

        IsBusy = true;
        try
        {
            AccountResetInitiateResponseDto initiated = await _recoveryClient.SubmitResetRequestAsync(
                endpoint,
                EmployeeResetNewUsername,
                EmployeeResetReason);

            EmployeeResetStatusText = $"{initiated.Message} (Mã yêu cầu: {initiated.RequestId.ToString("N")[..8]})";
            SetInfo("Đã gửi yêu cầu lên máy Giám đốc. Đang chờ Giám đốc duyệt...");

            (AccountResetStatusResponseDto finalStatus, LocalUserEntity? updated) = await _recoveryClient
                .WaitAndApplyResetAsync(endpoint, initiated.RequestId, newPassword);

            if (updated is not null)
            {
                LoginUsername = updated.Username;
                IsRecoveryMode = false;
                EmployeeResetStatusText = string.Empty;
                SetInfo($"Cấp lại tài khoản thành công (Tên đăng nhập: {updated.Username}). Hãy đăng nhập bằng mật khẩu mới.");
            }
            else
            {
                SetError(finalStatus.DecisionMessage ?? $"Yêu cầu cấp lại không thành công ({finalStatus.State}).");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Employee LAN password reset failed.");
            SetError($"Lỗi yêu cầu cấp lại tài khoản: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ExecuteDirectorAccountRecoveryAsync(string recoveryPassword, string newPassword, string confirmPassword)
    {
        ClearStatus();
        if (string.IsNullOrWhiteSpace(DirectorRecoverFilePath))
        {
            SetError("Vui lòng chọn tệp .recovery của công ty.");
            return;
        }

        if (string.IsNullOrEmpty(recoveryPassword))
        {
            SetError("Vui lòng nhập mật khẩu giải mã tệp .recovery.");
            return;
        }

        if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 6)
        {
            SetError("Mật khẩu đăng nhập mới phải có ít nhất 6 ký tự.");
            return;
        }

        if (newPassword != confirmPassword)
        {
            SetError("Mật khẩu mới và mật khẩu xác nhận không khớp.");
            return;
        }

        IsBusy = true;
        try
        {
            LocalUserEntity recoveredUser = await _bootstrap.RecoverDirectorAccountWithRecoveryKitAsync(
                DirectorRecoverFilePath,
                recoveryPassword,
                DirectorRecoverNewUsername,
                newPassword);

            LoginUsername = recoveredUser.Username;
            IsRecoveryMode = false;
            SetInfo($"Đã khôi phục tài khoản Giám đốc ({recoveredUser.Username}) thành công. Hãy đăng nhập bằng mật khẩu mới.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Director recovery kit password reset failed.");
            SetError($"Khôi phục tài khoản thất bại: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void ClearStatus()
    {
        StatusMessage = null;
        IsError = false;
    }

    public void SetError(string message)
    {
        StatusMessage = message;
        IsError = true;
    }

    public void SetInfo(string message)
    {
        StatusMessage = message;
        IsError = false;
    }
}
