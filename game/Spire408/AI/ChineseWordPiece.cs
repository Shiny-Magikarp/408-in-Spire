using System.Globalization;
using System.Text;

namespace Spire408.AI;

// Mirrors BertTokenizer's basic lower-case / accent / CJK / WordPiece pipeline.
// Tokenization equivalence against Python is checked before packaging the model.
public sealed class ChineseWordPiece
{
    private readonly Dictionary<string, int> _vocabulary;
    private readonly int _unknown, _cls, _sep;
    public ChineseWordPiece(string vocabulary)
    {
        _vocabulary = System.IO.File.ReadAllLines(vocabulary).Select((word, id) => (word, id))
            .ToDictionary(x => x.word, x => x.id, StringComparer.Ordinal);
        _unknown = _vocabulary["[UNK]"]; _cls = _vocabulary["[CLS]"]; _sep = _vocabulary["[SEP]"];
    }
    public (long[] Ids, long[] Mask, long[] Types) Encode(string premise, string hypothesis, int maxTokens = 192, bool rejectTruncation = false)
    {
        var a = Tokenize(premise); var b = Tokenize(hypothesis);
        if (rejectTruncation && a.Count + b.Count + 3 > maxTokens)
            throw new ArgumentException("文字小问超过瓦库的阅读长度，请精简至约 120 字后重试。草稿仍保留。");
        // Same longest-first pair truncation used by BertTokenizer.
        while (a.Count + b.Count > maxTokens - 3)
        { if (a.Count > b.Count) a.RemoveAt(a.Count - 1); else b.RemoveAt(b.Count - 1); }
        var ids = new List<long> { _cls }; ids.AddRange(a); ids.Add(_sep);
        int firstLength = ids.Count; ids.AddRange(b); ids.Add(_sep);
        return (ids.ToArray(), Enumerable.Repeat(1L, ids.Count).ToArray(),
            Enumerable.Range(0, ids.Count).Select(i => i < firstLength ? 0L : 1L).ToArray());
    }
    private List<long> Tokenize(string text)
    {
        var basic = new StringBuilder();
        foreach (Rune rune in text.EnumerateRunes())
        {
            int value = rune.Value; var category = Rune.GetUnicodeCategory(rune);
            if (value is 0 or 0xfffd || category is UnicodeCategory.Control or UnicodeCategory.Format)
            { if (value is 9 or 10 or 13) basic.Append(' '); continue; }
            if (Rune.IsWhiteSpace(rune)) { basic.Append(' '); continue; }
            bool chinese = value is >= 0x4e00 and <= 0x9fff or >= 0x3400 and <= 0x4dbf
                or >= 0x20000 and <= 0x2ebef or >= 0xf900 and <= 0xfaff or >= 0x2f800 and <= 0x2fa1f;
            bool punctuation = value is >= 33 and <= 47 or >= 58 and <= 64 or >= 91 and <= 96 or >= 123 and <= 126
                || category is UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation
                    or UnicodeCategory.OpenPunctuation or UnicodeCategory.ClosePunctuation or UnicodeCategory.InitialQuotePunctuation
                    or UnicodeCategory.FinalQuotePunctuation or UnicodeCategory.OtherPunctuation;
            if (chinese || punctuation) basic.Append(' ');
            basic.Append(rune.ToString());
            if (chinese || punctuation) basic.Append(' ');
        }
        string lowered = basic.ToString().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var stripped = new StringBuilder();
        foreach (Rune rune in lowered.EnumerateRunes())
            if (Rune.GetUnicodeCategory(rune) != UnicodeCategory.NonSpacingMark) stripped.Append(rune.ToString());
        var result = new List<long>();
        foreach (string token in stripped.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length > 100) { result.Add(_unknown); continue; }
            int start = 0; var pieces = new List<long>(); bool failed = false;
            while (start < token.Length)
            {
                int end = token.Length; int found = -1;
                while (start < end)
                {
                    string piece = (start == 0 ? "" : "##") + token[start..end];
                    if (_vocabulary.TryGetValue(piece, out found)) break;
                    found = -1; end--;
                }
                if (found < 0) { failed = true; break; }
                pieces.Add(found); start = end;
            }
            if (failed) result.Add(_unknown); else result.AddRange(pieces);
        }
        return result;
    }
}
