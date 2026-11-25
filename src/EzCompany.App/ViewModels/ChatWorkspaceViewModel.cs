using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using EzCompany.Core.LanAgent.Collaboration;
using EzCompany.Core.LanAgent.Models;
using EzCompany.Core.LanAgent.Tasks;
using Microsoft.Win32;

namespace EzCompany.App.ViewModels;

public sealed partial class SelectablePeerMember : ObservableObject
{
    public required string MachineId { get; init; }
    public required string FullName { get; init; }
    public required string RoleDisplay { get; init; }
    [ObservableProperty] private bool _isSelected;
}

public sealed class ChatMessageDisplayItem
{
    public required Guid Id { get; init; }
    public required string SenderFullName { get; init; }
    public required string Content { get; init; }
    public required string SentTimeLocal { get; init; }
    public string? AttachmentFileName { get; init; }
    public string? AttachmentVaultPath { get; init; }
    public bool HasAttachment => !string.IsNullOrWhiteSpace(AttachmentFileName);
    public bool NeedsReverseDownload => HasAttachment && string.IsNullOrWhiteSpace(AttachmentVaultPath);
}

public sealed partial class ChatWorkspaceViewModel : ObservableObject
{
    private readonly ChatService _chatService;
    private readonly WorkTaskService _taskService;
    private readonly LanTaskClient _taskClient;
    private readonly LanCollaborationClient _collabClient;
    private readonly PeerCollabSyncService _collabSync;
    private readonly ILocalStoragePaths _paths;
    private readonly IEventBus _eventBus;

    public ChatWorkspaceViewModel(
        ChatService chatService,
        WorkTaskService taskService,
        LanTaskClient taskClient,
        LanCollaborationClient collabClient,
        PeerCollabSyncService collabSync,
        ILocalStoragePaths paths,
        IEventBus eventBus)
    {
        _chatService = chatService;
        _taskService = taskService;
        _taskClient = taskClient;
        _collabClient = collabClient;
        _collabSync = collabSync;
        _paths = paths;
        _eventBus = eventBus;

        _eventBus.Subscribe<ChatMessageReceivedEvent>(_ => RefreshRequested?.Invoke(this, EventArgs.Empty));
    }

    public ObservableCollection<ChatConversationEntity> Conversations { get; } = [];
    public ObservableCollection<ChatMessageDisplayItem> Messages { get; } = [];
    public ObservableCollection<PeerDirectoryEntryDto> Peers { get; } = [];
    public ObservableCollection<SelectablePeerMember> GroupMemberChoices { get; } = [];
    public ObservableCollection<WorkTaskEntity> PinnableTasks { get; } = [];

    [ObservableProperty] private ChatConversationEntity? _selectedConversation;
    [ObservableProperty] private PeerDirectoryEntryDto? _selectedDirectPeer;
    [ObservableProperty] private string _newGroupName = string.Empty;
    [ObservableProperty] private WorkTaskEntity? _selectedTaskToPin;
    [ObservableProperty] private string _messageDraft = string.Empty;
    [ObservableProperty] private string _draftAttachmentPath = string.Empty;
    [ObservableProperty] private string _statusBanner = string.Empty;

    public event EventHandler? RefreshRequested;

    [RelayCommand]
    public async Task RefreshAllAsync()
    {
        IReadOnlyList<ChatConversationEntity> convs = await _chatService.GetConversationsAsync();
        Guid? prevConvId = SelectedConversation?.Id;
        Conversations.Clear();
        foreach (ChatConversationEntity c in convs)
        {
            Conversations.Add(c);
        }

        SelectedConversation = Conversations.FirstOrDefault(c => c.Id == prevConvId)
                               ?? Conversations.FirstOrDefault();

        IReadOnlyList<PeerDirectoryEntryDto> peers = await _taskClient.GetCompanyPeerDirectoryAsync();
        Peers.Clear();
        GroupMemberChoices.Clear();
        foreach (PeerDirectoryEntryDto p in peers.Where(x => !string.Equals(x.MachineId, _paths.MachineId, StringComparison.OrdinalIgnoreCase)))
        {
            Peers.Add(p);
            GroupMemberChoices.Add(new SelectablePeerMember
            {
                MachineId = p.MachineId,
                FullName = p.FullName,
                RoleDisplay = CompanyRoles.FormatRolesBilingual(p.Role)
            });
        }
        SelectedDirectPeer ??= Peers.FirstOrDefault();

        IReadOnlyList<WorkTaskEntity> inbox = await _taskService.GetAssignedToMeTasksAsync(includeCompleted: true);
        IReadOnlyList<WorkTaskEntity> outbox = await _taskService.GetAssignedByMeTasksAsync();
        PinnableTasks.Clear();
        foreach (WorkTaskEntity t in inbox.Concat(outbox).DistinctBy(x => x.Id).OrderByDescending(x => x.UpdatedAtUtc))
        {
            PinnableTasks.Add(t);
        }
        SelectedTaskToPin ??= PinnableTasks.FirstOrDefault();

        await LoadSelectedConversationMessagesAsync();
    }

    partial void OnSelectedConversationChanged(ChatConversationEntity? value)
    {
        _ = LoadSelectedConversationMessagesAsync();
    }

    public async Task LoadSelectedConversationMessagesAsync()
    {
        Messages.Clear();
        if (SelectedConversation is null)
        {
            return;
        }

        IReadOnlyList<ChatMessageEntity> raw = await _chatService.GetMessagesAsync(SelectedConversation.Id);
        foreach (ChatMessageEntity m in raw)
        {
            Messages.Add(new ChatMessageDisplayItem
            {
                Id = m.Id,
                SenderFullName = m.SenderFullName,
                Content = m.Content,
                SentTimeLocal = m.SentAtUtc.ToLocalTime().ToString("dd/MM HH:mm"),
                AttachmentFileName = m.AttachmentFileName,
                AttachmentVaultPath = m.AttachmentVaultPath
            });
        }
    }

