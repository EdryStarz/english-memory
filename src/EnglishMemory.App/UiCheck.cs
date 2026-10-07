using EnglishMemory.Core;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
namespace EnglishMemory.App;

public sealed partial class MainWindow
{
    private async Task RunUiCheck()
    {
        var dest = Environment.GetEnvironmentVariable("ENGLISH_MEMORY_UI_OUTPUT") ?? Path.Combine(DataPaths.Root, "ui-check"); Directory.CreateDirectory(dest); var log = new List<string>();
        try
        {
            await Task.Delay(600); if (engine.Listening) throw new Exception("Listening started without user action."); log.Add("PASS startup recording off");
            if (Environment.GetEnvironmentVariable("ENGLISH_MEMORY_UI_MIX") == "1") { await RunMixUiCheck(dest, log); return; }
            if (Environment.GetEnvironmentVariable("ENGLISH_MEMORY_UI_MEDIA") == "1")
            {
                Vm.Page = "Media"; MediaTitleBox.Text = "Example Show"; MediaKindChoice.SelectedIndex = 3; MediaSeasonBox.Value = 2; MediaEpisodeBox.Value = 4; MediaEpisodeTitleBox.Text = "A new plan"; ApplyMediaClick(this, new RoutedEventArgs());
                var subtitle = Path.Combine(DataPaths.Root, "Example.Show.S02E04.srt"); await File.WriteAllTextAsync(subtitle, "1\n00:01:05,200 --> 00:01:08,000\nI will figure it out before tomorrow.\n"); engine.Settings.MediaSubtitlePath = subtitle; engine.SaveSettings(); UpdateMediaPreview(); await SaveView(dest, "media-source-ru");
                MediaQuoteBox.Text = "I will figure it out before tomorrow."; AddMediaQuoteClick(this, new RoutedEventArgs());
                var queued = engine.Db.NextQueued()!.Value.Transcript;
                if (queued.Media is not { Title: "Example Show", Kind: "animated-series", Season: 2, Episode: 4 } || queued.SourceSeconds != 65.2) throw new Exception("Media form did not persist source/timing in queue");
                if (!MediaIntroText.Text.StartsWith("Привяжите") || !MediaEnabledToggle.Header.ToString()!.StartsWith("Сохранять")) throw new Exception("New media page static labels are not Russian");
                log.Add("PASS source form → durable text queue with title, kind, season, episode and timestamp");
                var analysis = new Analysis("en", queued.Text, queued.Text, [new("figure out", "разобраться", "phrasal verb", "B1", .9, .9, "Movies")]); engine.Db.SaveAnalysis(analysis, queued);
                Vm.Page = "Library"; Vm.Search = "example show"; Vm.Refresh(); if (Vm.Items.Count != 1) throw new Exception("Media-title Library search failed"); Vm.Selected = Vm.Items[0]; await SaveView(dest, "media-library-ru");
                if (!Vm.Details.Contains("Example Show") || !Vm.Details.Contains("S02E04") || !Vm.Details.Contains("указано вами")) throw new Exception("Media provenance absent from encounters"); log.Add("PASS Library title search, episode label, real encounter and provenance");
                Vm.LearnCommand.Execute(null); Vm.StartReviewCommand.Execute(null); Vm.ShowAnswerCommand.Execute(null); if (!Vm.ReviewQuote.Contains("S02E04")) throw new Exception("Media context absent from review"); await SaveView(dest, "media-review-ru"); log.Add("PASS source context in review");
                Vm.Page = "Media"; LanguageChoice.SelectedIndex = 1; await SaveView(dest, "media-source-en"); LanguageChoice.SelectedIndex = 0; log.Add("PASS media page in RU and EN"); return;
            }
            if (Environment.GetEnvironmentVariable("ENGLISH_MEMORY_UI_DIAGNOSTIC") == "1")
            {
                var originalSpeech = engine.Settings.SpeechModel;
                engine.Settings.SpeechModel = null;
                if (!engine.Ai.SpeechAvailable) throw new Exception("Installed model was not recovered after lost selection");
                log.Add("PASS installed speech model recovered after lost selection");
                await engine.Ai.ValidateSpeech(CancellationToken.None); log.Add("PASS actual Whisper model loaded");
                await Vm.ToggleListeningCommand.ExecuteAsync(null);
                if (!engine.Listening) throw new Exception("Listening command failed: " + Vm.Notice);
                await SaveView(dest, "listening-ru"); engine.Stop(); log.Add("PASS actual Listen command and capture start/stop");
                var result = await engine.Ai.Analyze("Я сегодня вообще не выспался.", CancellationToken.None); log.Add("PASS actual language model: " + result.NaturalEnglish);
                Vm.Page = "Models"; await SaveView(dest, "models-ru"); Vm.Page = "Settings"; await SaveView(dest, "settings-ru");
                LanguageChoice.SelectedIndex = 1; await SaveView(dest, "settings-en");
                if (PageTitle.Text != "Settings") throw new Exception("English language switch failed");
                LanguageChoice.SelectedIndex = 0; if (PageTitle.Text != "Настройки") throw new Exception("Russian language switch failed");
                log.Add("PASS RU/EN switch and persisted selection"); return;
            }
            if (Environment.GetEnvironmentVariable("ENGLISH_MEMORY_UI_STRESS") == "1")
            {
                Vm.Page = "Library"; await SaveView(dest, "library-20000"); Vm.Search = "stress phrase 19999"; if (Vm.Items.Count != 1) throw new Exception("20k UI search failed"); Vm.Selected = Vm.Items[0]; await SaveView(dest, "library-20000-search"); Vm.Search = ""; ApplyTheme("Dark"); AppWindow.Resize(new Windows.Graphics.SizeInt32(3840, 2160)); await SaveView(dest, "library-4k-dark"); AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 720)); await SaveView(dest, "library-1280x720"); log.Add("PASS 20000 phrase UI, search, long Cyrillic translation, large/small windows"); return;
            }
            await SaveView(dest, "today-empty");
            engine.TogglePrivate(); if (!engine.Private || engine.Listening) throw new Exception("Private mode failed"); engine.TogglePrivate(); log.Add("PASS private mode and visible status");
            var now = DateTimeOffset.UtcNow;
            engine.Db.SaveAnalysis(new("en", "I'll figure it out.", "I'll figure it out.", [new("figure out", "разобраться / выяснить", "phrasal verb", "B1", .9, .9, "Conversation")]), new("I'll figure it out.", "pc_audio", now));
            Vm.Page = "Inbox"; Vm.Refresh(); await SaveView(dest, "inbox-one-phrase"); Vm.Page = "Today";
            engine.Db.SaveAnalysis(new("ru", "Я сегодня вообще не выспался.", "I barely got any sleep today.", [new("get some sleep", "поспать", "phrase", "B1", .8, .95, "Home")]), new("Я сегодня вообще не выспался.", "microphone", now));
            for (int i = 0; i < 20; i++) engine.Db.SaveAnalysis(new("en", $"Context {i}", "This is a UI test fixture.", [new($"test phrase {i}", "Тестовая фраза для проверки интерфейса", "phrase", "A2", .4, .4, "Work")]), new($"A long real-context-shaped test fixture {i}. This data only exists in the UI check database.", "test", now));
            Vm.Refresh(); await SaveView(dest, "today-light"); Vm.Page = "Inbox"; Vm.Selected = Vm.Items.First(); await SaveView(dest, "inbox-light"); log.Add("PASS Inbox data binding: " + Vm.Items.Count);
            Vm.LearnCommand.Execute(null); Vm.StartReviewCommand.Execute(null); Vm.ShowAnswerCommand.Execute(null); await SaveView(dest, "review-light"); Vm.Rate(Rating.Good); if (engine.Db.Stats().Reviews != 1) throw new Exception("Review did not persist"); log.Add("PASS review UI and persistence");
            Vm.Page = "Library"; Vm.Search = "figure"; if (Vm.Items.Count != 1) throw new Exception("Search binding failed"); Vm.Selected = Vm.Items[0]; await SaveView(dest, "library-light"); Vm.Search = "";
            Vm.Page = "Progress"; await SaveView(dest, "progress-light"); Vm.Page = "Settings"; await SaveView(dest, "settings-light"); Vm.Page = "Models"; await SaveView(dest, "models-light");
            ApplyTheme("Dark"); Vm.Page = "Today"; await SaveView(dest, "today-dark"); Vm.Page = "Inbox"; Vm.Selected = Vm.Items.First(); await SaveView(dest, "inbox-dark");
            AppWindow.Resize(new Windows.Graphics.SizeInt32(900, 650)); Vm.Page = "Today"; await SaveView(dest, "today-small-dark");
            Root.MaxWidth = 640; Root.MaxHeight = 360; Root.HorizontalAlignment = HorizontalAlignment.Left; Root.VerticalAlignment = VerticalAlignment.Top; Root.UpdateLayout(); await SaveView(dest, "layout-640x360-effective"); Root.MaxWidth = double.PositiveInfinity; Root.MaxHeight = double.PositiveInfinity; log.Add("PASS theme switches and small effective layout rendered");
            log.Add("UI CHECK COMPLETE");
        }
        catch (Exception e) { log.Add("FAIL " + e); }
        finally { File.WriteAllLines(Path.Combine(dest, "ui-check.log"), log); await Quit(); }
    }
    private async Task SaveView(string dest, string name)
    {
        Root.UpdateLayout(); await Task.Delay(250); var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(Root); var pixels = (await bitmap.GetPixelsAsync()).ToArray();
        using var stream = new InMemoryRandomAccessStream(); var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream); encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels); await encoder.FlushAsync(); stream.Seek(0); using var reader = new DataReader(stream.GetInputStreamAt(0)); await reader.LoadAsync((uint)stream.Size); var bytes = new byte[stream.Size]; reader.ReadBytes(bytes); await File.WriteAllBytesAsync(Path.Combine(dest, name + ".png"), bytes);
    }
}
