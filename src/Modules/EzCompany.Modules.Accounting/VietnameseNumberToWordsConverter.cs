using System.Text;

namespace EzCompany.Modules.Accounting;

/// <summary>
/// Converts numeric VND amounts into standard Vietnamese accounting words.
/// </summary>
public static class VietnameseNumberToWordsConverter
{
    private static readonly string[] Digits =
        ["không", "một", "hai", "ba", "bốn", "năm", "sáu", "bảy", "tám", "chín"];

    private static readonly string[] Scales =
        ["", "nghìn", "triệu", "tỷ", "nghìn tỷ", "triệu tỷ"];

    public static string Convert(decimal amount)
    {
        if (amount == 0m)
        {
            return "Không đồng chẵn";
        }

        bool isNegative = amount < 0m;
        decimal abs = Math.Abs(amount);
        long whole = (long)Math.Round(abs, 0, MidpointRounding.AwayFromZero);

        if (whole == 0)
        {
            return "Không đồng chẵn";
        }

        var groups = new List<int>();
        long remaining = whole;
        while (remaining > 0)
        {
            groups.Add((int)(remaining % 1000));
            remaining /= 1000;
        }

        var parts = new List<string>();
        for (int i = groups.Count - 1; i >= 0; i--)
        {
            int g = groups[i];
            if (g == 0)
            {
                continue;
            }

            bool hasHigherGroup = i < groups.Count - 1;
            string groupText = ReadThreeDigits(g, hasHigherGroup);
            string scale = i < Scales.Length ? Scales[i] : string.Empty;
            parts.Add(string.IsNullOrEmpty(scale) ? groupText : $"{groupText} {scale}");
        }

        string joined = string.Join(" ", parts).Trim();
        if (isNegative)
        {
            joined = "âm " + joined;
        }

        if (joined.Length > 0)
        {
            joined = char.ToUpperInvariant(joined[0]) + joined[1..];
        }

        return $"{joined} đồng chẵn";
    }

    private static string ReadThreeDigits(int number, bool readLeadingZeroHundreds)
    {
        int hundreds = number / 100;
        int tens = (number % 100) / 10;
        int ones = number % 10;

        var sb = new StringBuilder();

        if (hundreds > 0 || readLeadingZeroHundreds)
        {
            sb.Append(Digits[hundreds]).Append(" trăm");
        }

        if (tens == 0)
        {
            if (ones > 0)
            {
                if (sb.Length > 0)
                {
                    sb.Append(" linh");
                }

                sb.Append(sb.Length > 0 ? " " + Digits[ones] : Digits[ones]);
            }
        }
        else if (tens == 1)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append("mười");
            if (ones == 5)
            {
                sb.Append(" lăm");
            }
            else if (ones > 0)
            {
                sb.Append(' ').Append(Digits[ones]);
            }
        }
        else
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(Digits[tens]).Append(" mươi");
            if (ones == 1)
            {
                sb.Append(" mốt");
            }
            else if (ones == 4)
            {
                sb.Append(" tư");
            }
            else if (ones == 5)
            {
                sb.Append(" lăm");
            }
            else if (ones > 0)
            {
                sb.Append(' ').Append(Digits[ones]);
            }
        }

        return sb.ToString().Trim();
    }
}
