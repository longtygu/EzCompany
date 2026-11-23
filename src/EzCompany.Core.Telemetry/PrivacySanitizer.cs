using System.Globalization;
using System.Text;

namespace EzCompany.Core.Telemetry;

/// <summary>
/// Redacts window titles containing banking, authentication, personal messaging, or private-browsing keywords.
/// </summary>
public static class PrivacySanitizer
{
    public const string RedactedPlaceholder = "[Đã ẩn nội dung riêng tư]";

    private static readonly string[] SensitiveKeywordsNormalized =
    [
        "ngan hang",
        "banking",
        "vietcombank",
        "vcb digibank",
        "techcombank",
        "mbbank",
        "mb bank",
        "bidv",
        "vietinbank",
        "acb",
        "vpbank",
        "tpbank",
        "sacombank",
        "agribank",
        "hdbank",
        "momo",
        "zalopay",
        "vnpay",
        "otp",
        "mat khau",
        "password",
        "passphrase",
        "bitwarden",
        "1password",
        "keepass",
        "lastpass",
        "zalo",
        "messenger",
        "facebook",
        "telegram",
        "whatsapp",
        "incognito",
        "an danh",
        "inprivate",
        "private browsing"
    ];

    public static string SanitizeWindowTitle(string? processName, string? windowTitle)
    {
        if (string.IsNullOrWhiteSpace(windowTitle))
        {
            return string.Empty;
        }

        string normalizedTitle = NormalizeForMatch(windowTitle);
        string normalizedProcess = NormalizeForMatch(processName ?? string.Empty);

        foreach (string keyword in SensitiveKeywordsNormalized)
        {
            if (normalizedTitle.Contains(keyword, StringComparison.Ordinal) ||
                normalizedProcess.Contains(keyword, StringComparison.Ordinal))
            {
                return RedactedPlaceholder;
            }
        }

        string trimmed = windowTitle.Trim();
        return trimmed.Length > 500 ? trimmed[..500] : trimmed;
    }

    public static string NormalizeForMatch(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        string formD = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);

        foreach (char c in formD)
        {
            UnicodeCategory uc = CharUnicodeInfo.GetUnicodeCategory(c);
            if (uc == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            char mapped = c switch
            {
                'đ' => 'd',
                'Đ' => 'd',
                _ => char.ToLowerInvariant(c)
            };

            sb.Append(mapped);
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
