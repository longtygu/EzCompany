using System.Globalization;

namespace EzCompany.Core.Updater;

/// <summary>
/// Semantic Versioning 2.0.0 parser and comparator.
/// </summary>
public readonly record struct SemVerVersion(
    int Major,
    int Minor,
    int Patch,
    string? Prerelease) : IComparable<SemVerVersion>
{
    public bool IsPrerelease => !string.IsNullOrWhiteSpace(Prerelease);

    public static bool TryParse(string? input, out SemVerVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        string clean = input.Trim();
        if (clean.StartsWith('v') || clean.StartsWith('V'))
        {
            clean = clean[1..];
        }

        int plusIdx = clean.IndexOf('+');
        if (plusIdx >= 0)
        {
            clean = clean[..plusIdx];
        }

        string? prerelease = null;
        int dashIdx = clean.IndexOf('-');
        if (dashIdx >= 0)
        {
            prerelease = clean[(dashIdx + 1)..];
            if (string.IsNullOrWhiteSpace(prerelease))
            {
                return false;
            }

            clean = clean[..dashIdx];
        }

        string[] nums = clean.Split('.');
        if (nums.Length is < 2 or > 4)
        {
            return false;
        }

        if (!int.TryParse(nums[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major) ||
            !int.TryParse(nums[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minor))
        {
            return false;
        }

        int patch = 0;
        if (nums.Length >= 3 && !int.TryParse(nums[2], NumberStyles.None, CultureInfo.InvariantCulture, out patch))
        {
            return false;
        }

        version = new SemVerVersion(major, minor, patch, prerelease);
        return true;
    }

    public static SemVerVersion Parse(string input) =>
        TryParse(input, out SemVerVersion v)
            ? v
            : throw new FormatException($"Invalid SemVer string: '{input}'.");

    public int CompareTo(SemVerVersion other)
    {
        int c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;

        bool thisPre = IsPrerelease;
        bool otherPre = other.IsPrerelease;
        if (!thisPre && !otherPre) return 0;
        if (!thisPre && otherPre) return 1;
        if (thisPre && !otherPre) return -1;

        string[] a = Prerelease!.Split('.');
        string[] b = other.Prerelease!.Split('.');
        int min = Math.Min(a.Length, b.Length);
        for (int i = 0; i < min; i++)
        {
            bool aNum = int.TryParse(a[i], NumberStyles.None, CultureInfo.InvariantCulture, out int ai);
            bool bNum = int.TryParse(b[i], NumberStyles.None, CultureInfo.InvariantCulture, out int bi);
            int cmp = (aNum, bNum) switch
            {
                (true, true) => ai.CompareTo(bi),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(a[i], b[i])
            };

            if (cmp != 0) return cmp;
        }

        return a.Length.CompareTo(b.Length);
    }

    public static bool operator <(SemVerVersion left, SemVerVersion right) => left.CompareTo(right) < 0;
    public static bool operator >(SemVerVersion left, SemVerVersion right) => left.CompareTo(right) > 0;
    public static bool operator <=(SemVerVersion left, SemVerVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >=(SemVerVersion left, SemVerVersion right) => left.CompareTo(right) >= 0;

    public override string ToString() =>
        IsPrerelease ? $"{Major}.{Minor}.{Patch}-{Prerelease}" : $"{Major}.{Minor}.{Patch}";
}

public enum UpdateApprovalStatus
{
    Allowed = 0,
    UpToDate = 1,
    BlockedExceedsMaxApproved = 2,
    BlockedPrereleaseChannel = 3,
    BlockedInvalidVersion = 4
}

public sealed record UpdateApprovalDecision(
    bool IsAllowed,
    UpdateApprovalStatus Status,
    string Reason);

/// <summary>
/// Enforces <c>MaxApprovedVersion</c> governance from <c>policy.lic</c>.
/// Employee nodes may install updates only up to <c>MaxApprovedVersion</c>;
/// Director nodes are permitted to preview newer versions first.
/// </summary>
public sealed class UpdatePolicyGate
{
    public UpdateApprovalDecision Evaluate(
        string currentVersion,
        string candidateVersion,
        bool isDirectorNode,
        string? maxApprovedVersion,
        string? allowedChannel)
    {
        if (!SemVerVersion.TryParse(currentVersion, out SemVerVersion current) ||
            !SemVerVersion.TryParse(candidateVersion, out SemVerVersion candidate))
        {
            return new UpdateApprovalDecision(false, UpdateApprovalStatus.BlockedInvalidVersion, "Định dạng phiên bản SemVer không hợp lệ.");
        }

        if (candidate <= current)
        {
            return new UpdateApprovalDecision(false, UpdateApprovalStatus.UpToDate, $"Đang ở phiên bản mới nhất ({current}).");
        }

        if (isDirectorNode)
        {
            return new UpdateApprovalDecision(
                true,
                UpdateApprovalStatus.Allowed,
                $"Máy Giám đốc được phép tải trước bản {candidate} để kiểm thử.");
        }

        bool betaAllowed = string.Equals(allowedChannel, "beta", StringComparison.OrdinalIgnoreCase);
        if (candidate.IsPrerelease && !betaAllowed)
        {
            return new UpdateApprovalDecision(
                false,
                UpdateApprovalStatus.BlockedPrereleaseChannel,
                $"Phiên bản {candidate} thuộc kênh beta/prerelease nhưng máy chỉ cho phép kênh stable.");
        }

        if (!SemVerVersion.TryParse(maxApprovedVersion, out SemVerVersion maxApproved))
        {
            return new UpdateApprovalDecision(
                false,
                UpdateApprovalStatus.BlockedInvalidVersion,
                "Thiếu MaxApprovedVersion hợp lệ trong policy.lic.");
        }

        if (candidate > maxApproved)
        {
            return new UpdateApprovalDecision(
                false,
                UpdateApprovalStatus.BlockedExceedsMaxApproved,
                $"Phiên bản {candidate} vượt quá ngưỡng Giám đốc đã phê duyệt ({maxApproved}).");
        }

        return new UpdateApprovalDecision(
            true,
            UpdateApprovalStatus.Allowed,
            $"Phiên bản {candidate} nằm trong ngưỡng phê duyệt ({maxApproved}).");
    }
}
