using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EzCompany.App.Services;
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
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOffline))]
    private bool _isOnline;
    public bool IsOffline => !IsOnline;
}

public sealed partial class ChatPeerPresenceItem : ObservableObject
{
    public required string MachineId { get; init; }
    public required string Username { get; init; }
    public required string FullName { get; init; }
    public required string ShortName { get; init; }
    public required string RoleRaw { get; init; }
    public required string RoleBilingual { get; init; }
    public string? Endpoint { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOffline))]
    [NotifyPropertyChangedFor(nameof(ToolTipText))]
    private bool _isOnline;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAvatarImage))]
    private BitmapImage? _avatarImage;

    public bool IsOffline => !IsOnline;
    public bool HasAvatarImage => AvatarImage is not null;
    public string ToolTipText => $"{FullName} ({RoleBilingual}) — {(IsOnline ? "Đang Online" : "Offline")} • Bấm để chat riêng";
}

public sealed class CustomStickerViewItem
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string FilePath { get; init; }
    public BitmapImage? PreviewImage { get; init; }
}

public sealed partial class ChatConversationListItem : ObservableObject
{
    [ObservableProperty] private ChatConversationEntity _entity = null!;

    public Guid Id => Entity.Id;
    public string Name => Entity.Name;
    public string ConversationType => Entity.ConversationType;
    public string? PinnedTaskSummary => Entity.PinnedTaskSummary;
    public DateTimeOffset UpdatedAtUtc => Entity.UpdatedAtUtc;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnread))]
    private int _unreadCount;

    [ObservableProperty]
    private string _lastUpdatedLocal = string.Empty;

    public bool HasUnread => UnreadCount > 0;
}

public sealed partial class ChatMessageDisplayItem : ObservableObject
{
    public required Guid Id { get; init; }
    public required string SenderMachineId { get; init; }
    public required string SenderFullName { get; init; }
    public required string SenderInitials { get; init; }
    public required string Content { get; init; }
    public required string SentTimeLocal { get; init; }
    public required DateTimeOffset SentAtUtc { get; init; }
    public required string MessageType { get; init; }
    public required bool IsOwnMessage { get; init; }
    public string? AttachmentFileName { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsReverseDownload))]
    private string? _attachmentVaultPath;

    [ObservableProperty]
    private BitmapImage? _stickerImage;

    public bool IsOtherMessage => !IsOwnMessage;
    public bool IsSticker => string.Equals(MessageType, "Sticker", StringComparison.OrdinalIgnoreCase);
    public bool HasAttachment => !string.IsNullOrWhiteSpace(AttachmentFileName);
    public bool HasFileAttachment => HasAttachment && !IsSticker;
    public bool NeedsReverseDownload => HasAttachment && string.IsNullOrWhiteSpace(AttachmentVaultPath);
}

public sealed partial class ChatWorkspaceViewModel : ObservableObject, IDisposable
{
    private readonly ChatService _chatService;
    private readonly WorkTaskService _taskService;
    private readonly LanTaskClient _taskClient;
    private readonly LanCollaborationClient _collabClient;
    private readonly PeerCollabSyncService _collabSync;
    private readonly CustomStickerService _stickerService;
    private readonly ILocalStoragePaths _paths;
    private readonly IEventBus _eventBus;
    private readonly ChatNotificationPopupService? _popupService;
    private readonly IDisposable _chatSubscription;
    private readonly SemaphoreSlim _pollLock = new(1, 1);

    private DateTimeOffset? _lastGlobalActivityUtc;
    private Guid? _pendingSelectConversationId;
    private bool _isLoadingMessages;
    private int _pollTickCounter;

    public ChatWorkspaceViewModel(
        ChatService chatService,
        WorkTaskService taskService,
        LanTaskClient taskClient,
        LanCollaborationClient collabClient,
        PeerCollabSyncService collabSync,
        CustomStickerService stickerService,
        ILocalStoragePaths paths,
        IEventBus eventBus,
        ChatNotificationPopupService? popupService = null)
    {
        _chatService = chatService;
        _taskService = taskService;
        _taskClient = taskClient;
        _collabClient = collabClient;
        _collabSync = collabSync;
        _stickerService = stickerService;
        _paths = paths;
        _eventBus = eventBus;
        _popupService = popupService;

        _chatSubscription = _eventBus.Subscribe<ChatMessageReceivedEvent>(_ =>
            RefreshRequested?.Invoke(this, EventArgs.Empty));
    }

