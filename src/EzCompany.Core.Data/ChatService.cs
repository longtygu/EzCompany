using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EzCompany.Core.Data;

/// <summary>
/// Manages local-first internal Direct &amp; Group chat conversations, file attachments in <c>Vault\Chat\</c>,
/// pinned <see cref="WorkTaskEntity"/> cards inside chat groups, and peer reverse retrieval.
/// </summary>
public sealed class ChatService
{
    public const long MaxChatAttachmentBytes = 10 * 1024 * 1024;

    private readonly IDbContextFactory<AppDbContext> _appDbFactory;
    private readonly ILocalStoragePaths _paths;
    private readonly ISessionContext _session;
    private readonly IAuditLogger _audit;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;

    public ChatService(
        IDbContextFactory<AppDbContext> appDbFactory,
        ILocalStoragePaths paths,
        ISessionContext session,
        IAuditLogger audit,
        IEventBus eventBus,
        TimeProvider? timeProvider = null)
    {
        _appDbFactory = appDbFactory;
        _paths = paths;
        _session = session;
        _audit = audit;
        _eventBus = eventBus;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public static Guid GetCompanyGeneralGroupId(Guid companyId)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"ezcompany:chat:general:{companyId:D}"));
        return new Guid(hash[..16]);
    }

    public static Guid GetDirectConversationId(Guid companyId, string machineIdA, string machineIdB)
    {
        string[] ordered = [machineIdA.Trim().ToUpperInvariant(), machineIdB.Trim().ToUpperInvariant()];
        Array.Sort(ordered, StringComparer.Ordinal);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"ezcompany:chat:dm:{companyId:D}:{ordered[0]}:{ordered[1]}"));
        return new Guid(hash[..16]);
    }

    public async Task<ChatConversationEntity> EnsureCompanyGeneralGroupAsync(CancellationToken ct = default)
    {
        Guid companyId = _session.CompanyId;
        if (companyId == Guid.Empty)
        {
            await using AppDbContext lookupDb = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            CompanyProfileEntity? company = await lookupDb.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);
            companyId = company?.CompanyId ?? Guid.Empty;
        }

        if (companyId == Guid.Empty)
        {
            throw new InvalidOperationException("Chưa khởi tạo công ty.");
        }

        Guid generalId = GetCompanyGeneralGroupId(companyId);
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        ChatConversationEntity? conv = await db.ChatConversations.FirstOrDefaultAsync(c => c.Id == generalId, ct).ConfigureAwait(false);
        if (conv is not null)
        {
            return conv;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        conv = new ChatConversationEntity
        {
            Id = generalId,
            CompanyId = companyId,
            ConversationType = "Group",
            Name = "Nhóm chung Toàn công ty",
            MemberMachineIdsJson = "[\"ALL\"]",
            MemberNamesJson = "[\"Toàn công ty\"]",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.ChatConversations.Add(conv);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return conv;
    }

    public async Task<ChatConversationEntity> GetOrCreateDirectConversationAsync(
        string peerMachineId,
        string peerFullName,
        CancellationToken ct = default)
    {
        EnsureSession();
        Guid convId = GetDirectConversationId(_session.CompanyId, _paths.MachineId, peerMachineId);

        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        ChatConversationEntity? existing = await db.ChatConversations.FirstOrDefaultAsync(c => c.Id == convId, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        string[] machines = [_paths.MachineId, peerMachineId.Trim()];
        string[] names = [_session.FullName, peerFullName.Trim()];
        var conv = new ChatConversationEntity
        {
            Id = convId,
            CompanyId = _session.CompanyId,
            ConversationType = "Direct",
            Name = $"Chat riêng: {peerFullName.Trim()}",
            MemberMachineIdsJson = JsonSerializer.Serialize(machines),
            MemberNamesJson = JsonSerializer.Serialize(names),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        db.ChatConversations.Add(conv);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return conv;
    }

    public async Task<ChatConversationEntity> CreateGroupConversationAsync(
        string groupName,
        IReadOnlyList<(string MachineId, string FullName)> members,
        CancellationToken ct = default)
    {
        EnsureSession();
        if (string.IsNullOrWhiteSpace(groupName))
        {
            throw new ArgumentException("Vui lòng nhập tên nhóm chat.", nameof(groupName));
        }

        var machineSet = new List<string> { _paths.MachineId };
        var nameSet = new List<string> { _session.FullName };
        foreach ((string m, string n) in members)
        {
            if (!machineSet.Contains(m, StringComparer.OrdinalIgnoreCase))
            {
                machineSet.Add(m);
                nameSet.Add(n);
            }
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        var conv = new ChatConversationEntity
        {
            Id = Guid.NewGuid(),
            CompanyId = _session.CompanyId,
            ConversationType = "Group",
            Name = groupName.Trim(),
            MemberMachineIdsJson = JsonSerializer.Serialize(machineSet),
            MemberNamesJson = JsonSerializer.Serialize(nameSet),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        db.ChatConversations.Add(conv);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return conv;
    }

    public async Task<IReadOnlyList<ChatConversationEntity>> GetConversationsAsync(CancellationToken ct = default)
    {
        _ = await EnsureCompanyGeneralGroupAsync(ct).ConfigureAwait(false);
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        List<ChatConversationEntity> list = await db.ChatConversations.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        return list.OrderByDescending(c => c.UpdatedAtUtc).ToArray();
    }

    public async Task<IReadOnlyList<ChatMessageEntity>> GetMessagesAsync(
        Guid conversationId,
        int limit = 250,
        CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        List<ChatMessageEntity> list = await db.ChatMessages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return list.OrderBy(m => m.SentAtUtc).TakeLast(limit).ToArray();
    }

    public async Task<IReadOnlyList<ChatMessageEntity>> GetMessagesAfterAsync(
        Guid conversationId,
        DateTimeOffset afterSentAtUtc,
        IReadOnlyCollection<Guid> excludeIds,
        CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        DateTimeOffset lowerBound = afterSentAtUtc.AddSeconds(-1);
        List<ChatMessageEntity> candidates = await db.ChatMessages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId && m.SentAtUtc >= lowerBound)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return candidates
            .Where(m => !excludeIds.Contains(m.Id) && (m.SentAtUtc > afterSentAtUtc || m.SentAtUtc == afterSentAtUtc))
            .OrderBy(m => m.SentAtUtc)
            .ToArray();
    }

    public async Task<(int Count, DateTimeOffset? LatestSentAtUtc, Guid? LatestMessageId)> GetConversationMarkerAsync(
        Guid conversationId,
        CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        int count = await db.ChatMessages.AsNoTracking()
            .CountAsync(m => m.ConversationId == conversationId, ct)
            .ConfigureAwait(false);

        if (count == 0)
        {
            return (0, null, null);
        }

        var latest = await db.ChatMessages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.SentAtUtc)
            .Select(m => new { m.Id, m.SentAtUtc })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return (count, latest?.SentAtUtc, latest?.Id);
    }

    public async Task<DateTimeOffset?> GetGlobalLatestChatActivityUtcAsync(CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        DateTimeOffset? latestMsg = await db.ChatMessages.AsNoTracking()
            .OrderByDescending(m => m.SentAtUtc)
            .Select(m => (DateTimeOffset?)m.SentAtUtc)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        DateTimeOffset? latestConv = await db.ChatConversations.AsNoTracking()
            .OrderByDescending(c => c.UpdatedAtUtc)
            .Select(c => (DateTimeOffset?)c.UpdatedAtUtc)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (!latestMsg.HasValue) return latestConv;
        if (!latestConv.HasValue) return latestMsg;
        return latestMsg.Value >= latestConv.Value ? latestMsg : latestConv;
    }

    public async Task<ChatMessageEntity> SendMessageAsync(
        Guid conversationId,
        string content,
        string? sourceAttachmentPath,
        Guid? linkedTaskId,
        IReadOnlyList<string> targetMachineIds,
        CancellationToken ct = default,
        string messageType = "Text")
    {
        EnsureSession();
        if (string.IsNullOrWhiteSpace(content) && string.IsNullOrWhiteSpace(sourceAttachmentPath))
        {
            throw new ArgumentException("Vui lòng nhập nội dung tin nhắn hoặc chọn tệp đính kèm.");
        }

        string? savedFileName = null;
        string? savedRelPath = null;
        string? savedSha = null;
        long savedSize = 0;

        if (!string.IsNullOrWhiteSpace(sourceAttachmentPath))
        {
            if (!File.Exists(sourceAttachmentPath))
            {
                throw new FileNotFoundException("Không tìm thấy tệp đính kèm.", sourceAttachmentPath);
            }

            byte[] bytes = await File.ReadAllBytesAsync(sourceAttachmentPath, ct).ConfigureAwait(false);
            if (bytes.Length > MaxChatAttachmentBytes)
            {
                throw new InvalidOperationException("Tệp đính kèm chat vượt quá 10 MB.");
            }

            savedFileName = SanitizeFileName(Path.GetFileName(sourceAttachmentPath));
            string relPath = Path.Combine("Chat", conversationId.ToString("N"), $"{Guid.NewGuid():N}_{savedFileName}");
            string fullDest = Path.Combine(_paths.VaultDirectory, relPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullDest)!);
            await File.WriteAllBytesAsync(fullDest, bytes, ct).ConfigureAwait(false);

            savedRelPath = relPath.Replace('\\', '/');
            savedSha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            savedSize = bytes.Length;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        string[] targets = targetMachineIds
            .Where(m => !string.IsNullOrWhiteSpace(m) && !string.Equals(m, _paths.MachineId, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        string normalizedType = string.IsNullOrWhiteSpace(messageType) ? "Text" : messageType.Trim();
        var msg = new ChatMessageEntity
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            CompanyId = _session.CompanyId,
            SenderMachineId = _paths.MachineId,
            SenderUsername = _session.Username,
            SenderFullName = _session.FullName,
            Content = content?.Trim() ?? string.Empty,
            MessageType = normalizedType,
            AttachmentFileName = savedFileName,
            AttachmentVaultPath = savedRelPath,
            AttachmentSha256 = savedSha,
            AttachmentSizeBytes = savedSize,
            LinkedTaskId = linkedTaskId,
            SentAtUtc = now,
            PendingSyncTargetsJson = targets.Length > 0 ? JsonSerializer.Serialize(targets) : null
        };

        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        ChatConversationEntity? conv = await db.ChatConversations.FirstOrDefaultAsync(c => c.Id == conversationId, ct).ConfigureAwait(false);
        if (conv is not null)
        {
            conv.UpdatedAtUtc = now;
        }

        db.ChatMessages.Add(msg);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        string preview = string.Equals(normalizedType, "Sticker", StringComparison.OrdinalIgnoreCase)
            ? $"[Sticker: {(string.IsNullOrWhiteSpace(msg.Content) ? savedFileName : msg.Content)}]"
            : string.IsNullOrWhiteSpace(msg.Content)
                ? $"[Tệp: {savedFileName}]"
                : msg.Content;
        _eventBus.Publish(new ChatMessageReceivedEvent(
            conversationId,
            msg.Id,
            _session.FullName,
            preview,
            now,
            _paths.MachineId,
            normalizedType));
        return msg;
    }

    public async Task<ChatMessageEntity> PinTaskToConversationAsync(
        Guid conversationId,
        Guid taskId,
        IReadOnlyList<string> targetMachineIds,
        CancellationToken ct = default)
    {
        EnsureSession();
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        WorkTaskEntity task = await db.WorkTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Không tìm thấy công việc để ghim.");

        ChatConversationEntity conv = await db.ChatConversations.FirstOrDefaultAsync(c => c.Id == conversationId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Không tìm thấy cuộc trò chuyện.");

        DateTimeOffset now = _timeProvider.GetUtcNow();
        string summary = $"[{task.Priority}] {task.Title} • Trạng thái: {task.Status} • Giao: {task.AssignerFullName} -> Nhận: {task.AssigneeFullName}";
        conv.PinnedTaskId = task.Id;
        conv.PinnedTaskSummary = summary;
        conv.UpdatedAtUtc = now;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync("CHAT_TASK_PINNED", "core.chat", task.Title, $"Conversation={conv.Name}", "Info", ct)
            .ConfigureAwait(false);

        return await SendMessageAsync(
            conversationId,
            $"📌 Đã ghim công việc: {summary}",
            null,
            task.Id,
            targetMachineIds,
            ct).ConfigureAwait(false);
    }

    public async Task<bool> UpsertIncomingChatSyncAsync(
        ChatConversationEntity incomingConv,
        ChatMessageEntity incomingMsg,
        byte[]? attachmentBytes,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(incomingConv);
        ArgumentNullException.ThrowIfNull(incomingMsg);

        string? savedRelPath = null;
        if (attachmentBytes is { Length: > 0 } && !string.IsNullOrWhiteSpace(incomingMsg.AttachmentFileName))
        {
            if (attachmentBytes.Length > MaxChatAttachmentBytes)
            {
                throw new InvalidOperationException("Tệp đính kèm chat vượt quá 10 MB.");
            }

            string computedSha = Convert.ToHexString(SHA256.HashData(attachmentBytes)).ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(incomingMsg.AttachmentSha256) &&
                !string.Equals(computedSha, incomingMsg.AttachmentSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new CryptographicException("SHA-256 tệp chat đồng bộ không khớp.");
            }

            string safeName = SanitizeFileName(incomingMsg.AttachmentFileName);
            string relPath = Path.Combine("Chat", incomingConv.Id.ToString("N"), $"{incomingMsg.Id:N}_{safeName}");
            string fullDest = Path.Combine(_paths.VaultDirectory, relPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullDest)!);
            await File.WriteAllBytesAsync(fullDest, attachmentBytes, ct).ConfigureAwait(false);
            savedRelPath = relPath.Replace('\\', '/');
        }

        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        ChatConversationEntity? conv = await db.ChatConversations
            .FirstOrDefaultAsync(c => c.Id == incomingConv.Id, ct)
            .ConfigureAwait(false);

        if (conv is null)
        {
            conv = new ChatConversationEntity
            {
                Id = incomingConv.Id,
                CompanyId = incomingConv.CompanyId,
                ConversationType = incomingConv.ConversationType,
                Name = incomingConv.Name,
                MemberMachineIdsJson = incomingConv.MemberMachineIdsJson,
                MemberNamesJson = incomingConv.MemberNamesJson,
                PinnedTaskId = incomingConv.PinnedTaskId,
                PinnedTaskSummary = incomingConv.PinnedTaskSummary,
                CreatedAtUtc = incomingConv.CreatedAtUtc,
                UpdatedAtUtc = incomingConv.UpdatedAtUtc
            };
            db.ChatConversations.Add(conv);
        }
        else if (incomingConv.UpdatedAtUtc >= conv.UpdatedAtUtc)
        {
            conv.UpdatedAtUtc = incomingConv.UpdatedAtUtc;
            if (incomingConv.PinnedTaskId.HasValue)
            {
                conv.PinnedTaskId = incomingConv.PinnedTaskId;
                conv.PinnedTaskSummary = incomingConv.PinnedTaskSummary;
            }
        }

        bool exists = await db.ChatMessages.AnyAsync(m => m.Id == incomingMsg.Id, ct).ConfigureAwait(false);
        if (exists)
        {
            if (savedRelPath is not null)
            {
                ChatMessageEntity existingMsg = await db.ChatMessages.FirstAsync(m => m.Id == incomingMsg.Id, ct).ConfigureAwait(false);
                existingMsg.AttachmentVaultPath ??= savedRelPath;
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return false;
        }

        string incomingType = string.IsNullOrWhiteSpace(incomingMsg.MessageType) ? "Text" : incomingMsg.MessageType.Trim();
        db.ChatMessages.Add(new ChatMessageEntity
        {
            Id = incomingMsg.Id,
            ConversationId = incomingMsg.ConversationId,
            CompanyId = incomingMsg.CompanyId,
            SenderMachineId = incomingMsg.SenderMachineId,
            SenderUsername = incomingMsg.SenderUsername,
            SenderFullName = incomingMsg.SenderFullName,
            Content = incomingMsg.Content,
            MessageType = incomingType,
            AttachmentFileName = incomingMsg.AttachmentFileName,
            AttachmentVaultPath = savedRelPath,
            AttachmentSha256 = incomingMsg.AttachmentSha256,
            AttachmentSizeBytes = incomingMsg.AttachmentSizeBytes,
            LinkedTaskId = incomingMsg.LinkedTaskId,
            SentAtUtc = incomingMsg.SentAtUtc,
            PendingSyncTargetsJson = null
        });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (!string.Equals(incomingMsg.SenderMachineId, _paths.MachineId, StringComparison.OrdinalIgnoreCase))
        {
            string preview = string.Equals(incomingType, "Sticker", StringComparison.OrdinalIgnoreCase)
                ? $"[Sticker: {(string.IsNullOrWhiteSpace(incomingMsg.Content) ? incomingMsg.AttachmentFileName : incomingMsg.Content)}]"
                : string.IsNullOrWhiteSpace(incomingMsg.Content)
                    ? $"[Tệp: {incomingMsg.AttachmentFileName}]"
                    : incomingMsg.Content;
            _eventBus.Publish(new ChatMessageReceivedEvent(
                incomingMsg.ConversationId,
                incomingMsg.Id,
                incomingMsg.SenderFullName,
                preview,
                incomingMsg.SentAtUtc,
                incomingMsg.SenderMachineId,
                incomingType));
        }

        return true;
    }

    public async Task SaveDownloadedAttachmentToLocalVaultAsync(
        Guid messageId,
        byte[] fileBytes,
        CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        ChatMessageEntity msg = await db.ChatMessages.FirstOrDefaultAsync(m => m.Id == messageId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Không tìm thấy tin nhắn chat.");

        string safeName = SanitizeFileName(msg.AttachmentFileName ?? "attachment.bin");
        string computedSha = Convert.ToHexString(SHA256.HashData(fileBytes)).ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(msg.AttachmentSha256) &&
            !string.Equals(computedSha, msg.AttachmentSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new CryptographicException("Mã SHA-256 tệp chat tải về không khớp.");
        }

        string relPath = Path.Combine("Chat", msg.ConversationId.ToString("N"), $"{msg.Id:N}_{safeName}");
        string fullDest = Path.Combine(_paths.VaultDirectory, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullDest)!);
        await File.WriteAllBytesAsync(fullDest, fileBytes, ct).ConfigureAwait(false);

        msg.AttachmentVaultPath = relPath.Replace('\\', '/');
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<(ChatConversationEntity Conv, ChatMessageEntity Msg)>> GetMessagesSinceAsync(
        DateTimeOffset sinceUtc,
        int limit = 200,
        CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        List<ChatMessageEntity> msgs = (await db.ChatMessages.AsNoTracking()
            .Where(m => m.SentAtUtc > sinceUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false))
            .OrderBy(m => m.SentAtUtc)
            .Take(limit)
            .ToList();

        if (msgs.Count == 0)
        {
            return [];
        }

        Guid[] convIds = msgs.Select(m => m.ConversationId).Distinct().ToArray();
        Dictionary<Guid, ChatConversationEntity> convMap = await db.ChatConversations.AsNoTracking()
            .Where(c => convIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct)
            .ConfigureAwait(false);

        var result = new List<(ChatConversationEntity, ChatMessageEntity)>();
        foreach (ChatMessageEntity m in msgs)
        {
            if (convMap.TryGetValue(m.ConversationId, out ChatConversationEntity? c))
            {
                result.Add((c, m));
            }
        }

        return result;
    }

    private void EnsureSession()
    {
        if (!_session.IsAuthenticated || _session.CompanyId == Guid.Empty)
        {
            throw new InvalidOperationException("Phiên làm việc chưa đăng nhập.");
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string cleaned = new(fileName.Where(c => !invalid.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "file.bin" : cleaned;
    }
}
