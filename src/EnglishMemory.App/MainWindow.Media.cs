using EnglishMemory.Core;
using Microsoft.UI.Xaml;
using System.Security.Cryptography;
namespace EnglishMemory.App;

public sealed partial class MainWindow
{
    private bool initializingMedia;
    private bool RussianMedia => engine.Settings.InterfaceLanguage == "ru";
    private void InitializeMedia()
    {
        initializingMedia = true;
        var s = engine.Settings;
        MediaEnabledToggle.IsOn = s.MediaEnabled; MediaAutoToggle.IsOn = s.MediaAutoDetect;
        MediaTitleBox.Text = s.MediaTitle; MediaEpisodeTitleBox.Text = s.MediaEpisodeTitle;
        MediaSeasonBox.Value = s.MediaSeason ?? double.NaN; MediaEpisodeBox.Value = s.MediaEpisode ?? double.NaN;
        MediaKindChoice.SelectedIndex = s.MediaKind switch { "movie" => 1, "series" => 2, "animated-series" => 3, _ => 0 };
        RefreshMediaWindows(); UpdateMediaPreview(); initializingMedia = false;
    }
    private void RefreshMediaWindows()
    {
        var windows = MediaWindows.List(); MediaWindowChoice.ItemsSource = windows;
        MediaWindowChoice.SelectedItem = windows.FirstOrDefault(w => w.Handle == engine.Settings.MediaWindowHandle && w.ProcessId == engine.Settings.MediaWindowProcess);
    }
    private void RefreshMediaWindowsClick(object sender, RoutedEventArgs e) => RefreshMediaWindows();
    private void PinMediaWindowClick(object sender, RoutedEventArgs e)
    {
        if (MediaWindowChoice.SelectedItem is not MediaWindow window) { Vm.Notice = RussianMedia ? "Выберите открытое окно плеера или браузера." : "Select an open player or browser window."; return; }
        var s = engine.Settings; s.MediaWindowHandle = window.Handle; s.MediaWindowProcess = window.ProcessId; s.MediaEnabled = s.MediaAutoDetect = true;
        engine.SaveSettings(); InitializeMedia(); Vm.Notice = RussianMedia ? "Окно выбрано. При захвате звука ПК будет сохранено его текущее название. Ручное название имеет приоритет." : "Window selected. Its current caption will be attached to PC audio. A manual title takes priority.";
    }
    private void UnpinMediaWindowClick(object sender, RoutedEventArgs e)
    {
        engine.Settings.MediaWindowHandle = 0; engine.Settings.MediaWindowProcess = 0; engine.SaveSettings(); InitializeMedia();
    }
    private void MediaToggled(object sender, RoutedEventArgs e)
    {
        if (!ready || initializingMedia) return;
        engine.Settings.MediaEnabled = MediaEnabledToggle.IsOn; engine.Settings.MediaAutoDetect = MediaAutoToggle.IsOn; engine.SaveSettings(); UpdateMediaPreview();
    }
    private void ApplyMediaClick(object sender, RoutedEventArgs e)
    {
        var s = engine.Settings; s.MediaTitle = MediaTitleBox.Text.Trim(); s.MediaEpisodeTitle = MediaEpisodeTitleBox.Text.Trim();
        s.MediaTitleEvidence = "manual"; s.MediaTitleReference = null;
        s.MediaSeason = double.IsNaN(MediaSeasonBox.Value) ? null : (int)MediaSeasonBox.Value;
        s.MediaEpisode = double.IsNaN(MediaEpisodeBox.Value) ? null : (int)MediaEpisodeBox.Value;
        s.MediaKind = MediaKindChoice.SelectedIndex switch { 1 => "movie", 2 => "series", 3 => "animated-series", _ => "unknown" };
        s.MediaEnabled = true; engine.SaveSettings(); InitializeMedia(); Vm.Notice = RussianMedia ? "Источник сохранён. Он применяется к новым встречам; старые записи не изменены." : "Source saved for new encounters. Existing records are unchanged.";
    }
    private void ClearManualMediaClick(object sender, RoutedEventArgs e)
    {
        var s = engine.Settings; s.MediaTitle = s.MediaEpisodeTitle = ""; s.MediaSeason = s.MediaEpisode = null; s.MediaKind = "unknown"; s.MediaTitleEvidence = "manual"; s.MediaTitleReference = null;
        engine.SaveSettings(); InitializeMedia();
    }
    private async void ChooseMediaSubtitlesClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var file = await OpenPicker(".srt", ".vtt").PickSingleFileAsync(); if (file is null) return;
            await Task.Run(() => new SubtitleIndex(file.Path));
            string destination;
            using (var stream = File.OpenRead(file.Path))
            {
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant()[..16];
                var folder = Path.Combine(DataPaths.Root, "Subtitles", hash); Directory.CreateDirectory(folder); destination = Path.Combine(folder, file.Name);
            }
            if (!file.Path.Equals(destination, StringComparison.OrdinalIgnoreCase)) File.Copy(file.Path, destination, true);
            engine.Settings.MediaSubtitlePath = destination; engine.Settings.MediaEnabled = true; engine.SaveSettings(); InitializeMedia();
            Vm.Notice = RussianMedia ? "Субтитры сохранены локально. Таймкод появится только при однозначном совпадении реплики." : "Subtitles saved locally. A timestamp is added only for an unambiguous match.";
        }
        catch (Exception ex) { Vm.Notice = ex.Message; }
    }
    private async void ChooseVideoFilenameClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var file = await OpenPicker(".mkv", ".mp4", ".avi", ".webm", ".mov", ".m4v").PickSingleFileAsync(); if (file is null) return;
            var context = MediaNames.Parse(file.Name, "video-file"); if (context is null) throw new InvalidDataException("Cannot read a title from this filename.");
            var s = engine.Settings; s.MediaTitle = context.Title; s.MediaSeason = context.Season; s.MediaEpisode = context.Episode; s.MediaEpisodeTitle = ""; s.MediaKind = context.Kind; s.MediaTitleEvidence = "video-file"; s.MediaTitleReference = file.Name; s.MediaEnabled = true;
            engine.SaveSettings(); InitializeMedia(); Vm.Notice = RussianMedia ? "Название взято из имени видеофайла. Видео не открывалось и не анализировалось; при необходимости исправьте метку вручную." : "Title read from the filename. Video was not opened or analyzed; edit the label if necessary.";
        }
        catch (Exception ex) { Vm.Notice = ex.Message; }
    }
    private void ClearMediaSubtitlesClick(object sender, RoutedEventArgs e) { engine.Settings.MediaSubtitlePath = null; engine.SaveSettings(); UpdateMediaPreview(); }
    private void AddMediaQuoteClick(object sender, RoutedEventArgs e)
    {
        try { engine.AddText(MediaQuoteBox.Text, "media_manual"); MediaQuoteBox.Text = ""; Vm.Notice = RussianMedia ? "Реплика с контекстом источника отправлена на локальный анализ." : "Quote and source context queued for local analysis."; Vm.Refresh(); }
        catch (Exception ex) { Vm.Notice = ex.Message; }
    }
    private void UsePcAudioClick(object sender, RoutedEventArgs e) { SourceChoice.SelectedIndex = (int)CaptureSource.PcAudio; Vm.Notice = RussianMedia ? "Выбран звук ПК. Нажмите «Слушать / Пауза», чтобы начать." : "PC Audio selected. Press Listen / Pause to start."; }
    private void UpdateMediaPreview()
    {
        var context = engine.Media.Snapshot("pc_audio");
        MediaPreview.Text = context?.Describe(RussianMedia) ?? (RussianMedia ? "Источник не определён. Выберите окно, укажите название вручную или загрузите субтитры." : "No source identified. Select a window, enter a title or load subtitles.");
        MediaSubtitleStatus.Text = File.Exists(engine.Settings.MediaSubtitlePath) ? Path.GetFileName(engine.Settings.MediaSubtitlePath) : RussianMedia ? "Субтитры не выбраны." : "No subtitles selected.";
    }
}