    public ObservableCollection<ChatConversationListItem> Conversations { get; } = [];
    public ObservableCollection<ChatMessageDisplayItem> Messages { get; } = [];
    public ObservableCollection<ChatPeerPresenceItem> PeerPresenceItems { get; } = [];
    public ObservableCollection<ChatPeerPresenceItem> FilteredPeerSearchResults { get; } = [];
    public ObservableCollection<WorkTaskEntity> PinnableTasks { get; } = [];
    public ObservableCollection<CustomStickerViewItem> Stickers { get; } = [];

    [ObservableProperty] private ChatConversationListItem? _selectedConversation;
    [ObservableProperty] private string _peerSearchQuery = string.Empty;
    [ObservableProperty] private bool _isPeerSearchDropdownOpen;
    [ObservableProperty] private string _onlinePeersSummary = "0/0 online";
    [ObservableProperty] private WorkTaskEntity? _selectedTaskToPin;
    [ObservableProperty] private string _messageDraft = string.Empty;
    [ObservableProperty] private string _draftAttachmentPath = string.Empty;
    [ObservableProperty] private string _statusBanner = string.Empty;
    [ObservableProperty] private bool _isStickerTrayOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetachButtonText))]
    private bool _isDetachedWindow;

    public string DetachButtonText => IsDetachedWindow ? "↙ Gắn về cửa sổ chính" : "↗ Tách cửa sổ";

    public event EventHandler? RefreshRequested;
    public event EventHandler? ScrollToBottomRequested;
    public event EventHandler? OpenCreateGroupWindowRequested;
    public event EventHandler? ToggleDetachRequested;

    [RelayCommand]
    public async Task RefreshAllAsync()
    {
        IReadOnlyList<ChatConversationEntity> convs = await _chatService.GetConversationsAsync();
        Guid? prevConvId = _pendingSelectConversationId ?? SelectedConversation?.Id;
        _pendingSelectConversationId = null;

        MergeConversations(convs, markUnread: false);
        SelectedConversation = Conversations.FirstOrDefault(c => c.Id == prevConvId)
                               ?? Conversations.FirstOrDefault();

        await RefreshPeerPresenceAsync();

        IReadOnlyList<WorkTaskEntity> inbox = await _taskService.GetAssignedToMeTasksAsync(includeCompleted: true);
        IReadOnlyList<WorkTaskEntity> outbox = await _taskService.GetAssignedByMeTasksAsync();
        PinnableTasks.Clear();
        foreach (WorkTaskEntity t in inbox.Concat(outbox).DistinctBy(x => x.Id).OrderByDescending(x => x.UpdatedAtUtc))
        {
            PinnableTasks.Add(t);
        }
        SelectedTaskToPin ??= PinnableTasks.FirstOrDefault();

        LoadStickers();
        _lastGlobalActivityUtc = await _chatService.GetGlobalLatestChatActivityUtcAsync();
        await LoadSelectedConversationMessagesAsync();
    }

    public async Task PollIncrementalAsync()
    {
        if (!await _pollLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            _pollTickCounter++;
            if (_pollTickCounter % 2 == 0)
            {
                await RefreshPeerPresenceAsync();
            }

            DateTimeOffset? latestActivity = await _chatService.GetGlobalLatestChatActivityUtcAsync();
            if (_lastGlobalActivityUtc.HasValue && latestActivity == _lastGlobalActivityUtc)
            {
                return;
            }

            _lastGlobalActivityUtc = latestActivity;
            IReadOnlyList<ChatConversationEntity> convs = await _chatService.GetConversationsAsync();
            Guid? selectedId = SelectedConversation?.Id;
            MergeConversations(convs, markUnread: true);

            if (selectedId.HasValue)
            {
                ChatConversationListItem? currentItem = Conversations.FirstOrDefault(c => c.Id == selectedId.Value);
                if (currentItem is not null && !ReferenceEquals(SelectedConversation, currentItem))
                {
                    SelectedConversation = currentItem;
                }
            }

            await AppendNewMessagesForSelectedAsync();
        }
        finally
        {
            _pollLock.Release();
        }
    }

    public async Task RefreshPeerPresenceAsync()
    {
        IReadOnlyList<PeerDirectoryEntryDto> peers = await _taskClient.GetCompanyPeerDirectoryAsync();
        PeerDirectoryEntryDto[] remotePeers = peers
            .Where(x => !string.Equals(x.MachineId, _paths.MachineId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.IsOnline)
            .ThenBy(x => x.FullName)
            .ToArray();

        var existingById = PeerPresenceItems.ToDictionary(p => p.MachineId, StringComparer.OrdinalIgnoreCase);
        bool setChanged = existingById.Count != remotePeers.Length ||
                          remotePeers.Any(r => !existingById.ContainsKey(r.MachineId));

        if (!setChanged)
        {
            foreach (PeerDirectoryEntryDto p in remotePeers)
            {
                ChatPeerPresenceItem item = existingById[p.MachineId];
                item.IsOnline = p.IsOnline;
                item.Endpoint = p.Endpoint;
            }
        }
        else
        {
            PeerPresenceItems.Clear();
            foreach (PeerDirectoryEntryDto p in remotePeers)
            {
                PeerPresenceItems.Add(new ChatPeerPresenceItem
                {
                    MachineId = p.MachineId,
                    Username = p.Username,
                    FullName = p.FullName,
                    ShortName = ExtractShortName(p.FullName),
                    RoleRaw = p.Role,
                    RoleBilingual = CompanyRoles.FormatRolesBilingual(p.Role),
                    Endpoint = p.Endpoint,
                    IsOnline = p.IsOnline,
                    AvatarImage = null
                });
            }

            UpdatePeerSearchFilter();
        }

        int onlineCount = PeerPresenceItems.Count(p => p.IsOnline);
        OnlinePeersSummary = $"{onlineCount}/{PeerPresenceItems.Count} online";
    }

    partial void OnPeerSearchQueryChanged(string value) => UpdatePeerSearchFilter();

    private void UpdatePeerSearchFilter()
    {
        IReadOnlyList<ChatPeerPresenceItem> matches = FilterPeersByQuery(PeerPresenceItems, PeerSearchQuery);
        FilteredPeerSearchResults.Clear();
        foreach (ChatPeerPresenceItem item in matches)
        {
            FilteredPeerSearchResults.Add(item);
        }

        IsPeerSearchDropdownOpen = !string.IsNullOrWhiteSpace(PeerSearchQuery) && FilteredPeerSearchResults.Count > 0;
    }

    public static IReadOnlyList<ChatPeerPresenceItem> FilterPeersByQuery(
        IEnumerable<ChatPeerPresenceItem> source,
        string? query)
    {
        string normalizedQuery = NormalizeForSearch(query);
        if (string.IsNullOrEmpty(normalizedQuery))
        {
            return [];
        }

        return source
            .Where(p =>
                NormalizeForSearch(p.FullName).Contains(normalizedQuery, StringComparison.Ordinal) ||
                NormalizeForSearch(p.Username).Contains(normalizedQuery, StringComparison.Ordinal) ||
                NormalizeForSearch(p.RoleRaw).Contains(normalizedQuery, StringComparison.Ordinal) ||
                NormalizeForSearch(p.RoleBilingual).Contains(normalizedQuery, StringComparison.Ordinal))
            .OrderByDescending(p => p.IsOnline)
            .ThenBy(p => p.FullName)
            .ToArray();
    }

    public static string NormalizeForSearch(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string decomposed = text.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (char ch in decomposed)
        {
            UnicodeCategory uc = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (uc != UnicodeCategory.NonSpacingMark)
            {
                char mapped = ch switch
                {
                    'đ' => 'd',
                    'Đ' => 'D',
                    _ => char.ToLowerInvariant(ch)
                };
                sb.Append(mapped);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string ExtractShortName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return "NV";
        }

        string[] parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 0 ? parts[^1] : fullName;
    }

    public async Task SelectConversationByIdAsync(Guid conversationId)
    {
        _pendingSelectConversationId = conversationId;
        ChatConversationListItem? match = Conversations.FirstOrDefault(c => c.Id == conversationId);
        if (match is null)
        {
            await RefreshAllAsync();
            match = Conversations.FirstOrDefault(c => c.Id == conversationId);
        }

        if (match is not null)
        {
            match.UnreadCount = 0;
            SelectedConversation = match;
        }
    }

    partial void OnSelectedConversationChanged(ChatConversationListItem? value)
    {
        if (value is not null)
        {
            value.UnreadCount = 0;
        }

        if (_popupService is not null)
        {
            _popupService.ActiveViewingConversationId = value?.Id;
        }

        _ = LoadSelectedConversationMessagesAsync();
    }

    public async Task LoadSelectedConversationMessagesAsync()
    {
        if (_isLoadingMessages)
        {
            return;
        }

        _isLoadingMessages = true;
        try
        {
            Messages.Clear();
            if (SelectedConversation is null)
            {
                return;
            }

            IReadOnlyList<ChatMessageEntity> raw = await _chatService.GetMessagesAsync(SelectedConversation.Id);
            foreach (ChatMessageEntity m in raw)
            {
                ChatMessageDisplayItem item = BuildDisplayItem(m);
                Messages.Add(item);
                if (item.IsSticker && item.NeedsReverseDownload)
                {
                    _ = EnsureStickerImageDownloadedAsync(item);
                }
            }

            ScrollToBottomRequested?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _isLoadingMessages = false;
        }
    }

    private async Task AppendNewMessagesForSelectedAsync()
    {
        if (SelectedConversation is null)
        {
            return;
        }

        DateTimeOffset afterUtc = Messages.Count > 0 ? Messages[^1].SentAtUtc : DateTimeOffset.MinValue;
        HashSet<Guid> existingIds = Messages.Select(m => m.Id).ToHashSet();

        IReadOnlyList<ChatMessageEntity> newOnes = await _chatService.GetMessagesAfterAsync(
            SelectedConversation.Id,
            afterUtc,
            existingIds);

        if (newOnes.Count == 0)
        {
            return;
        }

        foreach (ChatMessageEntity m in newOnes)
        {
            ChatMessageDisplayItem item = BuildDisplayItem(m);
            Messages.Add(item);
            if (item.IsSticker && item.NeedsReverseDownload)
            {
                _ = EnsureStickerImageDownloadedAsync(item);
            }
        }

        SelectedConversation.UnreadCount = 0;
        ScrollToBottomRequested?.Invoke(this, EventArgs.Empty);
    }

    private void MergeConversations(IReadOnlyList<ChatConversationEntity> incoming, bool markUnread)
    {
        Guid? activeId = SelectedConversation?.Id;
        var existingById = Conversations.ToDictionary(c => c.Id);

        var ordered = new List<ChatConversationListItem>(incoming.Count);
        foreach (ChatConversationEntity conv in incoming)
        {
            if (existingById.TryGetValue(conv.Id, out ChatConversationListItem? existing))
            {
                bool hasNewActivity = conv.UpdatedAtUtc > existing.UpdatedAtUtc;
                existing.Entity = conv;
                existing.LastUpdatedLocal = conv.UpdatedAtUtc.ToLocalTime().ToString("HH:mm");
                if (markUnread && hasNewActivity && conv.Id != activeId)
                {
                    existing.UnreadCount++;
                }

                ordered.Add(existing);
            }
            else
            {
                ordered.Add(new ChatConversationListItem
                {
                    Entity = conv,
                    UnreadCount = markUnread && conv.Id != activeId ? 1 : 0,
                    LastUpdatedLocal = conv.UpdatedAtUtc.ToLocalTime().ToString("HH:mm")
                });
            }
        }

        bool orderDiffers = ordered.Count != Conversations.Count ||
                            ordered.Where((t, idx) => t.Id != Conversations[idx].Id).Any();
        if (orderDiffers)
        {
            Conversations.Clear();
            foreach (ChatConversationListItem item in ordered)
            {
                Conversations.Add(item);
            }
        }
    }

    private ChatMessageDisplayItem BuildDisplayItem(ChatMessageEntity m)
    {
        bool isOwn = string.Equals(m.SenderMachineId, _paths.MachineId, StringComparison.OrdinalIgnoreCase);
        bool isSticker = string.Equals(m.MessageType, "Sticker", StringComparison.OrdinalIgnoreCase);
        BitmapImage? stickerImg = isSticker ? LoadLocalVaultStickerImage(m.AttachmentVaultPath) : null;

        return new ChatMessageDisplayItem
        {
            Id = m.Id,
            SenderMachineId = m.SenderMachineId,
            SenderFullName = m.SenderFullName,
            SenderInitials = BuildInitials(m.SenderFullName),
            Content = m.Content,
            SentTimeLocal = m.SentAtUtc.ToLocalTime().ToString("dd/MM HH:mm"),
            SentAtUtc = m.SentAtUtc,
            MessageType = string.IsNullOrWhiteSpace(m.MessageType) ? "Text" : m.MessageType,
            IsOwnMessage = isOwn,
            AttachmentFileName = m.AttachmentFileName,
            AttachmentVaultPath = m.AttachmentVaultPath,
            StickerImage = stickerImg
        };
    }

    private async Task EnsureStickerImageDownloadedAsync(ChatMessageDisplayItem item)
    {
        try
        {
            bool ok = await _collabClient.ReverseDownloadChatAttachmentAsync(item.Id);
            if (!ok || SelectedConversation is null)
            {
                return;
            }

            IReadOnlyList<ChatMessageEntity> raw = await _chatService.GetMessagesAsync(SelectedConversation.Id);
            ChatMessageEntity? updated = raw.FirstOrDefault(m => m.Id == item.Id);
            if (updated is not null && !string.IsNullOrWhiteSpace(updated.AttachmentVaultPath))
            {
                item.AttachmentVaultPath = updated.AttachmentVaultPath;
                item.StickerImage = LoadLocalVaultStickerImage(updated.AttachmentVaultPath);
            }
        }
        catch
        {
        }
    }

    private BitmapImage? LoadLocalVaultStickerImage(string? vaultRelPath)
    {
        if (string.IsNullOrWhiteSpace(vaultRelPath))
        {
            return null;
        }

        string fullPath = Path.GetFullPath(Path.Combine(
            _paths.VaultDirectory,
            vaultRelPath.Replace('/', Path.DirectorySeparatorChar)));
        return CustomStickerService.LoadBitmapFrozen(fullPath, 160);
    }

    public void LoadStickers()
    {
        IReadOnlyList<CustomStickerItem> list = _stickerService.GetStickers();
        Stickers.Clear();
        foreach (CustomStickerItem st in list)
        {
            Stickers.Add(new CustomStickerViewItem
            {
                Id = st.Id,
                Name = st.Name,
                FilePath = st.FullPath,
                PreviewImage = CustomStickerService.LoadBitmapFrozen(st.FullPath, 128)
            });
        }
    }

    [RelayCommand]
    private async Task StartDirectChatWithPeerAsync(ChatPeerPresenceItem? peer)
    {
        if (peer is null)
        {
            return;
        }

        PeerSearchQuery = string.Empty;
        IsPeerSearchDropdownOpen = false;

        ChatConversationEntity conv = await _chatService.GetOrCreateDirectConversationAsync(
            peer.MachineId,
            peer.FullName);

        await RefreshAllAsync();
        SelectedConversation = Conversations.FirstOrDefault(c => c.Id == conv.Id);
        StatusBanner = $"Đang trò chuyện riêng với {peer.FullName} ({peer.RoleBilingual}).";
    }

    [RelayCommand]
    private void RequestOpenCreateGroupWindow() => OpenCreateGroupWindowRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ToggleDetachChatWindow() => ToggleDetachRequested?.Invoke(this, EventArgs.Empty);

    public async Task CreateGroupWithMembersAsync(
        string groupName,
        IReadOnlyList<(string MachineId, string FullName)> members)
    {
        if (string.IsNullOrWhiteSpace(groupName))
        {
            return;
        }

        ChatConversationEntity conv = await _chatService.CreateGroupConversationAsync(groupName, members);
        await RefreshAllAsync();
        SelectedConversation = Conversations.FirstOrDefault(c => c.Id == conv.Id);
        StatusBanner = $"Đã tạo nhóm chat '{conv.Name}'.";
    }

    [RelayCommand]
    private void ToggleStickerTray() => IsStickerTrayOpen = !IsStickerTrayOpen;

    [RelayCommand]
    private async Task AddCustomStickerAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Chọn ảnh làm Sticker tùy chỉnh (PNG/JPG/GIF/WebP, tối đa 2 MB)",
            Filter = "Hình ảnh (*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            CustomStickerItem added = await _stickerService.AddStickerAsync(dialog.FileName);
            LoadStickers();
            IsStickerTrayOpen = true;
            StatusBanner = $"Đã thêm sticker '{added.Name}' vào bộ sưu tập.";
        }
        catch (Exception ex)
        {
            StatusBanner = $"Không thể thêm sticker: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteCustomStickerAsync(CustomStickerViewItem? item)
    {
        if (item is null)
        {
            return;
        }

        await _stickerService.DeleteStickerAsync(item.Id);
        LoadStickers();
        StatusBanner = $"Đã xóa sticker '{item.Name}'.";
    }

    [RelayCommand]
    private async Task SendStickerAsync(CustomStickerViewItem? item)
    {
        if (item is null || SelectedConversation is null)
        {
            return;
        }

        string[] targets = ResolveTargetMachineIds(SelectedConversation.Entity);
        ChatMessageEntity sent = await _chatService.SendMessageAsync(
            SelectedConversation.Id,
            item.Name,
            item.FilePath,
            null,
            targets,
            messageType: "Sticker");

        IsStickerTrayOpen = false;
        _ = await _collabSync.FlushPendingChatAsync();
        await AppendSingleOrRefreshAsync(sent);
        StatusBanner = $"Đã gửi sticker '{item.Name}'.";
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
    public async Task SendMessageAsync()
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

        string[] targets = ResolveTargetMachineIds(SelectedConversation.Entity);
        string msgType = string.IsNullOrWhiteSpace(DraftAttachmentPath) ? "Text" : "File";
        ChatMessageEntity sent = await _chatService.SendMessageAsync(
            SelectedConversation.Id,
            MessageDraft,
            string.IsNullOrWhiteSpace(DraftAttachmentPath) ? null : DraftAttachmentPath,
            null,
            targets,
            messageType: msgType);

        MessageDraft = string.Empty;
        DraftAttachmentPath = string.Empty;

        int delivered = await _collabSync.FlushPendingChatAsync();
        StatusBanner = delivered > 0
            ? "Đã gửi và đồng bộ tin nhắn qua LAN."
            : "Đã lưu tin nhắn cục bộ (sẽ tự đồng bộ khi đồng nghiệp Online).";

        await AppendSingleOrRefreshAsync(sent);
    }

    [RelayCommand]
    private async Task PinSelectedTaskAsync()
    {
        if (SelectedConversation is null || SelectedTaskToPin is null)
        {
            StatusBanner = "Chọn cuộc trò chuyện và công việc cần ghim.";
            return;
        }

        string[] targets = ResolveTargetMachineIds(SelectedConversation.Entity);
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
        foreach (ChatPeerPresenceItem peer in PeerPresenceItems.Where(p => p.IsOnline && !string.IsNullOrWhiteSpace(p.Endpoint)))
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

    private async Task AppendSingleOrRefreshAsync(ChatMessageEntity sent)
    {
        _lastGlobalActivityUtc = await _chatService.GetGlobalLatestChatActivityUtcAsync();
        if (Messages.All(m => m.Id != sent.Id))
        {
            Messages.Add(BuildDisplayItem(sent));
            ScrollToBottomRequested?.Invoke(this, EventArgs.Empty);
        }
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

    private static string BuildInitials(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "NV";
        }

        string[] parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 2)
        {
            return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[^1][0])}";
        }

        return parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant();
    }

    public void Dispose()
    {
        _chatSubscription.Dispose();
        _pollLock.Dispose();
    }
}
