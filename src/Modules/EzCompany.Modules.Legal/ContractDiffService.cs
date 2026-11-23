using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace EzCompany.Modules.Legal;

public enum ContractDiffKind
{
    Unchanged = 0,
    Added = 1,
    Deleted = 2,
    Modified = 3
}

public sealed record SideBySideDiffLine(
    int Index,
    int? OldLineNumber,
    string OldText,
    int? NewLineNumber,
    string NewText,
    ContractDiffKind Kind);

public sealed record InlineDiffLine(
    int Index,
    string Prefix,
    string Text,
    ContractDiffKind Kind);

public sealed record ContractDiffResult(
    IReadOnlyList<SideBySideDiffLine> SideBySideLines,
    IReadOnlyList<InlineDiffLine> InlineLines,
    IReadOnlyList<int> SideBySideChangeIndices,
    IReadOnlyList<int> InlineChangeIndices,
    int AddedCount,
    int DeletedCount,
    int ModifiedCount);

public sealed class ContractDiffService
{
    private readonly DocxTextExtractor _extractor;

    public ContractDiffService(DocxTextExtractor extractor)
    {
        _extractor = extractor;
    }

    public ContractDiffResult CompareDocxFiles(string originalPath, string revisedPath)
    {
        string oldText = _extractor.ExtractJoinedText(originalPath);
        string newText = _extractor.ExtractJoinedText(revisedPath);
        return CompareTexts(oldText, newText);
    }

    public ContractDiffResult CompareTexts(string oldText, string newText)
    {
        var differ = new Differ();
        SideBySideDiffModel sbsModel = new SideBySideDiffBuilder(differ).BuildDiffModel(oldText, newText);
        DiffPaneModel inlineModel = new InlineDiffBuilder(differ).BuildDiffModel(oldText, newText);

        var sideBySide = new List<SideBySideDiffLine>();
        var sbsChanges = new List<int>();
        int added = 0;
        int deleted = 0;
        int modified = 0;

        int maxCount = Math.Max(sbsModel.OldText.Lines.Count, sbsModel.NewText.Lines.Count);
        for (int i = 0; i < maxCount; i++)
        {
            DiffPiece? oldPiece = i < sbsModel.OldText.Lines.Count ? sbsModel.OldText.Lines[i] : null;
            DiffPiece? newPiece = i < sbsModel.NewText.Lines.Count ? sbsModel.NewText.Lines[i] : null;

            ContractDiffKind kind = ResolveSideBySideKind(oldPiece?.Type, newPiece?.Type);
            switch (kind)
            {
                case ContractDiffKind.Added:
                    added++;
                    sbsChanges.Add(i);
                    break;
                case ContractDiffKind.Deleted:
                    deleted++;
                    sbsChanges.Add(i);
                    break;
                case ContractDiffKind.Modified:
                    modified++;
                    sbsChanges.Add(i);
                    break;
            }

            sideBySide.Add(new SideBySideDiffLine(
                i,
                oldPiece?.Position,
                oldPiece?.Text ?? string.Empty,
                newPiece?.Position,
                newPiece?.Text ?? string.Empty,
                kind));
        }

        var inlineLines = new List<InlineDiffLine>();
        var inlineChanges = new List<int>();
        for (int i = 0; i < inlineModel.Lines.Count; i++)
        {
            DiffPiece piece = inlineModel.Lines[i];
            (string prefix, ContractDiffKind kind) = piece.Type switch
            {
                ChangeType.Inserted => ("+", ContractDiffKind.Added),
                ChangeType.Deleted => ("-", ContractDiffKind.Deleted),
                ChangeType.Modified => ("~", ContractDiffKind.Modified),
                _ => (" ", ContractDiffKind.Unchanged)
            };

            if (kind != ContractDiffKind.Unchanged)
            {
                inlineChanges.Add(i);
            }

            inlineLines.Add(new InlineDiffLine(i, prefix, piece.Text ?? string.Empty, kind));
        }

        return new ContractDiffResult(sideBySide, inlineLines, sbsChanges, inlineChanges, added, deleted, modified);
    }

    private static ContractDiffKind ResolveSideBySideKind(ChangeType? oldType, ChangeType? newType)
    {
        if (oldType == ChangeType.Modified || newType == ChangeType.Modified)
        {
            return ContractDiffKind.Modified;
        }

        if (oldType == ChangeType.Deleted && (newType is null or ChangeType.Imaginary))
        {
            return ContractDiffKind.Deleted;
        }

        if ((oldType is null or ChangeType.Imaginary) && newType == ChangeType.Inserted)
        {
            return ContractDiffKind.Added;
        }

        if (oldType == ChangeType.Deleted || newType == ChangeType.Inserted)
        {
            return ContractDiffKind.Modified;
        }

        return ContractDiffKind.Unchanged;
    }
}
