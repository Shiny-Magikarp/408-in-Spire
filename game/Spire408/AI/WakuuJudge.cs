using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using Spire408.Core;

namespace Spire408.AI;

public static class WakuuJudge
{
    private static readonly object Sync = new();
    private static InferenceSession? _session;
    private static ChineseWordPiece? _tokenizer;
    private static string _version = "";
    private static string AssemblyDirectory => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) is { Length: > 0 } directory ? directory : AppContext.BaseDirectory;
    public static string ModelDirectory { get; set; } = Path.Combine(AssemblyDirectory, "Models", "wakuu");
    public static bool FilesPresent => System.IO.File.Exists(Path.Combine(ModelDirectory, "wakuu.onnx"))
        && System.IO.File.Exists(Path.Combine(ModelDirectory, "model.json")) && System.IO.File.Exists(Path.Combine(ModelDirectory, "vocab.txt"));
    public static string Version { get { EnsureReady(); return _version; } }
    public static Task WarmupAsync() => Task.Run(() => { EnsureReady(); Judge("所有边的权值都非负。", "图中不存在负权边。"); });
    private static void EnsureReady()
    {
        lock (Sync)
        {
            if (_session != null) return;
            Console.WriteLine("[Spire408] Loading offline Wakuu model from " + ModelDirectory);
            if (!FilesPresent) throw new FileNotFoundException("未安装瓦库离线模型，可运行 PrepareWakuu.ps1 后重新打包。");
            using var manifest = JsonDocument.Parse(System.IO.File.ReadAllText(Path.Combine(ModelDirectory, "model.json")));
            string path = Path.Combine(ModelDirectory, "wakuu.onnx");
            string hash = Convert.ToHexString(SHA256.HashData(System.IO.File.ReadAllBytes(path)));
            if (!hash.Equals(manifest.RootElement.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("瓦库模型校验失败，请重新安装完整模型文件。");
            string native = Path.Combine(AssemblyDirectory, "onnxruntime.dll");
            if (System.IO.File.Exists(native)) NativeLibrary.Load(native);
            using var options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4), InterOpNumThreads = 1,
                LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR
            };
            _tokenizer = new ChineseWordPiece(Path.Combine(ModelDirectory, "vocab.txt"));
            _session = new InferenceSession(path, options);
            Console.WriteLine("[Spire408] Offline Wakuu session ready.");
            _version = manifest.RootElement.GetProperty("model_version").GetString()!;
        }
    }
    public static SemanticEvidence Judge(string answer, string reference)
    {
        if (SemanticGuards.Contradicts(answer, reference)) return new(1,0,0);
        EnsureReady();
        var (ids, mask, types) = _tokenizer!.Encode(answer, reference, rejectTruncation: true);
        long[] shape = [1, ids.Length];
        using var a = OrtValue.CreateTensorValueFromMemory(ids, shape);
        using var b = OrtValue.CreateTensorValueFromMemory(mask, shape);
        using var c = OrtValue.CreateTensorValueFromMemory(types, shape);
        using var options = new RunOptions();
        using var outputs = _session!.Run(options, ["input_ids", "attention_mask", "token_type_ids"], [a, b, c], ["logits"]);
        var logits = outputs[0].GetTensorDataAsSpan<float>();
        if (logits.Length != 3) throw new InvalidDataException("瓦库评分模型的输出维度不匹配");
        double max = Math.Max(logits[0], Math.Max(logits[1], logits[2]));
        double x = Math.Exp(logits[0] - max), y = Math.Exp(logits[1] - max), z = Math.Exp(logits[2] - max), sum = x + y + z;
        return new SemanticEvidence(x / sum, y / sum, z / sum);
    }
    // Every passage is retained in the retrieval pool. The model reads the
    // closest three short passages, avoiding silent long-answer truncation.
    public static SemanticEvidence JudgeAnswer(string answer, string reference)
    {
        if (answer.Length + reference.Length <= 175) return Judge(answer, reference);
        string plain = Regex.Replace(answer, @"\*\*|`", "");
        plain = Regex.Replace(plain, @"^[（(]\d+[)）]\s*", "");
        plain = Regex.Replace(plain, @"[ \t]+", " ");
        var passages = new List<string>();
        foreach (string sentence in Regex.Split(plain, @"(?<=[。；！？\n])"))
        {
            string remaining = sentence.Trim();
            while (remaining.Length > 80)
            {
                int cut = new[] {'，',',','；','：',' '}.Select(mark => remaining.LastIndexOf(mark,79,50)).Max();
                if (cut < 30) cut = 79;
                passages.Add(remaining[..(cut + 1)]); remaining = remaining[(cut + 1)..].Trim();
            }
            if (remaining.Length > 0) passages.Add(remaining);
        }
        static HashSet<string> Bigrams(string s)
        {
            s = Regex.Replace(s.ToLowerInvariant(), @"\s+|\p{P}", "");
            return Enumerable.Range(0, Math.Max(0,s.Length - 1)).Select(i => s.Substring(i,2)).ToHashSet();
        }
        var terms = Bigrams(reference);
        var ranked = passages.Select((text,index) => new { text,index,overlap = Bigrams(text).Count(terms.Contains) })
            .OrderByDescending(x => x.overlap).ThenBy(x => x.index).Take(3).ToList();
        if (ranked.Count == 0 || ranked[0].overlap == 0) return new(0,1,0);
        // A different subquestion can legitimately state the opposite rule for
        // a different condition (e.g. root vs non-root). Compare only passages
        // with nearly the same retrieval relevance as the closest passage.
        double minimumOverlap = ranked[0].overlap * .9;
        ranked = ranked.Where(p => p.overlap >= minimumOverlap).ToList();
        var evidence = ranked.Select(p => Judge(p.text, reference)).ToList();
        // An answer that states a conclusion and its negation remains disputed.
        if (evidence.Any(e => e.Contradiction >= .6) && evidence.Any(e => e.Entailment >= .85))
            return new(.2,.4,.4);
        return evidence.OrderByDescending(e => e.Entailment).First();
    }
}
