using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using Spire408.Core;
using Spire408.Integration;
using Spire408.UI;
using Spire408.Models;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace Spire408.Tests;

// Only compiled with EnableSmoke=true. Requires a separate save directory and Steam disabled.
internal static class GameSmoke
{
    private static SceneTree Tree => (SceneTree)Engine.GetMainLoop();
    private static int _checks;
    public static void Install()
    {
        if (!CommandLineHelper.HasArg("spire408-smoke")) return;
        if (!OS.GetUserDataDir().Replace('\\','/').Contains("/408InSpire/.tools/test-user/")
            || CommandLineHelper.GetValue("force-steam") != "off")
            throw new InvalidOperationException("Smoke test requires isolated APPDATA and --force-steam=off");
        Callable.From(Run).CallDeferred();
    }
    private static void Check(bool ok, string name)
    {
        if (!ok) throw new InvalidOperationException(name);
        _checks++; GD.Print("[Spire408 smoke] PASS " + name);
    }
    private static async Task Wait(Func<bool> condition, string description, int frames = 1800)
    {
        while (!condition() && frames-- > 0) await Tree.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
        if (!condition()) throw new TimeoutException(description);
    }
    private static async Task Screenshot(string name)
    {
        if (DisplayServer.GetName() == "headless") return;
        await Tree.ToSignal(Tree.CreateTimer(0.25), SceneTreeTimer.SignalName.Timeout);
        string path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(OS.GetExecutablePath())!, "408InSpire", "reports", "screenshots", "game-" + name + ".png");
        Tree.Root.GetTexture().GetImage().SavePng(path);
    }
    private static async Task BadgeFrames(StudyCombatHud hud, int start, int count)
    {
        if (DisplayServer.GetName() == "headless") return;
        string folder = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(OS.GetExecutablePath())!, "408InSpire", "reports", "screenshots");
        for (int i = 0; i < count; i++)
        {
            await Tree.ToSignal(Tree.CreateTimer(0.12), SceneTreeTimer.SignalName.Timeout);
            var region = new Rect2I((int)hud.GlobalPosition.X - 12, (int)hud.GlobalPosition.Y - 8, 184, 206);
            Tree.Root.GetTexture().GetImage().GetRegion(region).SavePng(System.IO.Path.Combine(folder, $"badge-060-{start+i:00}.png"));
        }
    }
    private static async void Run()
    {
        try
        {
            await NGame.Instance!.GameStartupComplete;
            SaveManager.Instance.SetFtuesEnabled(false);
            await NGame.Instance.Transition.FadeIn(0.1f, "res://materials/transitions/fade_transition_mat.tres", CancellationToken.None);
            ModEntry.Store.Modify<LearningProfile>("learning", p =>
            {
                p.Wrong.Clear(); p.Attempts.Clear(); p.CorrectCount = 0; p.AnsweredCount = 0;
                p.EssayWrong.Clear(); p.EssayAttempts.Clear(); p.EssayDrafts.Clear(); p.EssayPractice.Clear(); p.EssayManualWrong.Clear();
            });
            ModEntry.Store.Save("learning");
            NModalContainer.Instance?.Clear();
            await Tree.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
            var characterScreen = NGame.Instance.MainMenu!.SubmenuStack.GetSubmenuType<NCharacterSelectScreen>();
            characterScreen.InitializeSingleplayer();
            AccessTools.Method(typeof(NCharacterSelectScreen), "OnEmbarkPressed").Invoke(characterScreen, [null]);
            Check(NModalContainer.Instance!.OpenModal is StudySetup, "original embark button opens study plan");
            ((StudySetup)NModalContainer.Instance.OpenModal!).CancelButton.Activate();
            Check(NModalContainer.Instance.OpenModal == null && ModEntry.PendingRun == null, "cancel returns without creating a run");
            characterScreen.OnSubmenuClosed();
            bool configured = false;
            ModEntry.ShowSetup(() => configured = true);
            var setup = (StudySetup)NModalContainer.Instance!.OpenModal!;
            Check(setup.CountLabel.Text.Contains("1,676") && setup.CountLabel.Text.Contains("可核分 204 / 实验 124"), "scene bindings and complete grading coverage counts");
            Check(setup.StartButton is NPopupYesNoButton && setup.DataStructures is NTickbox
                && setup.SourceFilter is MegaCrit.Sts2.Core.Nodes.Screens.Settings.NSettingsDropdown
                && setup.LibraryButton is MegaCrit.Sts2.Core.Nodes.Screens.Settings.NSettingsButton,
                "study controls use original game UI components");
            await Tree.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
            Check(setup.CancelButton.GlobalPosition.X < setup.StartButton.GlobalPosition.X - 800
                && Math.Abs(setup.CancelButton.GlobalPosition.Y - setup.StartButton.GlobalPosition.Y) < 20,
                "back and start occupy opposite bottom corners of the panel");
            await Screenshot("setup");
            Vector2 sourcePosition = setup.SourceFilter.GlobalPosition;
            setup.SourceFilter.ForceClick();
            await Tree.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
            Check(setup.SourceFilter.GetNode<Control>("%DropdownContainer").Visible, "native source dropdown opens");
            await Screenshot("sources");
            var sourceRows = setup.SourceFilter.GetNode<Control>("%DropdownContainer/VBoxContainer");
            ((NDropdownItem)sourceRows.GetChild(1)).ForceClick();
            Check(setup.SourceFilter.Selected == 1 && !setup.SourceFilter.GetNode<Control>("%DropdownContainer").Visible,
                "native dropdown selection filters questions and closes");
            setup.SourceFilter.ForceClick();
            ((NDropdownItem)sourceRows.GetChild(0)).ForceClick();
            await Tree.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
            Check(setup.SourceFilter.GlobalPosition.IsEqualApprox(sourcePosition), "dropdown leaves form layout unchanged");
            setup.ChapterFilter.ForceClick();
            await Tree.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
            Check(setup.ChapterFilter.GetNode<Control>("%DropdownContainer/Scrollbar").Visible,
                "chapter dropdown retains original scrolling for long lists");
            setup.ChapterFilter.ForceClick();
            setup.WrongToggle.ButtonPressed = true;
            Check(setup.StartButton.Disabled, "wrong-only empty pool disables embark");
            setup.WrongToggle.ButtonPressed = false;
            setup.LibraryButton.Activate();
            await Wait(() => NModalContainer.Instance!.OpenModal is EssayLibrary, "essay library opens from pre-run plan");
            var library = (EssayLibrary)NModalContainer.Instance!.OpenModal!;
            Check(library.MatchingCount == 204, "pre-run library exposes all imported essays plus original exercises");
            await Tree.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
            Check(library.GetGlobalRect().Encloses(library.QuestionSelect.GetGlobalRect()) && library.QuestionSelect.Size.X > 600,
                "native question selector remains inside the editor-authored library panel");
            library.QuestionSelect.ForceClick();
            Check(library.QuestionSelect.GetNode<Control>("%DropdownContainer").Visible,
                "question library native dropdown opens its complete list");
            library.QuestionSelect.ForceClick();
            await Screenshot("essay-library");
            library.Search.Text = "cs408-mock02-43";
            library.Search.EmitSignal(LineEdit.SignalName.TextChanged, library.Search.Text);
            Check(library.MatchingCount == 1 && library.Selected!.OriginalPoints == 12,
                "library search selects an imported question with preserved original points");
            library.Open.Activate();
            await Wait(() => NModalContainer.Instance!.OpenModal is EssayPanel, "imported practice opens");
            var practiceView = (EssayPanel)NModalContainer.Instance!.OpenModal!;
            var practiceQuestion = ModEntry.Essays.Questions.Single(q => q.Id == "cs408-mock02-43");
            for (int page = 0; page < 2; page++)
            {
                var fields = new[] { practiceView.Answer1, practiceView.Answer2, practiceView.Answer3, practiceView.Answer4 };
                for (int slot = 0; slot < 4; slot++) fields[slot].Text = practiceQuestion.Parts[page*4+slot].Reference;
                if (page == 0) practiceView.Next.Activate();
            }
            practiceView.Previous.Activate();
            Check(practiceView.Answer1.Text == "4" && practiceView.Answers.Count == 8 && practiceView.Answers[7] == "102000ns",
                "editor-bound answer pages retain all eight calculation fields and drafts");
            practiceView.WrongToggle.ButtonPressed = true;
            practiceView.Submit.Activate();
            await Wait(() => !practiceView.Busy && practiceView.Submit.Text == "返回题库", "numeric library practice grades");
            Check(ModEntry.Profile.EssayPractice[practiceQuestion.Id].Score == 100 && practiceView.Feedback.GetParsedText().Contains("完整参考答案")
                && ModEntry.PendingRun == null, "library calculation gives scoped feedback without creating a run or game reward");
            Check(practiceView.WrongToggle.ButtonPressed && ModEntry.Profile.EssayManualWrong.Contains(practiceQuestion.Id),
                "correct practice calculations preserve the learner's manual full-question mistake flag");
            practiceView.WrongToggle.ButtonPressed = false;
            await Screenshot("essay-imported-practice");
            practiceView.Submit.Activate();
            await Wait(() => NModalContainer.Instance!.OpenModal is EssayLibrary, "practice returns to library");
            library = (EssayLibrary)NModalContainer.Instance!.OpenModal!;
            Check(library.Search.Text == "cs408-mock02-43", "library preserves search after practice");
            library.Search.Text = "cs408-mock01-41";
            library.Search.EmitSignal(LineEdit.SignalName.TextChanged, library.Search.Text);
            library.Open.Activate();
            practiceView = (EssayPanel)NModalContainer.Instance!.OpenModal!;
            practiceView.Answer1.Text = "int check(Node *p) { return 0; }";
            practiceView.WrongToggle.ButtonPressed = true;
            Check(ModEntry.Profile.EssayWrong.ContainsKey("cs408-mock01-41"), "review-only algorithm can be marked in the persistent wrong notebook");
            practiceView.Submit.Activate();
            await Wait(() => !practiceView.Busy && practiceView.Submit.Text == "返回题库", "algorithm reference opens");
            Check(practiceView.Feedback.GetParsedText().Contains("实验性") && practiceView.Feedback.GetParsedText().Contains("瓦库评分：")
                && practiceView.Feedback.GetParsedText().Contains("未验证代码执行"),
                "new semantic code practice shows its experimental score, process-review boundary and complete reference");
            practiceView.WrongToggle.ButtonPressed = false;
            practiceView.Submit.Activate();
            ((EssayLibrary)NModalContainer.Instance!.OpenModal!).Back.Activate();
            setup = (StudySetup)NModalContainer.Instance!.OpenModal!;
            Check(ModEntry.Profile.EssayPractice.Count == 2 && !ModEntry.Profile.EssayWrong.ContainsKey("cs408-mock01-41"),
                "library results persist and manual mastery removes a self-check mistake");
            setup.StartButton.Activate();
            Check(configured && ModEntry.PendingRun is { Enabled: true }, "pre-run configuration accepted");
            var diagramQuestion = ModEntry.Bank.Questions.First(q => q.Stem.Contains('$') && q.Stem.Contains("!["));
            ModEntry.PendingRun!.Deck.Remove(diagramQuestion.Id);
            ModEntry.PendingRun.Deck.Insert(0, diagramQuestion.Id);
            var correctedQuestion = ModEntry.Questions["408-53059b12626384217e962029"];
            Check(correctedQuestion.Answer == "D" && !correctedQuestion.Explanation.Contains("缺页"), "pinned erratum loads in the actual game");
            ModEntry.PendingRun.Deck.Remove(correctedQuestion.Id);
            ModEntry.PendingRun.Deck.Insert(1, correctedQuestion.Id);
            var state = await NGame.Instance.StartNewSingleplayerRun(ModelDb.Character<Ironclad>(), true,
                ActModel.GetDefaultList(), Array.Empty<ModifierModel>(), "SPIRE408SMOKE", GameMode.Standard);
            var player = state.Players[0];
            Check(player.Gold == 0 && ModEntry.Active(state) != null, "new run starts with zero gold");
            var book = player.Relics.OfType<StudyBook>().Single();
            Check(book.Title.GetFormattedText() == "408计算机专业课"
                && book.DynamicDescription.GetFormattedText().Contains("伤害、格挡翻倍")
                && book.DynamicDescription.GetFormattedText().Contains("30/60/90")
                && book.DynamicDescription.GetFormattedText().Contains("75–89")
                && book.Flavor.GetFormattedText() == "一本平平无奇的专业课教材，不知道为什么透露着地狱的气息",
                "textbook explains study rewards and preserves original text in native flavor field");
            Check(book.Icon.GetWidth() == 85 && book.BigIcon.GetWidth() == 256 && book.IconOutline.GetWidth() == 85,
                "inventory and detail relic textures load from mod pack");
            var bookHolder = NRun.Instance!.GlobalUi.RelicInventory.FindChildren("*", recursive: true, owned: false)
                .OfType<NRelicInventoryHolder>().Single(h => h.Relic.Model is StudyBook);
            bookHolder.GrabFocus();
            await Screenshot("relic");
            bookHolder.ReleaseFocus();
            var inspectBook = NGame.Instance.GetInspectRelicScreen();
            inspectBook.Open(player.Relics, book);
            await Screenshot("relic-details");
            inspectBook.Close();
            await Wait(() => !inspectBook.Visible, "native relic inspection closes before combat");
            await PlayerCmd.GainGold(999, player);
            player.Gold += 999;
            Check(player.Gold == 0, "command and direct gold increases blocked");
            await RelicCmd.Obtain<OldCoin>(player);
            Check(player.Gold == 300, "Old Coin keeps its native 300-gold pickup effect");
            await PlayerCmd.GainGold(999, player);
            player.Gold += 999;
            Check(player.Gold == 300, "relic gold permit does not leak into later unrelated gold changes");
            player.Gold = 0;
            var room = (CombatRoom)await RunManager.Instance.EnterRoomDebug(RoomType.Monster,
                model: ModelDb.Encounter<SlimesWeak>().ToMutable(), showTransition: false);
            await Wait(() => player.PlayerCombatState?.Hand.Cards.Count > 0 && CombatManager.Instance.IsInProgress, "combat ready");
            NModalContainer.Instance?.Clear();
            var hud = NCombatRoom.Instance!.Ui.GetNode<StudyCombatHud>("StudyCombatHud");
            await Tree.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
            Check(hud.Visible && hud.RemainingLabel.Text == "3" && hud.GoldLabel.Text == "答题金币 0/30"
                && hud.Emblem.Texture.GetWidth() == 256, "combat scene displays generated knowledge resource and initial quota");
            await Wait(() => !hud.ActivationButton.Disabled, "badge enabled in player play phase");
            Check(!ModEntry.Active(state)!.Battle.Armed && hud.ActivationButton.Animations.CurrentAnimation == "idle",
                "badge begins idle with editor-authored idle animation");
            await BadgeFrames(hud,0,5);
            hud.ActivationButton.Activate();
            Check(ModEntry.Active(state)!.Battle.Armed && hud.ActivationButton.Animations.CurrentAnimation == "active",
                "click activates next chosen card and starts active pulse");
            await Screenshot("combat-resource-active");
            await BadgeFrames(hud,5,8);
            hud.ActivationButton.EmitSignal(Control.SignalName.MouseEntered);
            await BadgeFrames(hud,13,3);
            Check(hud.ActivationButton.HoverMotion.Scale.X > 1.04f, "hover enlarges badge without disturbing state animation");
            hud.ActivationButton.EmitSignal(Control.SignalName.MouseExited);
            await Tree.ToSignal(Tree.CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);
            Check(hud.ActivationButton.HoverMotion.Scale.IsEqualApprox(Vector2.One), "hover exit smoothly restores the badge's outer scale");
            hud.ActivationButton.Activate();
            Check(!ModEntry.Active(state)!.Battle.Armed && ModEntry.Active(state)!.Battle.Earned == 0
                && hud.ActivationButton.Animations.CurrentAnimation == "idle", "second click cancels activation without spending gains");
            await BadgeFrames(hud,16,5);
            var enemyTarget = room.CombatState.Enemies.First();
            await CreatureCmd.GainMaxHp(enemyTarget, 200);
            player.PlayerCombatState!.Energy = 20;
            var plainCard = room.CombatState.CreateCard<StrikeIronclad>(player);
            await CardPileCmd.AddGeneratedCardToCombat(plainCard, PileType.Hand, player);
            int plainHp = enemyTarget.CurrentHp;
            Check(plainCard.TryManualPlay(enemyTarget), "unarmed manual play queues normally");
            await Wait(() => plainCard.Pile?.Type == PileType.Discard, "unarmed manual card finishes without a modal");
            Check(plainHp - enemyTarget.CurrentHp == 6 && ModEntry.Profile.AnsweredCount == 0
                && ModEntry.Active(state)!.Cursor == 0 && NModalContainer.Instance!.OpenModal == null,
                "idle manual card retains original damage without consuming questions");
            await Wait(() => !hud.ActivationButton.Disabled, "badge interactive after plain play");
            hud.ActivationButton.Activate();
            var autoCard = room.CombatState.CreateCard<StrikeIronclad>(player);
            await CardPileCmd.AddGeneratedCardToCombat(autoCard, PileType.Hand, player);
            int autoHp = enemyTarget.CurrentHp;
            await CardCmd.AutoPlay(new ThrowingPlayerChoiceContext(), autoCard, enemyTarget);
            Check(autoHp - enemyTarget.CurrentHp == 6 && ModEntry.Profile.AnsweredCount == 0
                && NModalContainer.Instance!.OpenModal == null && ModEntry.Active(state)!.Battle.Armed,
                "automatic plays do normal damage and preserve activation for the chosen manual card");
            hud.ActivationButton.Activate();
            await Screenshot("combat-resource");
            // Exercise the actual manual card queue: wrong Strike, boosted Strike, Defend and two-hit attack.
            StrikeIronclad? boostedStrike = null;
            for (int i = 0; i < 4; i++)
            {
                CardModel card = i switch { 2 => room.CombatState.CreateCard<DefendIronclad>(player),
                    3 => room.CombatState.CreateCard<TwinStrike>(player), _ => room.CombatState.CreateCard<StrikeIronclad>(player) };
                if (i == 1) boostedStrike = (StrikeIronclad)card;
                await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, player);
                int hpBefore = enemyTarget.CurrentHp, blockBefore = player.Creature.Block;
                var target = i == 2 ? null : enemyTarget;
                if (i == 1)
                {
                    var olderCard = room.CombatState.CreateCard<StrikeIronclad>(player);
                    await CardPileCmd.AddGeneratedCardToCombat(olderCard, PileType.Hand, player);
                    RunManager.Instance.ActionExecutor.Pause();
                    try
                    {
                        Check(olderCard.TryManualPlay(enemyTarget), "older unarmed card enters queue before activation");
                        hud.ActivationButton.Activate();
                        Check(card.TryManualPlay(target), "chosen card enters queue after activation");
                    }
                    finally { RunManager.Instance.ActionExecutor.Unpause(); }
                }
                else
                {
                    await Wait(() => !hud.ActivationButton.Disabled, "badge available before selected play");
                    hud.ActivationButton.Activate();
                    Check(card.TryManualPlay(target), "manual card queued through original game action " + i);
                }
                await Wait(() => NModalContainer.Instance?.OpenModal is QuizPanel, "quiz modal");
                Check(!ModEntry.Active(state)!.Battle.Armed, "selected play consumes activation exactly once " + i);
                if (i == 1) Check(hpBefore - enemyTarget.CurrentHp == 6 && ModEntry.Profile.AnsweredCount == 1,
                    "previously queued card finishes unboosted before chosen card opens its quiz");
                var view = (QuizPanel)NModalContainer.Instance!.OpenModal!;
                var run = ModEntry.Active(state)!;
                var q = ModEntry.Questions[run.Battle.PendingId];
                if (i == 0) await Screenshot("quiz");
                string choice = i == 0 ? (q.Answer == "A" ? "B" : "A") : q.Answer;
                var button = choice switch { "A" => view.PickA, "B" => view.PickB, "C" => view.PickC, _ => view.PickD };
                button.Activate();
                view.Submit.Activate();
                Check(view.Explanation.Visible, "grading shows explanation " + i);
                if (i == 1)
                {
                    await Screenshot("corrected-instruction-question");
                    string explanationText = view.Explanation.GetParsedText();
                    Check(q.Id == correctedQuestion.Id && view.Feedback.Text.Contains("回答正确")
                        && explanationText.Contains("操作码前缀") && !explanationText.Contains("缺页"),
                        $"reported question accepts D and renders its own corrected explanation (id={q.Id}; feedback={view.Feedback.Text}; explanation={explanationText})");
                }
                if (i == 0)
                {
                    await Tree.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
                    await Tree.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
                    await Screenshot("explanation");
                    Check(view.Scroll.GetGlobalRect().Intersects(view.Feedback.GetGlobalRect()),
                        "grading scrolls visible feedback into view");
                }
                view.Submit.Activate();
                await Wait(() => NModalContainer.Instance!.OpenModal == null && !CombatManager.Instance.IsPaused
                    && card.Pile?.Type == PileType.Discard, "manual card finishes after answer");
                int expectedDamage = i switch { 0 => 6, 1 => 18, 3 => 20, _ => 0 };
                Check(hpBefore - enemyTarget.CurrentHp == expectedDamage && player.Creature.Block - blockBefore == (i == 2 ? 10 : 0),
                    "actual card damage/block result is scoped correctly " + i);
                await Tree.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
                var progress = ModEntry.Active(state)!.Battle;
                Check(progress.Earned == Math.Max(0,i)*10 && hud.RemainingLabel.Text == progress.RemainingGains.ToString()
                    && hud.GoldLabel.Text == $"答题金币 {progress.Earned}/30", "HUD follows grading and wrong answers retain gains " + i);
                if (i == 1) await Screenshot("combat-resource-earned");
            }
            Check(ModEntry.Profile.Wrong.Count == 1, "wrong answer stored across runs");
            Check(ModEntry.Active(state)!.Battle.Earned == 30 && ModEntry.Active(state)!.Battle.RemainingGains == 0, "ordinary combat cap is 30 with three successful gains");
            hud.ActivationButton.Activate();
            Check(hud.ActivationButton.Disabled && !ModEntry.Active(state)!.Battle.Armed && !hud.ActivationButton.Animations.IsPlaying(),
                "full quota disables badge activation and stops its pulse");
            Check(!await ModEntry.Quiz(player, 2) && NModalContainer.Instance!.OpenModal == null,
                "cap suppresses further forced quizzes and grants no buff");
            await CardPileCmd.Add(boostedStrike!, PileType.Hand);
            int unboostedHp = enemyTarget.CurrentHp;
            Check(boostedStrike!.TryManualPlay(enemyTarget), "previously boosted card can be replayed after cap");
            await Wait(() => boostedStrike.Pile?.Type == PileType.Discard, "capped card finishes without a question");
            Check(unboostedHp - enemyTarget.CurrentHp == 6 && boostedStrike.DynamicVars.Damage.BaseValue == 6
                && ModEntry.Profile.AnsweredCount == 4, "boost expires after one play and capped card keeps original values");
            await Screenshot("combat-resource-full");
            var royaltiesCard = room.CombatState.CreateCard<Royalties>(player);
            await CardPileCmd.AddGeneratedCardToCombat(royaltiesCard, PileType.Hand, player);
            await CardCmd.AutoPlay(new ThrowingPlayerChoiceContext(), royaltiesCard, player.Creature);
            Check(player.Creature.Powers.OfType<RoyaltiesPower>().Any(p => p.Amount == 30), "Royalties retains its native gold-producing power");
            var spareEnemy = room.CombatState.Enemies.First(e => !ReferenceEquals(e, enemyTarget));
            await CreatureCmd.SetCurrentHp(spareEnemy, 1);
            var greedCard = room.CombatState.CreateCard<HandOfGreed>(player);
            await CardPileCmd.AddGeneratedCardToCombat(greedCard, PileType.Hand, player);
            await CardCmd.AutoPlay(new ThrowingPlayerChoiceContext(), greedCard, spareEnemy);
            GD.Print($"[Spire408 smoke] Hand of Greed result: target HP={spareEnemy.CurrentHp}, gold={player.Gold}, study gold={ModEntry.Active(state)!.Battle.Earned}, answered={ModEntry.Profile.AnsweredCount}");
            Check(player.Gold == 20 && ModEntry.Active(state)!.Battle.Earned == 30 && ModEntry.Profile.AnsweredCount == 4,
                "Hand of Greed pays native fatal gold even after the separate answer quota is full");
            foreach (var enemy in room.CombatState.Enemies.ToList()) await CreatureCmd.Kill(enemy, force: true);
            await CombatManager.Instance.CheckWinCondition();
            await Wait(() => player.Gold == 50, "victory study payout");
            Check(player.Gold == 50, "victory adds exactly 30 study gold alongside card gold");
            ModEntry.Settle(state);
            Check(player.Gold == 50, "repeated settlement cannot duplicate payout");
            var rewards = new MegaCrit.Sts2.Core.Rewards.RewardsSet(player).WithRewardsFromRoom(room);
            await rewards.GenerateWithoutOffering();
            Check(rewards.Rewards.OfType<MegaCrit.Sts2.Core.Rewards.GoldReward>().Single().Amount == 30,
                "ordinary combat gold is hidden while Royalties' extra gold reward remains");
            NRewardButton? FindRoyaltyReward() => Tree.Root.FindChildren("*", recursive:true, owned:false).OfType<NRewardButton>()
                .FirstOrDefault(b => b.IsVisibleInTree() && b.Reward is MegaCrit.Sts2.Core.Rewards.GoldReward gold && IntrinsicGoldRewards.Allows(gold));
            await Wait(() => FindRoyaltyReward() != null, "native Royalties gold reward is available");
            FindRoyaltyReward()!.ForceClick();
            await Wait(() => player.Gold == 80, "native Royalties reward pays gold");
            Check(ModEntry.Active(state)!.Battle.Earned == 30 && player.Gold == 80, "selecting a card-origin gold reward preserves the study cap");
            player.Gold = 30; // Isolated test baseline for the unchanged essay transaction checks below.
            var snapshot = RunManager.Instance.ToSave(room);
            string json = SaveManager.ToJson(snapshot);
            var parsed = SaveManager.FromJson<SerializableRun>(json);
            Check(parsed.Success && parsed.SaveData != null, "game run snapshot serializes to valid JSON");
            await SaveManager.Instance.SaveRun(room, saveProgress: false);
            var persisted = SaveManager.Instance.LoadRunSave();
            Check(persisted.Success && persisted.SaveData != null, "game saves and reads the isolated run file");
            var restored = RunState.FromSerializable(persisted.SaveData!);
            Check(ModEntry.Runs.TryGet(restored, out var restoredStudy) && restoredStudy.Enabled
                && restoredStudy.Battle.Earned == 30 && restoredStudy.Battle.RemainingGains == 0 && restoredStudy.PaidBattles.Count == 1,
                "study settings and paid ledger survive game save/load");
            Check(restoredStudy!.IntrinsicGoldRewardKeys.SetEquals(ModEntry.Active(state)!.IntrinsicGoldRewardKeys)
                && restoredStudy.IntrinsicGoldRewardKeys.Count > 0, "native card reward source journal survives save/load");
            Check(restored.Players[0].Gold == 30, "restored balance is preserved by gold guard");
            Check(restored.Players[0].Relics.OfType<StudyBook>().Count() == 1,
                "textbook relic survives native save/load without duplication");
            var study = ModEntry.Active(state)!;
            study.EssayDeck = ["practice-co-cache", "practice-ds-graph", "practice-cn-subnet"];
            study.EssayCursor = 0;
            ModEntry.Persist(state, study);
            var treasuryRoom = (EventRoom)await RunManager.Instance.EnterRoomDebug(RoomType.Event,
                model: ModelDb.Event<SunkenTreasury>(), showTransition: false);
            var treasury = treasuryRoom.LocalMutableEvent;
            await Tree.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
            Check(treasury.CurrentOptions.Count == 3 && treasury.CurrentOptions[0].Title.GetFormattedText() == "大题挑战",
                "native gold event options become a challenge with a safe leave path");
            await Screenshot("essay-event");
            NEventRoom.Instance!.OptionButtonClicked(treasury.CurrentOptions[0], 0);
            await Wait(() => NModalContainer.Instance?.OpenModal is EssayPanel, "essay panel opens from original event button");
            var essay = (EssayPanel)NModalContainer.Instance!.OpenModal!;
            essay.Answer1.Text = "6";
            essay.Back.Activate();
            await Wait(() => NModalContainer.Instance!.OpenModal == null && treasury.CurrentOptions.Count == 3
                && !treasury.CurrentOptions[0].WasChosen, "essay returns without cost");
            Check(player.Gold == 30 && ModEntry.Profile.EssayDrafts.Values.Any(a => a.FirstOrDefault() == "6"),
                "return preserves draft and does not award event gold");
            NEventRoom.Instance!.OptionButtonClicked(treasury.CurrentOptions[0], 0);
            await Wait(() => NModalContainer.Instance?.OpenModal is EssayPanel, "reopen saved essay draft");
            essay = (EssayPanel)NModalContainer.Instance!.OpenModal!;
            Check(essay.Answer1.Text == "6", "essay draft restored into editor-bound input");
            string eventKey = EssayEvents.Key(treasury, study);
            var question = ModEntry.Essays.Questions.Single(q => q.Id == study.EssayEvents[eventKey].QuestionId);
            var inputs = new[] { essay.Answer1, essay.Answer2, essay.Answer3, essay.Answer4 };
            for (int i = 0; i < question.Parts.Count; i++) inputs[i].Text = question.Parts[i].Reference;
            await Screenshot("essay-answer");
            essay.Submit.Activate();
            await Wait(() => !essay.Busy, "offline essay grading completes", 3600);
            if (essay.Submit.Text != "领取奖励") throw new InvalidOperationException(essay.Feedback.Text);
            Check(true, "bundled offline inference succeeds inside the game");
            Check(study.EssayEvents[eventKey].Attempt?.Score == 100 && essay.Feedback.GetParsedText().Contains("100 / 100"),
                "real bundled model marks canonical essay answers");
            await Screenshot("essay-score");
            string completeSnapshot = SaveManager.ToJson(SaveManager.Instance.LoadRunSave().SaveData!);
            await SaveManager.Instance.SaveRun(treasuryRoom, saveProgress: false);
            Check(EssayEvents.TransactionInProgress
                && SaveManager.ToJson(SaveManager.Instance.LoadRunSave().SaveData!) == completeSnapshot,
                "saving inside essay keeps the last complete room snapshot and separate grade journal");
            int relicsBefore = player.Relics.Count;
            essay.Submit.Activate();
            await Wait(() => treasury.IsFinished && player.Gold == 70, "essay claim completes original event", 3600);
            Check(player.Relics.Count == relicsBefore + 1 && study.EssayEvents[eventKey].BonusClaimed,
                "high essay score grants one original-game relic");
            Check(treasury.Description!.GetFormattedText().Contains("40") && !treasury.Description.GetFormattedText().Contains("60 金币"),
                "event ending describes actual study payout instead of vanilla gold");
            await Screenshot("essay-reward");
            await EssayEvents.Claim(state, study, study.EssayEvents[eventKey], player);
            Check(player.Gold == 70 && player.Relics.Count == relicsBefore + 1,
                "repeated essay claim cannot duplicate gold or relic");
            var essaySave = SaveManager.Instance.LoadRunSave();
            var essayRestored = RunState.FromSerializable(essaySave.SaveData!);
            Check(ModEntry.Runs.TryGet(essayRestored, out var essayStudy) && essayStudy.EssayEvents[eventKey].Claimed
                && essayStudy.EssayEvents[eventKey].BonusClaimed && essayRestored.Players[0].Gold == 70,
                "essay result and rewards survive native run save/load");
            await Wait(() => !EssayEvents.TransactionInProgress, "essay transaction committed");
            var reopenedTreasury = ModelDb.Event<SunkenTreasury>().ToMutable();
            await reopenedTreasury.BeginEvent(player, null, false);
            Check(reopenedTreasury.IsFinished && reopenedTreasury.CurrentOptions.Count == 0
                && reopenedTreasury.Description!.GetFormattedText().Contains("40"),
                "native event initialization restores completed study ending without new options");
            var wispRoom = (EventRoom)await RunManager.Instance.EnterRoomDebug(RoomType.Event,
                model: ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.LostWisp>(), showTransition: false);
            var wisp = wispRoom.LocalMutableEvent;
            NEventRoom.Instance!.OptionButtonClicked(wisp.CurrentOptions[1], 1);
            await Wait(() => NModalContainer.Instance?.OpenModal is EssayPanel, "second event essay opens");
            ((EssayPanel)NModalContainer.Instance!.OpenModal!).Abandon.Activate();
            await Wait(() => wisp.IsFinished && NModalContainer.Instance!.OpenModal == null, "essay abandon leaves event");
            Check(player.Gold == 70, "abandoning a challenge pays no vanilla or study gold");
            var cardChallenge = new EssayChallenge { Key = study.RunId + "/smoke-card", Act = 1,
                Attempt = new EssayAttempt { Score = 75 } };
            study.EssayEvents[cardChallenge.Key] = cardChallenge;
            int deckBefore = PileType.Deck.GetPile(player).Cards.Count;
            Task cardClaim = EssayEvents.Claim(state, study, cardChallenge, player);
            NRewardButton? FindCardReward() => Tree.Root.FindChildren("*", recursive: true, owned: false)
                .OfType<NRewardButton>().FirstOrDefault(b => b.IsVisibleInTree() && b.Reward is MegaCrit.Sts2.Core.Rewards.CardReward);
            await Wait(() => FindCardReward() != null, "rare card reward uses original rewards screen");
            var rewardButton = FindCardReward()!;
            var cardReward = (MegaCrit.Sts2.Core.Rewards.CardReward)rewardButton.Reward!;
            Check(cardReward.Cards.Count() == 3 && cardReward.Cards.All(c => c.Rarity == CardRarity.Rare),
                "75-point bonus contains three frozen rare-card candidates");
            rewardButton.ForceClick();
            NCardRewardSelectionScreen? FindCardScreen() => Tree.Root.FindChildren("*", recursive: true, owned: false)
                .OfType<NCardRewardSelectionScreen>().FirstOrDefault(s => s.IsVisibleInTree());
            await Wait(() => FindCardScreen() != null && AccessTools.Field(typeof(NCardRewardSelectionScreen), "_completionSource").GetValue(FindCardScreen()) != null,
                "native rare-card selection ready");
            await Screenshot("essay-rare-cards");
            var selectedCard = cardReward.Cards.First();
            var holder = FindCardScreen()!.GetCardHolder(selectedCard);
            holder.EmitSignal(NCardHolder.SignalName.Pressed, holder);
            await cardClaim;
            Check(player.Gold == 105 && PileType.Deck.GetPile(player).Cards.Count == deckBefore + 1 && cardChallenge.BonusClaimed,
                "native rare-card selection grants exactly one card and fixed essay gold");
            await EssayEvents.Claim(state, study, cardChallenge, player);
            Check(player.Gold == 105 && PileType.Deck.GetPile(player).Cards.Count == deckBefore + 1,
                "repeated card bonus claim is idempotent");
            var costRoom = (EventRoom)await RunManager.Instance.EnterRoomDebug(RoomType.Event,
                model: ModelDb.Event<ThisOrThat>(), showTransition: false);
            var costEvent = costRoom.LocalMutableEvent;
            int healthBefore = player.Creature.CurrentHp;
            NEventRoom.Instance!.OptionButtonClicked(costEvent.CurrentOptions[0], 0);
            await Wait(() => NModalContainer.Instance?.OpenModal is EssayPanel, "cost-bearing event essay opens");
            essay = (EssayPanel)NModalContainer.Instance!.OpenModal!;
            foreach(var input in new[] { essay.Answer1, essay.Answer2, essay.Answer3, essay.Answer4 }) input.Text = "0";
            essay.Submit.Activate();
            await Wait(() => !essay.Busy && essay.Submit.Text == "领取奖励", "zero-score essay graded");
            essay.Submit.Activate();
            await Wait(() => costEvent.IsFinished && player.Creature.CurrentHp == healthBefore - 6, "original event cost executes");
            Check(player.Gold == 105 && player.Creature.CurrentHp == healthBefore - 6,
                "zero score grants no gold while disclosed original health cost is retained");
            Check(ModEntry.Profile.EssayWrong.ContainsKey("practice-cn-subnet"), "incorrect essay enters a separate persistent notebook");
            study.EssayDeck.Add("cs408-q-2024-44");
            ModEntry.Persist(state, study);
            string modelDirectory = Spire408.AI.WakuuJudge.ModelDirectory;
            Spire408.AI.WakuuJudge.ModelDirectory = System.IO.Path.Combine(modelDirectory, "missing-for-numeric-smoke");
            try
            {
                var importedRoom = (EventRoom)await RunManager.Instance.EnterRoomDebug(RoomType.Event,
                    model: ModelDb.Event<SunkenTreasury>(), showTransition: false);
                var importedEvent = importedRoom.LocalMutableEvent;
                NEventRoom.Instance!.OptionButtonClicked(importedEvent.CurrentOptions[0], 0);
                await Wait(() => NModalContainer.Instance?.OpenModal is EssayPanel, "imported exam challenge opens without semantic model files");
                essay = (EssayPanel)NModalContainer.Instance!.OpenModal!;
                var importedQuestion = ModEntry.Essays.Questions.Single(q => q.Id == "cs408-q-2024-44");
                var fields = new[] { essay.Answer1, essay.Answer2, essay.Answer3, essay.Answer4 };
                for(int i=0;i<4;i++) fields[i].Text = importedQuestion.Parts[i].Reference;
                essay.Next.Activate(); essay.Answer1.Text = importedQuestion.Parts[4].Reference;
                essay.Submit.Activate();
                await Wait(() => !essay.Busy && essay.Submit.Text == "领取奖励", "imported calculation grades without model files");
                Check(essay.Feedback.GetParsedText().Contains("100 / 100") && essay.Rules.Text.Contains("原卷 10 分"),
                    "imported event calculates practice score while preserving original exam points");
                await Screenshot("essay-imported-event");
                essay.Submit.Activate();
                await Wait(() => importedEvent.IsFinished && player.Gold == 145, "imported practice payout");
                Check(player.Gold == 145 && study.EssayEvents[EssayEvents.Key(importedEvent,study)].Bonus == "none",
                    "imported event rewards its calculation score while retaining the per-act bonus cap");
            }
            finally { Spire408.AI.WakuuJudge.ModelDirectory = modelDirectory; }
            ModEntry.Profile.Wrong.Clear(); ModEntry.Store.Save("learning");
            ModEntry.ShowSetup(() => { });
            var essayOnlySetup = (StudySetup)NModalContainer.Instance!.OpenModal!;
            essayOnlySetup.WrongToggle.ButtonPressed = true;
            Check(!essayOnlySetup.StartButton.Disabled && essayOnlySetup.CountLabel.Text.Contains("大题 1"),
                "essay-only wrong notebook can start a study run without choice questions");
            essayOnlySetup.CancelButton.Activate();
            GD.Print($"[Spire408 smoke] COMPLETE {_checks} checks");
            Tree.Quit();
        }
        catch (Exception ex)
        {
            GD.PushError("[Spire408 smoke] FAIL " + ex);
            Tree.Quit(1);
        }
    }
}
