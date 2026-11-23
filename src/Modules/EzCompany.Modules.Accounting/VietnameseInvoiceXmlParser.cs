using System.Globalization;
using System.IO;
using System.Xml.Linq;

namespace EzCompany.Modules.Accounting;

public sealed record ParsedInvoiceLine(
    string ItemName,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineAmount);

public sealed record ParsedInvoiceXml(
    string InvoiceSeries,
    string InvoiceNumber,
    DateTimeOffset IssueDate,
    string SellerTaxCode,
    string SellerName,
    string BuyerTaxCodeOrCccd,
    string BuyerName,
    decimal SubTotal,
    decimal VatAmount,
    decimal TotalAmount,
    string AmountInWords,
    bool HasMathMismatch,
    decimal MathDifference,
    IReadOnlyList<ParsedInvoiceLine> Lines,
    string ValidationSummary);

/// <summary>
/// Parses Vietnamese e-invoice XML conforming to Decree 123/2020/ND-CP and Decree 70/2025/ND-CP.
/// </summary>
public sealed class VietnameseInvoiceXmlParser
{
    public const decimal MathToleranceVnd = 10m;

    public ParsedInvoiceXml ParseFile(string xmlFilePath)
    {
        using FileStream fs = File.OpenRead(xmlFilePath);
        XDocument doc = XDocument.Load(fs);
        return ParseDocument(doc);
    }

    public ParsedInvoiceXml ParseXmlString(string xmlContent)
    {
        XDocument doc = XDocument.Parse(xmlContent);
        return ParseDocument(doc);
    }

    public ParsedInvoiceXml ParseDocument(XDocument doc)
    {
        XElement root = doc.Root ?? throw new InvalidDataException("Tệp XML không có phần tử gốc.");
        XElement hdon = string.Equals(root.Name.LocalName, "HDon", StringComparison.OrdinalIgnoreCase)
            ? root
            : FindFirstDescendant(root, "HDon")
              ?? throw new InvalidDataException("Không tìm thấy thẻ hóa đơn <HDon> trong tệp XML.");

        XElement? dlHDon = FindFirstDescendant(hdon, "DLHDon") ?? hdon;
        XElement? ttChung = FindFirstDescendant(dlHDon, "TTChung");
        XElement? ndHDon = FindFirstDescendant(dlHDon, "NDHDon");
        XElement? nBan = FindFirstDescendant(ndHDon, "NBan");
        XElement? nMua = FindFirstDescendant(ndHDon, "NMua");
        XElement? dsHhdVu = FindFirstDescendant(ndHDon, "DSHHDVu");
        XElement? tToan = FindFirstDescendant(ndHDon, "TToan");

        string khms = ReadChildValue(ttChung, "KHMSHDon");
        string khh = ReadChildValue(ttChung, "KHHDon");
        string series = string.IsNullOrEmpty(khms) ? khh : $"{khms}{khh}";
        string number = ReadChildValue(ttChung, "SHDon");
        string issueDateStr = ReadChildValue(ttChung, "NLap");
        DateTimeOffset issueDate = ParseDate(issueDateStr);

        string sellerMst = ReadChildValue(nBan, "MST");
        string sellerName = ReadChildValue(nBan, "Ten");

        string buyerId = ReadFirstNonEmpty(nMua, "MST", "CCCDan", "SoCCCD", "CCCD");
        string buyerName = ReadFirstNonEmpty(nMua, "Ten", "HVTNMHang");

        var lines = new List<ParsedInvoiceLine>();
        if (dsHhdVu is not null)
        {
            foreach (XElement itemEl in dsHhdVu.Elements().Where(e => string.Equals(e.Name.LocalName, "HHDVu", StringComparison.OrdinalIgnoreCase)))
            {
                string itemName = ReadChildValue(itemEl, "THHDVu");
                decimal qty = ReadDecimal(itemEl, "SLuong");
                decimal price = ReadDecimal(itemEl, "DGia");
                decimal lineTotal = ReadDecimal(itemEl, "ThTien");
                lines.Add(new ParsedInvoiceLine(itemName, qty, price, lineTotal));
            }
        }

        decimal subTotal = ReadDecimal(tToan, "TgTCThue");
        decimal vatAmount = ReadDecimal(tToan, "TgTThue");
        decimal totalAmount = ReadDecimal(tToan, "TgTTTBSo");
        string xmlWords = ReadChildValue(tToan, "TgTTTBChu");

        if (subTotal == 0m && lines.Count > 0)
        {
            subTotal = lines.Sum(l => l.LineAmount);
        }

        decimal lineSum = lines.Sum(l => l.LineAmount);
        decimal lineDiff = lines.Count > 0 && subTotal > 0m ? Math.Abs(lineSum - subTotal) : 0m;
        decimal totalDiff = Math.Abs((subTotal + vatAmount) - totalAmount);
        decimal maxDiff = Math.Max(lineDiff, totalDiff);
        bool hasMismatch = maxDiff > MathToleranceVnd;

        var notes = new List<string>();
        if (totalDiff > MathToleranceVnd)
        {
            notes.Add($"Lệch tổng cộng (Tiền hàng + VAT vs Thanh toán): {totalDiff:N0} đ");
        }

        if (lineDiff > MathToleranceVnd)
        {
            notes.Add($"Lệch cộng dòng hàng hóa ({lineSum:N0} vs {subTotal:N0}): {lineDiff:N0} đ");
        }

        if (string.IsNullOrWhiteSpace(sellerMst) || string.IsNullOrWhiteSpace(number))
        {
            notes.Add("Thiếu MST người bán hoặc Số hóa đơn.");
        }

        string amountWords = string.IsNullOrWhiteSpace(xmlWords)
            ? VietnameseNumberToWordsConverter.Convert(totalAmount)
            : xmlWords;

        return new ParsedInvoiceXml(
            series,
            number,
            issueDate,
            sellerMst,
            sellerName,
            buyerId,
            buyerName,
            subTotal,
            vatAmount,
            totalAmount,
            amountWords,
            hasMismatch,
            maxDiff,
            lines,
            notes.Count == 0 ? "Hợp lệ" : string.Join("; ", notes));
    }

    private static XElement? FindFirstDescendant(XElement? parent, string localName)
    {
        if (parent is null) return null;
        return parent.Descendants()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase));
    }

    private static string ReadChildValue(XElement? parent, string localName)
    {
        if (parent is null) return string.Empty;
        XElement? el = parent.Elements()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase));
        return el?.Value.Trim() ?? string.Empty;
    }

    private static string ReadFirstNonEmpty(XElement? parent, params string[] localNames)
    {
        foreach (string name in localNames)
        {
            string val = ReadChildValue(parent, name);
            if (!string.IsNullOrWhiteSpace(val))
            {
                return val;
            }
        }

        return string.Empty;
    }

    private static decimal ReadDecimal(XElement? parent, string localName)
    {
        string raw = ReadChildValue(parent, localName);
        if (string.IsNullOrWhiteSpace(raw)) return 0m;
        return decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal val) ? val : 0m;
    }

    private static DateTimeOffset ParseDate(string raw)
    {
        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTimeOffset dto))
        {
            return dto;
        }

        if (DateTime.TryParseExact(raw, ["yyyy-MM-dd", "dd/MM/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime dt))
        {
            return new DateTimeOffset(dt);
        }

        return DateTimeOffset.UtcNow;
    }
}
