using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using Spire408.AI;
using Spire408.Core;
using Spire408.UI;

namespace Spire408.Integration;

public static class EssayLibraryFlow
{
    public static void MarkWrong(EssayQuestion q, bool value)
    {
        if (value)
        {
            ModEntry.Profile.EssayWrong[q.Id] = q.Revision;
            ModEntry.Profile.EssayManualWrong.Add(q.Id);
        }
        else
        {
            ModEntry.Profile.EssayWrong.Remove(q.Id);
            ModEntry.Profile.EssayManualWrong.Remove(q.Id);
        }
        ModEntry.Store.Save("learning");
    }
    public static void Open(StudySettings settings, Action back, string selectedId = "", string search = "")
    {
        var host = NModalContainer.Instance!;
        var library = GD.Load<PackedScene>("res://Spire408/Scenes/EssayLibrary.tscn").Instantiate<EssayLibrary>();
        library.Configure(EssayEngine.Filter(ModEntry.Essays, settings, ModEntry.Profile), selectedId, search);
        library.Closed += () => { host.Clear(); back(); };
        library.PracticeRequested += q =>
        {
            string term = library.Search.Text; host.Clear();
            Practice(q, () => Open(settings, back, q.Id, term));
        };
        host.Add(library);
    }
    private static void Practice(EssayQuestion q, Action back)
    {
        var host = NModalContainer.Instance!;
        var view = GD.Load<PackedScene>("res://Spire408/Scenes/EssayPanel.tscn").Instantiate<EssayPanel>();
        string key = "library/" + q.Id + "/" + q.Revision;
        view.Configure(q, ModEntry.Profile.EssayDrafts.GetValueOrDefault(key) ?? [], null, "无", false, async (answers, token) =>
        {
            var result = await Task.Run(() => EssayEngine.Grade(q, answers, WakuuJudge.JudgeAnswer,
                q.Parts.Any(p => p.Kind is "concept" or "rubric") ? WakuuJudge.Version : "deterministic-rubric", token), token)
                .WaitAsync(TimeSpan.FromSeconds(20), token);
            token.ThrowIfCancellationRequested();
            ModEntry.Profile.EssayPractice[q.Id] = result;
            EssayEngine.UpdateNotebook(ModEntry.Profile, q, result);
            ModEntry.Store.Save("learning");
            return result;
        }, practice: true, initiallyWrong: ModEntry.Profile.EssayWrong.ContainsKey(q.Id),
            manualWrong: ModEntry.Profile.EssayManualWrong.Contains(q.Id));
        view.DraftChanged += answers => { ModEntry.Profile.EssayDrafts[key] = answers; ModEntry.Store.Save("learning"); };
        view.WrongChanged += value => MarkWrong(q, value);
        view.Closed += _ => { host.Clear(); back(); };
        host.Add(view);
    }
}
