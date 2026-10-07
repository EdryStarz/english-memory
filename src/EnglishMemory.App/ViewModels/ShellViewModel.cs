using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnglishMemory.Core;
using System.Collections.ObjectModel;
namespace EnglishMemory.App.ViewModels;

public partial class ShellViewModel : ObservableObject
{
    public Engine Engine { get; }
    public ObservableCollection<Vocabulary> Items { get; } = [];
    public ObservableCollection<Vocabulary> Recent { get; } = [];
    public ObservableCollection<string> Live { get; } = [];
    private string _status = ""; public string Status { get => _status; set { SetProperty(ref _status, value); } }
    private string _notice = ""; public string Notice { get => _notice; set { SetProperty(ref _notice, EnglishMemory.App.UI.Localization.T(value)); } }
    private string _page = ""; public string Page { get => _page; set { if (SetProperty(ref _page, value)) OnPageChanged(value); } }
    private string _search = ""; public string Search { get => _search; set { if (SetProperty(ref _search, value)) OnSearchChanged(value); } }
    private string _quickText = ""; public string QuickText { get => _quickText; set { SetProperty(ref _quickText, value); } }
    private string _metrics = ""; public string Metrics { get => _metrics; set { SetProperty(ref _metrics, value); } }
    private string _progress = ""; public string Progress { get => _progress; set { SetProperty(ref _progress, value); } }
    private Vocabulary? _selected; public Vocabulary? Selected { get => _selected; set { if (SetProperty(ref _selected, value)) OnSelectedChanged(value); } }
    private Vocabulary? _reviewCard; public Vocabulary? ReviewCard { get => _reviewCard; set { SetProperty(ref _reviewCard, value); } }
    private bool _answerShown; public bool AnswerShown { get => _answerShown; set { SetProperty(ref _answerShown, value); } }
    private string _reviewPrompt = ""; public string ReviewPrompt { get => _reviewPrompt; set { SetProperty(ref _reviewPrompt, value); } }
    private string _reviewAnswer = ""; public string ReviewAnswer { get => _reviewAnswer; set { SetProperty(ref _reviewAnswer, value); } }
    private string _reviewQuote = ""; public string ReviewQuote { get => _reviewQuote; set { SetProperty(ref _reviewQuote, value); } }
    private string _reviewPosition = ""; public string ReviewPosition { get => _reviewPosition; set { SetProperty(ref _reviewPosition, value); } }
    private string _details = ""; public string Details { get => _details; set { SetProperty(ref _details, value); } }
    private string _filter = ""; public string Filter { get => _filter; set { if (SetProperty(ref _filter, value)) OnFilterChanged(value); } }
    private Queue<Vocabulary> review = []; private int reviewed, total;
    public event Action? RefreshRequested;
    public ShellViewModel(Engine engine) { Engine = engine; Status = "Idle · listening is off"; Notice = Search = QuickText = Metrics = Progress = ReviewAnswer = ReviewQuote = ReviewPosition = ""; Page = "Mix"; ReviewPrompt = "Your review queue is clear."; Details = EnglishMemory.App.UI.Localization.T("Select a phrase to see its real encounters."); Filter = "All topics"; }
    private void OnSearchChanged(string value) => Refresh();
    private void OnPageChanged(string value) => Refresh();
    private void OnFilterChanged(string value) => Refresh();
    private void OnSelectedChanged(Vocabulary? value)
    {
        if (value is null) { Details = EnglishMemory.App.UI.Localization.T("Select a phrase to see its real encounters."); return; }
        var encounters = Engine.Db.Encounters(value.Id);
        Details = $"{value.Target}\n{value.Translation}\n\n{(value.Media is null ? "" : value.Media.Describe(Engine.Settings.InterfaceLanguage == "ru") + "\n\n")}{value.Metadata}\nFirst seen: {value.FirstSeen.LocalDateTime:d}\nNext review: {value.Due.LocalDateTime:g}\nStability: {value.Stability:F2} days · Difficulty: {value.Difficulty:F2}\nReviews: {value.Reviews}\n\nREAL ENCOUNTERS\n\n" + string.Join("\n\n", encounters.Select(e => $"{e.Timestamp.LocalDateTime:g} · {(e.Source == "microphone" ? "I said" : e.Source == "pc_audio" ? "I heard" : e.Source == "media_manual" ? (Engine.Settings.InterfaceLanguage == "ru" ? "Реплика из видео" : "Video quote") : e.Source == "subtitle" ? (Engine.Settings.InterfaceLanguage == "ru" ? "Субтитры" : "Subtitles") : e.Source)}{(e.SourceSeconds.HasValue ? $" · {TimeSpan.FromSeconds(e.SourceSeconds.Value):g}" : "")}\n{(e.Media is null ? "" : e.Media.Describe(Engine.Settings.InterfaceLanguage == "ru") + "\n")}{e.Quote}\n{e.NaturalEnglish}"));
    }
    public void Refresh()
    {
        Status = Engine.Settings.InterfaceLanguage == "ru" ? (Engine.Private ? "Приватный режим · запись остановлена" : Engine.CaptureError is not null ? "Ошибка захвата · запись остановлена" : Engine.Listening ? (Engine.Processing ? "Слушаю · обрабатываю локально" : "Слушаю · звук остаётся на компьютере") : Engine.Processing ? "Обрабатываю локально" : "Прослушивание выключено") : Engine.Status; var s = Engine.Db.Stats(); Metrics = Engine.Settings.InterfaceLanguage == "ru" ? $"{s.Due} к повторению    ·    {s.Inbox} во входящих    ·    {s.Learning} изучается" : $"{s.Due} due    ·    {s.Inbox} in Inbox    ·    {s.Learning} learning";
        Progress = $"{s.Total} phrases in your memory\n{s.Encounters} real encounters\n{s.Reviews} reviews · {s.Retention:P0} recall success\n{s.Streak} day streak\n{TimeSpan.FromSeconds(s.ListeningSeconds):g} listening time\n\nReal-life coverage\n{(s.Coverage.HasValue ? $"{s.Coverage:P0} of extracted phrase encounters this week are marked Known or Mastered." : "Listen to English audio to build a weekly estimate.")}\n\nCoverage measures extracted phrases, not all words in the audio. CEFR levels are model estimates.";
        if (Engine.Settings.InterfaceLanguage == "ru") Progress = $"{s.Total} фраз в библиотеке\n{s.Encounters} реальных встреч\n{s.Reviews} повторений · {s.Retention:P0} успешных ответов\n{s.Streak} дней подряд\n{TimeSpan.FromSeconds(s.ListeningSeconds):g} прослушивания\n\nПокрытие встреченных фраз\n{(s.Coverage.HasValue ? $"{s.Coverage:P0} встреченных фраз за неделю отмечены как известные или освоенные." : "Слушайте английскую речь, чтобы получить недельную оценку.")}\n\nОценка относится к извлечённым фразам. Уровни CEFR определяет модель.";
        Recent.Clear(); foreach (var i in Engine.Db.List(limit: 6, personalOnly: true)) Recent.Add(i);
        var fresh = Engine.Db.List(Search, Page == "Inbox" ? ItemStatus.New : null, topic: Filter == "All topics" ? null : Filter, limit: Math.Max(500, Items.Count), personalOnly: Page == "My Life", minimumLevel: Page == "Library" && LibraryState == 0 ? Engine.Settings.MinimumCaptureLevel : null, libraryState: Page == "Library" ? LibraryState switch { 0 => "active", 2 => "known", 3 => "hidden", _ => null } : null); if (Page == "Library" && LibraryState == 0) fresh = fresh.Where(v => LearningFilter.Accept(v, Engine.Settings)).ToList(); if (!Items.SequenceEqual(fresh)) { var id = Selected?.Id; var ids = fresh.Select(v => v.Id).ToHashSet();
            for (int i = Items.Count - 1; i >= 0; i--) if (!ids.Contains(Items[i].Id)) Items.RemoveAt(i);
            for (int i = 0; i < fresh.Count; i++)
            {
                if (i >= Items.Count || Items[i].Id != fresh[i].Id)
                {
                    var existing = Items.Select((v, at) => (v, at)).FirstOrDefault(x => x.v.Id == fresh[i].Id);
                    if (existing.v is not null) Items.Move(existing.at, i); else Items.Insert(i, fresh[i]);
                }
                if (Items[i] != fresh[i]) Items[i] = fresh[i];
            }
            Selected = Items.FirstOrDefault(i => i.Id == id); }
        RefreshMix(); RefreshRequested?.Invoke();
    }
    [RelayCommand] private async Task ToggleListening()
    {
        try
        {
            if (Engine.Listening) Engine.Stop();
            else
            {
                Notice = "Loading speech model…";
                await Task.Run(() => Engine.Ai.ValidateSpeech(CancellationToken.None));
                Engine.Start(); Notice = "Listening started. Speak, then pause to finish a phrase.";
            }
        }
        catch (Exception e)
        {
            Notice = "Listening could not start: " + e.Message;
            File.AppendAllText(Path.Combine(DataPaths.Root, "diagnostics.log"), DateTimeOffset.Now + " " + e + Environment.NewLine);
        }
        Refresh();
    }
    [RelayCommand] private void TogglePrivate() { try { Engine.TogglePrivate(); } catch (Exception e) { Notice = e.Message; } Refresh(); }
    [RelayCommand] private void CaptureRecent() { Engine.Audio.CaptureRecent(); Notice = Engine.Listening ? "Recent audio queued for local transcription." : "Start Listening to fill the recent audio buffer."; }
    [RelayCommand] private void AddText() { try { Engine.AddText(QuickText); QuickText = ""; Notice = "Text queued for local analysis."; } catch (Exception e) { Notice = e.Message; } Refresh(); }
    [RelayCommand] private void Learn() { if (Selected is null) return; Engine.Db.SetStatus(Selected.Id, ItemStatus.Learning); Refresh(); }
    [RelayCommand] private void Important() { if (Selected is null) return; Engine.Db.SetStatus(Selected.Id, ItemStatus.Learning, true); Refresh(); }
    [RelayCommand] private void Ignore() { if (Selected is null) return; ChangeItems([Selected.Id], ItemStatus.Suspended); }
    [RelayCommand] private void MarkKnown() { if (Selected is null) return; ChangeItems([Selected.Id], ItemStatus.Known); }
    [RelayCommand]
    private void StartReview()
    {
        var s = Engine.Settings; var due = Engine.Db.List(dueOnly: true, limit: s.ReviewsPerDay); review = new(due); total = review.Count; reviewed = 0; Page = "Review"; NextCard();
    }
    private void NextCard() { AnswerShown = false; ReviewCard = review.Count > 0 ? review.Dequeue() : null; ReviewPrompt = ReviewCard?.Prompt ?? EnglishMemory.App.UI.Localization.T("Your review queue is clear."); ReviewAnswer = ReviewCard?.Answer ?? ""; ReviewQuote = ReviewCard is not null ? ReviewCard.Quote + (ReviewCard.HasMedia ? "\n" + ReviewCard.MediaLabel : "") : EnglishMemory.App.UI.Localization.T("Learn a phrase from Inbox to add it to your reviews."); ReviewPosition = ReviewCard is null ? $"{reviewed} reviews completed" : $"{reviewed + 1} of {total} · {(ReviewCard.Source == "microphone" ? "Your words" : "Your English")}"; }
    [RelayCommand] private void ShowAnswer() => AnswerShown = ReviewCard is not null;
    public void Rate(Rating rating) { if (Page == "Mix") { RateMix(rating); return; } if (!AnswerShown || ReviewCard is null) return; Engine.Db.Review(ReviewCard, rating, Engine.Settings.Retention); reviewed++; NextCard(); Refresh(); }
    public void AddLive(Transcript t) { Live.Insert(0, $"{t.Time.LocalDateTime:T} · {(t.Source == "microphone" ? "I said" : "I heard")}\n{t.Text}"); while (Live.Count > 100) Live.RemoveAt(Live.Count - 1); }
}
