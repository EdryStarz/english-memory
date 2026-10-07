using CommunityToolkit.Mvvm.Input;
using EnglishMemory.Core;
namespace EnglishMemory.App.ViewModels;

public partial class ShellViewModel
{
    public int LibraryState { get; set; }
    private MixSession? mix;
    private MixSession Mix => mix ??= new(Engine.Db, Engine.Settings);
    private Dictionary<long, ItemStatus> undoStates = [];
    private bool Ru => Engine.Settings.InterfaceLanguage == "ru";
    public bool HasReviewCard => ReviewCard is not null;
    public bool MixEmpty => ReviewCard is null;
    public bool CanUndo => undoStates.Count > 0;
    private string mixSummary = "";
    public string MixSummary { get => mixSummary; private set => SetProperty(ref mixSummary, value); }
    public string MixSettingsLabel => Engine.Settings.MinimumCaptureLevel + "+ / " + Engine.Settings.MixNewPercent + "%";
    public string MinimumLevelLabel => Engine.Settings.MinimumCaptureLevel + "+";
    [RelayCommand] private void StartMix() { Page = "Mix"; if (ReviewCard is null) NextMix(); }
    public void ResetMix() { Mix.Reset(); ReviewCard = null; if (Page == "Mix") NextMix(); Refresh(); }
    private void RefreshMix()
    {
        OnPropertyChanged(nameof(MixSettingsLabel));
        MixSummary = Ru ? $"{Engine.Settings.MixNewPercent}% новых · остальные — повторения     /     {Mix.Completed} ответов в этой сессии" : $"{Engine.Settings.MixNewPercent}% new · the rest are repeats     /     {Mix.Completed} answers this session";
        if (Page != "Mix") return;
        if (ReviewCard is null || ReviewCard.Id != Mix.Current?.Id) NextMix(); else UpdateMixCard();
    }
    private void NextMix()
    {
        AnswerShown = false; ReviewCard = Mix.Next(); UpdateMixCard();
        OnPropertyChanged(nameof(HasReviewCard)); OnPropertyChanged(nameof(MixEmpty));
    }
    private void UpdateMixCard()
    {
        ReviewPrompt = ReviewCard?.Target ?? (Ru ? "Пока всё!" : "You're all caught up!");
        ReviewAnswer = ReviewCard?.Translation ?? "";
        ReviewQuote = ReviewCard is null ? "" : (string.IsNullOrWhiteSpace(ReviewCard.Quote) ? (Ru ? "Фраза из встроенной подборки." : "A phrase from the starter collection.") : ReviewCard.Quote) + (ReviewCard.HasMedia ? "\n" + ReviewCard.MediaLabel : "");
        ReviewPosition = ReviewCard is null ? (Ru ? "Добавьте текст или включите прослушивание — новые слова появятся здесь." : "Add text or turn on listening — new words will appear here.") : (Mix.CurrentIsNew ? (Ru ? "НОВОЕ" : "NEW") : Mix.CurrentIsPractice ? (Ru ? "ПРАКТИКА" : "PRACTICE") : (Ru ? "ПОВТОРЕНИЕ" : "REVIEW")) + " · " + ReviewCard.Cefr + " · " + UI.Localization.T(ReviewCard.Topic);
    }
    private void RateMix(Rating rating)
    {
        if (!AnswerShown || ReviewCard is null) return;
        Mix.Rate(rating); NextMix(); Refresh();
    }
    [RelayCommand] private void KnowMix() { if (ReviewCard is not null) ChangeItems([ReviewCard.Id], ItemStatus.Known); }
    [RelayCommand] private void IgnoreMix() { if (ReviewCard is not null) ChangeItems([ReviewCard.Id], ItemStatus.Suspended); }
    public void ChangeItems(IEnumerable<long> ids, ItemStatus status)
    {
        var changes = Engine.Db.SetStatuses(ids, status); if (changes.Count == 0) return;
        undoStates = changes; OnPropertyChanged(nameof(CanUndo));
        foreach (var id in changes.Keys) Mix.Forget(id);
        if (ReviewCard is not null && changes.ContainsKey(ReviewCard.Id)) { if (Page == "Mix") NextMix(); else ReviewCard = null; }
        Notice = Ru ? $"{(status == ItemStatus.Known ? "Отмечено как известное" : status == ItemStatus.Learning ? "Добавлено в обучение" : "Убрано из обучения")}: {changes.Count}. Можно отменить." : $"{changes.Count} cards {(status == ItemStatus.Known ? "marked known" : status == ItemStatus.Learning ? "added to learning" : "hidden from learning")}. Undo is available.";
        Refresh();
    }
    [RelayCommand] private void UndoAction()
    {
        if (undoStates.Count == 0) return;
        Engine.Db.RestoreStatuses(undoStates); undoStates.Clear(); OnPropertyChanged(nameof(CanUndo));
        Notice = Ru ? "Действие отменено." : "Action undone."; ResetMix();
    }
    [RelayCommand] private void HideBasic()
    {
        var changes = Engine.Db.HideBasicNew(Engine.Settings);
        if (changes.Count == 0) { Notice = Ru ? "В очереди нет базовых или неанглийских слов." : "No basic or non-English words in the new queue."; return; }
        undoStates = changes; OnPropertyChanged(nameof(CanUndo));
        Notice = Ru ? $"Убрано базовых / некорректных карточек: {changes.Count}. Действие можно отменить." : $"{changes.Count} basic / invalid cards hidden. You can undo this.";
        if (ReviewCard is not null && changes.ContainsKey(ReviewCard.Id)) NextMix(); Refresh();
    }
}
