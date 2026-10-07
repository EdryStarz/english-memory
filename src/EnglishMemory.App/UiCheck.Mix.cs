using EnglishMemory.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace EnglishMemory.App;

public sealed partial class MainWindow
{
    private async Task RunMixUiCheck(string dest, List<string> log)
    {
        if (Vm.Page != "Mix" || Nav.MenuItems.Count != 3 || engine.Settings.MixNewPercent != 75) throw new Exception("Focused default mix navigation is missing");
        log.Add("PASS startup opens Mix directly with 3 primary destinations");
        engine.Db.DeleteAll(); var now = DateTimeOffset.UtcNow;
        string[] phrases = ["figure out", "make a decision", "meet a deadline", "keep in touch", "take care of", "run out of", "pay attention", "give it a try", "get some sleep", "fix a bug", "work remotely", "plot twist", "carry-on luggage", "complete a quest", "deal damage", "fair enough"];
        for (int i = 0; i < phrases.Length; i++) engine.Db.SaveAnalysis(new("en", "I will figure it out before tomorrow.", "I will figure it out before tomorrow.", [new(phrases[i], i == 0 ? "разобраться / выяснить" : "Тестовый перевод для проверки карточки", "phrase", "B1", i == 0 ? 1 : .8, .8, "Conversation")]), new("I will figure it out before tomorrow.", "pc_audio", now.AddSeconds(-i), Media: new("Example Show", "animated-series", 2, 4), SourceSeconds: 65.2));
        engine.Db.SaveAnalysis(new("en","Old context","Old context",[new("handle a challenge", "справиться с трудностью", "phrase", "B2", .8,.8,"Work")]),new("Old context","pc_audio",now)); engine.Db.SetStatus(engine.Db.List("handle a challenge").Single().Id,ItemStatus.Learning);
        engine.Db.SaveAnalysis(new("en","Hello","Hello",[new("hello","привет","word","B1",1,1,"Conversation"),new("apple","яблоко","word","A1",1,1,"Food")]),new("Old junk","manual",now));
        Vm.ResetMix(); for (int loading=0; loading<20 && (MixAtlasImage.Source as Microsoft.UI.Xaml.Media.Imaging.BitmapImage)?.PixelWidth==0; loading++) await Task.Delay(100); await SaveView(dest, "mix-new-ru");
        if (Vm.ReviewCard?.Target != "figure out" || MixPicturePane.Visibility != Visibility.Visible || MixPictureShift.X != -256 || MixPictureShift.Y != 0 || MixRevealButton.Visibility != Visibility.Visible || !File.Exists(Path.Combine(AppContext.BaseDirectory,"Assets","learning-scenes.png"))) throw new Exception("Initial word, image association, reveal action or bundled picture missing");
        log.Add("PASS new B1 card appears without Inbox acceptance; actual picture association resolves");
        Vm.ShowAnswerCommand.Execute(null); await SaveView(dest,"mix-answer-ru");
        var ratePosition = MixRememberButton.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point(0,0)); if(ratePosition.Y + MixRememberButton.ActualHeight > Root.ActualHeight - 24 || MixRememberButton.ActualHeight < 40) throw new Exception("Primary rating action is below the visible window");
        if (!Vm.ReviewAnswer.Contains("разобраться") || !Vm.ReviewQuote.Contains("S02E04") || MixRevealButton.Visibility != Visibility.Collapsed) throw new Exception("Answer, episode context or one-click reveal missing");
        var kinds=new List<bool>(); for(int i=0;i<4;i++) { kinds.Add(Vm.ReviewPosition.StartsWith("НОВОЕ")); Vm.ShowAnswerCommand.Execute(null); Vm.Rate(Rating.Good); }
        if(!kinds.SequenceEqual(new[]{true,true,true,false}) || engine.Db.Stats().Reviews!=4) throw new Exception("UI mix did not follow 3:1 or persist ratings");
        log.Add("PASS UI continues automatically through 3 new / 1 review; source context and ratings persist");
        var removed=Vm.ReviewCard!.Id; Vm.IgnoreMixCommand.Execute(null); if(engine.Db.List(status:ItemStatus.Suspended).All(v=>v.Id!=removed) || Vm.ReviewCard?.Id==removed) throw new Exception("Hide does not advance directly"); Vm.UndoActionCommand.Execute(null); if(engine.Db.List(status:ItemStatus.Suspended).Any(v=>v.Id==removed)) throw new Exception("Hide undo failed");
        var known=Vm.ReviewCard!.Id; Vm.KnowMixCommand.Execute(null); if(engine.Db.List(status:ItemStatus.Known).All(v=>v.Id!=known) || Vm.ReviewCard?.Id==known) throw new Exception("Known does not advance directly"); Vm.UndoActionCommand.Execute(null);
        log.Add("PASS one-action Known / Not needed immediately advance, with reversible undo");
        Vm.HideBasicCommand.Execute(null); if(engine.Db.List(status:ItemStatus.Suspended).Count!=2) throw new Exception("Bulk basic cleanup failed"); Vm.UndoActionCommand.Execute(null);
        Vm.Page="Library"; Vm.Refresh(); if(Vm.Items.Any(v=>v.Target is "hello" or "apple")) throw new Exception("Default dictionary still shows basic junk");
        PhraseList.SelectedItems.Add(Vm.Items[0]); PhraseList.SelectedItems.Add(Vm.Items[1]);
        engine.Db.SaveAnalysis(new("en","New capture","New capture",[new("freshly captured phrase","новая фраза","phrase","B1",.1,.1,"Work")]),new("New capture","pc_audio",now)); Vm.Refresh();
        if(PhraseList.SelectedItems.Count!=2) throw new Exception("New capture cleared the multi-selection"); HideSelectedClick(this,new RoutedEventArgs());
        if(engine.Db.List(status:ItemStatus.Suspended).Count!=2) throw new Exception("Multiple selection action did not persist"); Vm.UndoActionCommand.Execute(null); await SaveView(dest,"dictionary-clean-ru");
        LibraryStateChoice.SelectedIndex=3; if(Vm.Items.Count!=0) throw new Exception("Hidden words view failed after undo"); LibraryStateChoice.SelectedIndex=0;
        log.Add("PASS bulk cleanup, multiple selection, default clean dictionary and status views");
        Vm.Page="Mix"; Vm.ShowAnswerCommand.Execute(null); ApplyTheme("Dark"); await SaveView(dest,"mix-answer-dark");
        LanguageChoice.SelectedIndex=1; await SaveView(dest,"mix-en"); if(PageTitle.Text!="Mix" || !MixPictureLabel.Text.StartsWith("Association:")) throw new Exception("Mix English localization failed"); LanguageChoice.SelectedIndex=0; ApplyTheme("Light");
        AppWindow.Resize(new Windows.Graphics.SizeInt32(900,650)); await SaveView(dest,"mix-900x650"); if(MixPictureFrame.Width!=192 || Nav.IsPaneOpen) throw new Exception("Narrow card does not reflow");
        log.Add("PASS RU/EN, dark/light and narrow card reflow");
        var card=Vm.ReviewCard!; var dir=Path.Combine(DataPaths.Root,"CardPictures"); Directory.CreateDirectory(dir); File.Copy(Path.Combine(AppContext.BaseDirectory,"Assets","learning-scenes.png"),Path.Combine(dir,PictureKey(card)+".png")); UpdateMixPicture(); await SaveView(dest,"mix-custom-picture");
        if(CustomCardPicture.Visibility!=Visibility.Visible || MixPictureCanvas.Visibility!=Visibility.Collapsed) throw new Exception("Custom image attachment did not replace automatic picture");
        log.Add("PASS local custom picture overrides illustration association");
    }
}