    [RelayCommand]
    private async Task StartDirectChatAsync()
    {
        if (SelectedDirectPeer is null)
        {
            StatusBanner = "Chọn đồng nghiệp để bắt đầu chat riêng.";
            return;
        }

        ChatConversationEntity conv = await _chatService.GetOrCreateDirectConversationAsync(
            SelectedDirectPeer.MachineId,
            SelectedDirectPeer.FullName);
        await RefreshAllAsync();
        SelectedConversation = Conversations.FirstOrDefault(c => c.Id == conv.Id);
    }

    [RelayCommand]
    private async Task CreateGroupAsync()
    {
        if (string.IsNullOrWhiteSpace(NewGroupName))
        {
            StatusBanner = "Nhập tên nhóm chat trước khi tạo.";
            return;
        }

        List<(string MachineId, string FullName)> selected = GroupMemberChoices
            .Where(m => m.IsSelected)
            .Select(m => (m.MachineId, m.FullName))
            .ToList();

        ChatConversationEntity conv = await _chatService.CreateGroupConversationAsync(NewGroupName, selected);
        NewGroupName = string.Empty;
        await RefreshAllAsync();
        SelectedConversation = Conversations.FirstOrDefault(c => c.Id == conv.Id);
        StatusBanner = $"Đã tạo nhóm chat '{conv.Name}'.";
    }

    [RelayCommand]
    private void BrowseAttachment()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Chọn tệp gửi trong Chat (tối đa 10 MB)",
            Filter = "Tất cả tệp (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            DraftAttachmentPath = dialog.FileName;
        }
    }

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        if (SelectedConversation is null)
        {
            StatusBanner = "Chưa chọn cuộc trò chuyện.";
            return;
        }

        if (string.IsNullOrWhiteSpace(MessageDraft) && string.IsNullOrWhiteSpace(DraftAttachmentPath))
        {
            return;
        }

        string[] targets = ResolveTargetMachineIds(SelectedConversation);
        await _chatService.SendMessageAsync(
            SelectedConversation.Id,
            MessageDraft,
            string.IsNullOrWhiteSpace(DraftAttachmentPath) ? null : DraftAttachmentPath,
            null,
            targets);

        MessageDraft = string.Empty;
        DraftAttachmentPath = string.Empty;

        int delivered = await _collabSync.FlushPendingChatAsync();
        StatusBanner = delivered > 0
            ? "Đã gửi và đồng bộ tin nhắn qua LAN."
            : "Đã lưu tin nhắn cục bộ (sẽ tự đồng bộ khi đồng nghiệp Online).";

        await LoadSelectedConversationMessagesAsync();
    }

    [RelayCommand]
    private async Task PinSelectedTaskAsync()
    {
        if (SelectedConversation is null || SelectedTaskToPin is null)
        {
            StatusBanner = "Chọn cuộc trò chuyện và công việc cần ghim.";
            return;
        }

        string[] targets = ResolveTargetMachineIds(SelectedConversation);
        await _chatService.PinTaskToConversationAsync(SelectedConversation.Id, SelectedTaskToPin.Id, targets);
        _ = await _collabSync.FlushPendingChatAsync();
        await RefreshAllAsync();
        StatusBanner = $"Đã ghim công việc '{SelectedTaskToPin.Title}' vào đoạn chat.";
    }

    [RelayCommand]
    private async Task OpenOrRetrieveAttachmentAsync(ChatMessageDisplayItem? item)
    {
        if (item is null || !item.HasAttachment)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(item.AttachmentVaultPath))
        {
            bool downloaded = await _collabClient.ReverseDownloadChatAttachmentAsync(item.Id);
            if (!downloaded)
            {
                StatusBanner = "Không tìm thấy tệp trên máy đồng nghiệp đang Online.";
                return;
            }

            await LoadSelectedConversationMessagesAsync();
            item = Messages.FirstOrDefault(m => m.Id == item.Id) ?? item;
        }

        if (string.IsNullOrWhiteSpace(item.AttachmentVaultPath))
        {
            return;
        }

        string fullPath = Path.GetFullPath(Path.Combine(
            _paths.VaultDirectory,
            item.AttachmentVaultPath.Replace('/', Path.DirectorySeparatorChar)));
        if (File.Exists(fullPath))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{fullPath}\"")
            {
                UseShellExecute = true
            });
        }
    }

    [RelayCommand]
    private async Task ReversePullHistoryFromLanAsync()
    {
        int totalPulled = 0;
        foreach (PeerDirectoryEntryDto peer in Peers.Where(p => p.IsOnline && !string.IsNullOrWhiteSpace(p.Endpoint)))
        {
            try
            {
                totalPulled += await _collabClient.PullChatHistoryFromPeerAsync(peer.Endpoint!);
            }
            catch
            {
            }
        }

        await RefreshAllAsync();
        StatusBanner = totalPulled > 0
            ? $"Đã truy xuất ngược {totalPulled} tin nhắn/tệp lịch sử từ các máy LAN."
            : "Dữ liệu chat đã đầy đủ hoặc không có tin nhắn mới từ các máy Online.";
    }

    private string[] ResolveTargetMachineIds(ChatConversationEntity conv)
    {
        try
        {
            string[]? members = JsonSerializer.Deserialize<string[]>(conv.MemberMachineIdsJson);
            if (members is { Length: > 0 })
            {
                return members;
            }
        }
        catch
        {
        }

        return ["ALL"];
    }
}
