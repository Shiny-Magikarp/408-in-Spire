using System.Diagnostics;
using System.Text.Json;
using System.Security.Cryptography;
using Spire408.AI;
using Spire408.Core;

string root=Path.GetFullPath(args.Length>0?args[0]:".");
string mode=args.Length>1?args[1]:"canonical";
WakuuJudge.ModelDirectory=args.Length>2?Path.GetFullPath(args[2]):Path.Combine(root,"models/wakuu");
string output=args.Length>3?args[3]:"reports/wakuu-marking-bench.json";
var timer=Stopwatch.StartNew();
var records=new List<object>();
if(mode=="canonical")
{
    var bank=EssayBank.Parse(File.ReadAllText(Path.Combine(root,"game/Spire408/Data/essays.json")));
    foreach(var q in bank.Questions.Where(q=>q.Experimental))
    {
        var grade=EssayEngine.Grade(q,q.Parts.Select(p=>p.Reference).ToList(),WakuuJudge.JudgeAnswer,WakuuJudge.Version);
        records.Add(new{id=q.Id,score=grade.Score,parts=grade.Grades});
    }
}
else
{
    using var cases=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,mode)));
    foreach(var row in cases.RootElement.EnumerateArray())
    {
        var p=WakuuJudge.JudgeAnswer(row.GetProperty("premise").GetString()!,row.GetProperty("hypothesis").GetString()!);
        int label=row.GetProperty("label").GetInt32();
        bool award=p.Entailment>=.85 && p.Contradiction<.1;
        records.Add(new{id=row.GetProperty("id").GetString(),label,award,correct=(label==2)==award,
            entailment=p.Entailment,contradiction=p.Contradiction,neutral=p.Neutral});
    }
}
using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(WakuuJudge.ModelDirectory,"model.json")));
string dataset=Path.Combine(root,mode=="canonical"?"game/Spire408/Data/essays.json":mode);
string datasetSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dataset))).ToLowerInvariant();
File.WriteAllText(Path.Combine(root,output),JsonSerializer.Serialize(new{model=WakuuJudge.Version,
    model_sha256=manifest.RootElement.GetProperty("sha256").GetString(),dataset_sha256=datasetSha256,
    seconds=timer.Elapsed.TotalSeconds,records},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"Benchmark complete: {records.Count} records, {timer.Elapsed.TotalSeconds:F1}s; {output}");
