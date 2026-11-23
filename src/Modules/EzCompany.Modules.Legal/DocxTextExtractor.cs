using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace EzCompany.Modules.Legal;

/// <summary>
/// Extracts paragraphs and tables from <c>.docx</c> contracts in exact document order using OpenXML.
/// </summary>
public sealed class DocxTextExtractor
{
    public IReadOnlyList<string> ExtractLines(string docxPath)
    {
        if (!File.Exists(docxPath))
        {
            throw new FileNotFoundException("Không tìm thấy tệp hợp đồng .docx.", docxPath);
        }

        using WordprocessingDocument doc = WordprocessingDocument.Open(docxPath, isEditable: false);
        Body? body = doc.MainDocumentPart?.Document?.Body;
        if (body is null)
        {
            return [];
        }

        var lines = new List<string>();
        foreach (OpenXmlElement element in body.ChildElements)
        {
            switch (element)
            {
                case Paragraph p:
                    string text = ExtractParagraphText(p);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        lines.Add(text);
                    }
                    break;

                case Table tbl:
                    int rowIndex = 0;
                    foreach (TableRow row in tbl.Elements<TableRow>())
                    {
                        rowIndex++;
                        string[] cells = row.Elements<TableCell>()
                            .Select(c => string.Join(" ", c.Elements<Paragraph>().Select(ExtractParagraphText)).Trim())
                            .ToArray();
                        string rowText = string.Join(" | ", cells);
                        if (!string.IsNullOrWhiteSpace(rowText.Replace("|", "").Trim()))
                        {
                            lines.Add($"[Bảng #{rowIndex}] {rowText}");
                        }
                    }
                    break;
            }
        }

        return lines;
    }

    public string ExtractJoinedText(string docxPath) =>
        string.Join(Environment.NewLine, ExtractLines(docxPath));

    private static string ExtractParagraphText(Paragraph p) =>
        string.Concat(p.Descendants<Text>().Select(t => t.Text)).Trim();
}
