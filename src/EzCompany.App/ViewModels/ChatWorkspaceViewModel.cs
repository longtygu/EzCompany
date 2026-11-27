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
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name))]
    [NotifyPropertyChangedFor(nameof(ConversationType))]
    [NotifyPropertyChangedFor(nameof(PinnedTaskSummary))]
    [NotifyPropertyChangedFor(nameof(UpdatedAtUtc))]
    private ChatConversationEntity _entity = null!;

    public Guid Id => Entity.Id;
    public string Name => Entity.Name;
    public string ConversationType => Entity.ConversationType;
    public string MemberMachineIdsJson => Entity.MemberMachineIdsJson;
    public string? PinnedTaskSummary => Entity.PinnedTaskSummary;
    public DateTimeOffset UpdatedAtUtc => Entity.UpdatedAtUtc;
    public bool IsGroupConversation => string.Equals(Entity.ConversationType, "Group", StringComparison.OrdinalIgnoreCase);

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
    [NotifyPropertyChangedFor(nameof(HasInlineImage))]
    private BitmapImage? _inlineImage;

    [ObservableProperty]
    private BitmapImage? _stickerImage;

    public Guid? ReplyToMessageId { get; init; }
    public string? ReplyPreviewText { get; init; }
    public bool HasReplyPreview => !string.IsNullOrWhiteSpace(ReplyPreviewText);

    public bool IsOtherMessage => !IsOwnMessage;
    public bool IsSticker => string.Equals(MessageType, "Sticker", StringComparison.OrdinalIgnoreCase);
    public bool HasAttachment => !string.IsNullOrWhiteSpace(AttachmentFileName);
    public bool HasFileAttachment => HasAttachment && !IsSticker;
    public bool HasInlineImage => InlineImage is not null;
    public bool IsImageAttachment => HasFileAttachment && IsImageFileName(AttachmentFileName);
    public bool NeedsReverseDownload => HasAttachment && string.IsNullOrWhiteSpace(AttachmentVaultPath);

    public static bool IsImageFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        string ext = Path.GetExtension(fileName);
        return ext.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".webp", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".gif", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed partial class ChatWorkspaceViewModel : ObservableObject, IDisposable
{
    private readonly ChatService _chatService;
    private readonly WorkTaskService _taskService;
    private readonly LanTaskClient _taskClient;
    private readonly LanCollaborationClient _collabClient;
    private readonly PeerCollabSyncService _collabSync;
    private readonly CustomStickerService _stickerService;
    private readonly UserAvatarService _avatars;
    private readonly ILocalStoragePaths _paths;
    private readonly IEventBus _eventBus;
    private readonly ChatNotificationPopupService? _popupService;
    private readonly IDisposable _chatSubscription;
    private readonly IDisposable _avatarSubscription;
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
        UserAvatarService avatars,
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
        _avatars = avatars;
        _paths = paths;
        _eventBus = eventBus;
        _popupService = popupService;

        _chatSubscription = _eventBus.Subscribe<ChatMessageReceivedEvent>(_ =>
            RefreshRequested?.Invoke(this, EventArgs.Empty));
        _avatarSubscription = _eventBus.Subscribe<UserAvatarUpdatedEvent>(_ =>
            AvatarRefreshRequested?.Invoke(this, EventArgs.Empty));
    }

    public ObservableCollection<ChatConversationListItem> Conversations { get; } = [];
    public ObservableCollection<ChatMessageDisplayItem> Messages { get; } = [];
    public ObservableCollection<ChatMessageDisplayItem> VisibleMessages { get; } = [];
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
    [ObservableProperty] private string _messageSearchQuery = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReplyingToMessage))]
    [NotifyPropertyChangedFor(nameof(ReplyingToPreview))]
    private ChatMessageDisplayItem? _replyingToMessage;
    public bool HasReplyingToMessage => ReplyingToMessage is not null;
    public string ReplyingToPreview => ReplyingToMessage is null
        ? string.Empty
        : $"Đang trả lời {ReplyingToMessage.SenderFullName}: {(string.IsNullOrWhiteSpace(ReplyingToMessage.Content) ? ReplyingToMessage.AttachmentFileName : ReplyingToMessage.Content)}";

    [ObservableProperty] private string _statusBanner = string.Empty;
    [ObservableProperty] private bool _isStickerTrayOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLanDisconnected))]
    private bool _isLanConnected;

    public bool IsLanDisconnected => !IsLanConnected;

    [ObservableProperty] private string _connectionStatusText = "● Đang kiểm tra kết nối...";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetachButtonText))]
    private bool _isDetachedWindow;

    public string DetachButtonText => IsDetachedWindow ? "↙ Gắn về cửa sổ chính" : "↗ Tách cửa sổ";

    public event EventHandler? RefreshRequested;
    public event EventHandler? AvatarRefreshRequested;
    public event EventHandler? ScrollToBottomRequested;
    public event EventHandler? OpenCreateGroupWindowRequested;
    public event EventHandler? OpenManageGroupWindowRequested;
    public event EventHandler? ToggleDetachRequested;
    public event EventHandler<ChatMessageDisplayItem>? PreviewImageRequested;

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
        IReadOnlyList<PeerDirectoryEntryDto> peers;
        try
        {
            peers = await _taskClient.GetCompanyPeerDirectoryAsync();
        }
        catch
        {
            UpdateConnectionState(directoryOk: false, onlinePeers: 0, totalPeers: PeerPresenceItems.Count);
            return;
        }

        PeerDirectoryEntryDto[] remotePeers = peers
            .Where(x => !string.Equals(x.MachineId, _paths.MachineId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.IsOnline)
            .ThenBy(x => x.FullName)
            .ToArray();

        Dictionary<string, byte[]> avatarMap = await _avatars.GetAvatarMapAsync();

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
                if (avatarMap.TryGetValue(p.MachineId, out byte[]? avBytes))
                {
                    item.AvatarImage ??= LoadAvatarFrozen(avBytes);
                }
                else
                {
                    item.AvatarImage = null;
                }
            }
        }
        else
        {
            PeerPresenceItems.Clear();
            foreach (PeerDirectoryEntryDto p in remotePeers)
            {
                BitmapImage? avBmp = avatarMap.TryGetValue(p.MachineId, out byte[]? avBytes)
                    ? LoadAvatarFrozen(avBytes)
                    : null;
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
                    AvatarImage = avBmp
                });
            }

            UpdatePeerSearchFilter();
        }

        int onlineCount = PeerPresenceItems.Count(p => p.IsOnline);
        OnlinePeersSummary = $"{onlineCount}/{PeerPresenceItems.Count} online";
        UpdateConnectionState(directoryOk: true, onlinePeers: onlineCount, totalPeers: PeerPresenceItems.Count);
    }

    private void UpdateConnectionState(bool directoryOk, int onlinePeers, int totalPeers)
    {
        bool networkAvailable = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
        (bool isConnected, string text) = ComputeConnectionState(networkAvailable, directoryOk, onlinePeers, totalPeers);
        IsLanConnected = isConnected;
        ConnectionStatusText = text;
    }

    public static (bool IsConnected, string Text) ComputeConnectionState(
        bool networkAvailable,
        bool directoryOk,
        int onlinePeers,
        int totalPeers)
    {
        if (!networkAvailable)
        {
            return (false, "● Mất kết nối mạng");
        }

        if (!directoryOk)
        {
            return (false, "● Mất kết nối LAN");
        }

        if (onlinePeers <= 0)
        {
            return (false, totalPeers == 0
                ? "● Mất kết nối — chưa có máy nào trong LAN"
                : $"● Mất kết nối — 0/{totalPeers} máy online");
        }

        return (true, $"● Đã kết nối LAN • {onlinePeers}/{totalPeers} máy online");
    }

    partial void OnPeerSearchQueryChanged(string value) => UpdatePeerSearchFilter();
    partial void OnMessageSearchQueryChanged(string value) => RebuildVisibleMessages();

    private void RebuildVisibleMessages()
    {
        string q = NormalizeForSearch(MessageSearchQuery);
        VisibleMessages.Clear();
        IEnumerable<ChatMessageDisplayItem> filtered = string.IsNullOrEmpty(q)
            ? Messages
            : Messages.Where(m =>
                NormalizeForSearch(m.Content).Contains(q, StringComparison.Ordinal) ||
                NormalizeForSearch(m.SenderFullName).Contains(q, StringComparison.Ordinal) ||
                NormalizeForSearch(m.AttachmentFileName).Contains(q, StringComparison.Ordinal));

        foreach (ChatMessageDisplayItem item in filtered)
        {
            VisibleMessages.Add(item);
        }
    }

    private static BitmapImage? LoadAvatarFrozen(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 96;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

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
            VisibleMessages.Clear();
            if (SelectedConversation is null)
            {
                return;
            }

            IReadOnlyList<ChatMessageEntity> raw = await _chatService.GetMessagesAsync(SelectedConversation.Id);
            foreach (ChatMessageEntity m in raw)
            {
                ChatMessageDisplayItem item = BuildDisplayItem(m);
                Messages.Add(item);
                if ((item.IsSticker || item.IsImageAttachment) && item.NeedsReverseDownload)
                {
                    _ = EnsureStickerImageDownloadedAsync(item);
                }
            }

            RebuildVisibleMessages();
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
            if ((item.IsSticker || item.IsImageAttachment) && item.NeedsReverseDownload)
            {
                _ = EnsureStickerImageDownloadedAsync(item);
            }
        }

        SelectedConversation.UnreadCount = 0;
        RebuildVisibleMessages();
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
        BitmapImage? stickerImg = isSticker ? LoadLocalVaultStickerImage(m.AttachmentVaultPath, 160) : null;
        BitmapImage? inlineImg = !isSticker && ChatMessageDisplayItem.IsImageFileName(m.AttachmentFileName)
            ? LoadLocalVaultStickerImage(m.AttachmentVaultPath, 420)
            : null;

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
            StickerImage = stickerImg,
            InlineImage = inlineImg,
            ReplyToMessageId = m.ReplyToMessageId,
            ReplyPreviewText = m.ReplyPreviewText
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
                if (item.IsSticker)
                {
                    item.StickerImage = LoadLocalVaultStickerImage(updated.AttachmentVaultPath, 160);
                }
                else if (item.IsImageAttachment)
                {
                    item.InlineImage = LoadLocalVaultStickerImage(updated.AttachmentVaultPath, 420);
                }
            }
        }
        catch
        {
        }
    }

    private BitmapImage? LoadLocalVaultStickerImage(string? vaultRelPath, int decodePixelWidth = 160)
    {
        if (string.IsNullOrWhiteSpace(vaultRelPath))
        {
            return null;
        }

        string fullPath = Path.GetFullPath(Path.Combine(
            _paths.VaultDirectory,
            vaultRelPath.Replace('/', Path.DirectorySeparatorChar)));
        return CustomStickerService.LoadBitmapFrozen(fullPath, decodePixelWidth);
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
    private void RequestOpenManageGroupWindow()
    {
        if (SelectedConversation is null || !SelectedConversation.IsGroupConversation)
        {
            StatusBanner = "Chỉ có thể đổi tên / sửa thành viên đối với nhóm chat.";
            return;
        }

        OpenManageGroupWindowRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ToggleDetachChatWindow() => ToggleDetachRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ClearMessageSearch() => MessageSearchQuery = string.Empty;

    [RelayCommand]
    private void StartReply(ChatMessageDisplayItem? item)
    {
        if (item is null)
        {
            return;
        }

        ReplyingToMessage = item;
    }

    [RelayCommand]
    private void CancelReply() => ReplyingToMessage = null;

    [RelayCommand]
    private void PreviewImageMessage(ChatMessageDisplayItem? item)
    {
        if (item is not null && item.IsImageAttachment)
        {
            PreviewImageRequested?.Invoke(this, item);
        }
    }

    public async Task UpdateGroupMembersAsync(
        string newName,
        IReadOnlyList<(string MachineId, string FullName)> members)
    {
        if (SelectedConversation is null)
        {
            return;
        }

        try
        {
            await _chatService.UpdateGroupConversationAsync(SelectedConversation.Id, newName, members);
            _ = await _collabSync.FlushPendingChatAsync();
            await RefreshAllAsync();
            StatusBanner = $"Đã cập nhật nhóm chat '{newName}'.";
        }
        catch (Exception ex)
        {
            StatusBanner = $"Lỗi cập nhật nhóm: {ex.Message}";
        }
    }

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
        Guid? replyId = ReplyingToMessage?.Id;
        string? replySnippet = ReplyingToMessage is null
            ? null
            : $"{ReplyingToMessage.SenderFullName}: {(string.IsNullOrWhiteSpace(ReplyingToMessage.Content) ? ReplyingToMessage.AttachmentFileName : ReplyingToMessage.Content)}";

        ChatMessageEntity sent = await _chatService.SendMessageAsync(
            SelectedConversation.Id,
            MessageDraft,
            string.IsNullOrWhiteSpace(DraftAttachmentPath) ? null : DraftAttachmentPath,
            null,
            targets,
            messageType: msgType,
            replyToMessageId: replyId,
            replyPreviewText: replySnippet);

        MessageDraft = string.Empty;
        DraftAttachmentPath = string.Empty;
        ReplyingToMessage = null;

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
    private async Task UnpinTaskAsync()
    {
        if (SelectedConversation is null || string.IsNullOrWhiteSpace(SelectedConversation.PinnedTaskSummary))
        {
            StatusBanner = "Đoạn chat này chưa ghim công việc nào.";
            return;
        }

        string[] targets = ResolveTargetMachineIds(SelectedConversation.Entity);
        try
        {
            await _chatService.UnpinTaskFromConversationAsync(SelectedConversation.Id, targets);
        }
        catch (Exception ex)
        {
            StatusBanner = $"Không thể bỏ ghim: {ex.Message}";
            return;
        }

        _ = await _collabSync.FlushPendingChatAsync();
        await RefreshAllAsync();
        StatusBanner = "Đã bỏ ghim công việc khỏi đoạn chat.";
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
            RebuildVisibleMessages();
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
        _avatarSubscription.Dispose();
        _pollLock.Dispose();
    }
}
