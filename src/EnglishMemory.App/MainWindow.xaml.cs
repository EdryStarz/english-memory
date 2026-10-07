using EnglishMemory.App.Infrastructure;
using EnglishMemory.App.ViewModels;
using EnglishMemory.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Diagnostics;
using System.IO.Compression;
using Windows.System;
using Windows.Storage.Pickers;
namespace EnglishMemory.App;

public sealed partial class MainWindow : Microsoft.UI.Xaml.Window
{
    public ShellViewModel Vm { get; }
    private readonly Engine engine;
    private readonly Speaker speaker = new();
    private readonly WindowsIntegration windows;
    private bool ready, quitting;
    private CancellationTokenSource? download;
    private readonly Microsoft.UI.Xaml.DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(5) };
    public MainWindow()
    {
        File.AppendAllText(Path.Combine(DataPaths.Root, "startup.log"), "Window initializing\n"); InitializeComponent(); File.AppendAllText(Path.Combine(DataPaths.Root, "startup.log"), "Window XAML ready\n"); engine = new(); Vm = new(engine); Root.DataContext = Vm;
        UI.Localization.Language = engine.Settings.InterfaceLanguage;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "EnglishMemory.ico"));
        SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        windows = new(hwnd, () => { AppWindow.Show(); Activate(); }, () => Vm.ToggleListeningCommand.Execute(null), () => Vm.CaptureRecentCommand.Execute(null), () => Vm.TogglePrivateCommand.Execute(null), async () => await Quit(), id => { if (id == 100) Vm.ToggleListeningCommand.Execute(null); if (id == 101) Vm.TogglePrivateCommand.Execute(null); if (id == 102) Vm.CaptureRecentCommand.Execute(null); });
        try { if (!Environment.GetCommandLineArgs().Contains("--ui-check")) windows.SetHotkeys(engine.Settings); } catch (Exception e) { Vm.Notice = e.Message; }
        engine.Changed += () => DispatcherQueue.TryEnqueue(() => { if (quitting) return; Vm.Refresh(); UpdateStatus(); });
        engine.Message += s => DispatcherQueue.TryEnqueue(() => { if (quitting) return; Vm.Notice = s; NoticeBar.IsOpen = true; });
        engine.Transcribed += t => DispatcherQueue.TryEnqueue(() => { if (!quitting) Vm.AddLive(t); });
        Vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Vm.Page)) ShowPage(); if (e.PropertyName == nameof(Vm.ReviewCard)) UpdateMixPicture(); if (e.PropertyName == nameof(Vm.Notice) && Vm.Notice.Length > 0) NoticeBar.IsOpen = Vm.Page != "Mix"; if (e.PropertyName == nameof(Vm.AnswerShown)) { ShowAnswerButton.Visibility = AnswerHint.Visibility = Vm.AnswerShown ? Visibility.Collapsed : Visibility.Visible; UpdateMixControls(); } };
        Vm.RefreshRequested += () => { EmptyBrowse.Visibility = Vm.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed; EmptyRecent.Visibility = Vm.Recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed; };
        InitializeSettings(); InitializeMedia(); InitializeMix(); Nav.SelectedItem = Nav.MenuItems[0];
        Root.KeyDown += KeyPressed; AddAccelerator(VirtualKey.F, VirtualKeyModifiers.Control, () => { Vm.Page = "Library"; SearchBox.Focus(FocusState.Programmatic); }); AddAccelerator(VirtualKey.K, VirtualKeyModifiers.Control, () => { Vm.Page = "Library"; SearchBox.Focus(FocusState.Programmatic); });
        var s = engine.Settings; var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(Math.Clamp(s.WindowX, area.X, area.X + Math.Max(0, area.Width - 300)), Math.Clamp(s.WindowY, area.Y, area.Y + Math.Max(0, area.Height - 200)), Math.Clamp(s.WindowWidth, 800, Math.Max(800, area.Width)), Math.Clamp(s.WindowHeight, 580, Math.Max(580, area.Height))));
        AppWindow.Changed += (_, e) => { if (ready && (e.DidPositionChange || e.DidSizeChange)) { s.WindowX = AppWindow.Position.X; s.WindowY = AppWindow.Position.Y; s.WindowWidth = AppWindow.Size.Width; s.WindowHeight = AppWindow.Size.Height; } };
        AppWindow.Closing += (sender, e) => { if (!quitting && s.MinimizeToTray) { e.Cancel = true; AppWindow.Hide(); } else if (!quitting) { e.Cancel = true; _ = Quit(); } };
        ready = true; Vm.Refresh(); UpdateStatus(); timer.Tick += (_, _) => { Vm.Refresh(); UpdateStatus(); UI.Localization.Apply(Root); }; timer.Start();
        Root.Loaded += (_, _) => { ApplyLanguage(); UpdateMixPicture(); UpdateMixControls(); };
        Root.SizeChanged += (_, _) => AdaptLayout(); ContentGrid.SizeChanged += (_, _) => AdaptMixLayout(); AdaptLayout();
        if (Environment.GetCommandLineArgs().Contains("--video-source")) Root.Loaded += (_, _) => { Nav.SelectedItem = NavigationItems().First(i => i.Tag?.ToString() == "Media"); };
        if (Environment.GetCommandLineArgs().Contains("--ui-check")) Root.Loaded += async (_, _) => await RunUiCheck();
    }
    private void LanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        engine.Settings.InterfaceLanguage = LanguageChoice.SelectedIndex == 0 ? "ru" : "en";
        engine.SaveSettings(); ApplyLanguage();
    }
    private void ApplyLanguage()
    {
        UI.Localization.Language = engine.Settings.InterfaceLanguage;
        UI.Localization.Apply(Root);
        foreach (var item in NavigationItems())
            item.Content = UI.Localization.T(item.Tag?.ToString() switch { "Live" => "Live transcript", "Models" => "AI Models", "Media" => "Video source", "Library" => "Dictionary", "Today" => "Add text", var name => name ?? "" });
        if (Nav.SettingsItem is NavigationViewItem settings) settings.Content = UI.Localization.T("Settings");
        foreach (var combo in new[] { ThemeChoice, SourceChoice, TopicFilter, PerformanceChoice, MediaKindChoice }) { var template = combo.ItemTemplate; combo.ItemTemplate = null; combo.ItemTemplate = template; }
        windows.LocalizeMenu(); Vm.Refresh(); ShowPage(); UpdateStatus(); UpdateMixPicture(); UpdateMixControls();
    }
    private void InitializeSettings()
    {
        var s = engine.Settings; LanguageChoice.SelectedIndex = s.InterfaceLanguage == "ru" ? 0 : 1; ThemeChoice.SelectedItem = s.Theme; ApplyTheme(s.Theme); SourceChoice.SelectedIndex = (int)s.Source; MicrophoneChoice.Items.Add(new DeviceChoice(null, engine.Settings.InterfaceLanguage == "ru" ? "Микрофон по умолчанию" : "Default microphone"));
        try { foreach (var d in engine.Audio.Microphones()) MicrophoneChoice.Items.Add(new DeviceChoice(d.Id, d.Name)); } catch (Exception e) { Vm.Notice = e.Message; }
        MicrophoneChoice.SelectedItem = MicrophoneChoice.Items.Cast<DeviceChoice>().FirstOrDefault(x => x.Id == s.MicrophoneId) ?? MicrophoneChoice.Items[0];
        AutoCaptureToggle.IsOn = s.AutoCapture; TrayToggle.IsOn = s.MinimizeToTray; StartupToggle.IsOn = WindowsIntegration.StartupEnabled; GamingToggle.IsOn = s.GamingMode; IdleToggle.IsOn = s.OnlyWhenIdle; RetentionBox.Value = s.Retention; ReviewLimitBox.Value = s.ReviewsPerDay; ThreadBox.Value = s.Threads; PerformanceChoice.SelectedItem = s.Performance;
        foreach (var v in speaker.Voices) VoiceChoice.Items.Add(v); VoiceChoice.SelectedItem = s.Voice ?? speaker.Voices.FirstOrDefault(); VoiceRate.Value = s.SpeechRate;
        ListenKeyBox.Text = s.ListeningHotkey; PrivateKeyBox.Text = s.PrivateHotkey; CaptureKeyBox.Text = s.CaptureHotkey;
        HardwareText.Text = Hardware.Describe();
    }
    private record DeviceChoice(string? Id, string Name) { public override string ToString() => Name; }
    private void AdaptLayout() { bool narrow = Root.ActualWidth < 1000; Nav.IsPaneOpen = !narrow; AdaptMixLayout(); ContentGrid.Padding = narrow ? new Thickness(16) : new Thickness(32, 24, 32, 24); BrandText.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible; OfflineLabel.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible; DetailsPane.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible; DetailColumn.Width = narrow ? new GridLength(0) : new GridLength(2, GridUnitType.Star); ListeningStatusText.MaxWidth = narrow ? 200 : 340; }
    private async void DetailsClick(object sender, RoutedEventArgs e) => await new ContentDialog { XamlRoot = Root.XamlRoot, Title = Vm.Selected?.Target ?? "Encounters", Content = new ScrollViewer { Content = new TextBlock { Text = Vm.Details, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true } }, CloseButtonText = "Close" }.ShowAsync();
    private async void NeedsReviewClick(object sender, RoutedEventArgs e)
    {
        var entries = engine.Db.RecentTranscripts().Where(t => t.State != "done").ToList();
        var d = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "Processing queue & needs review", Content = new ScrollViewer { MaxHeight = 420, Content = new TextBlock { Text = entries.Count == 0 ? "No pending transcripts." : string.Join("\n\n", entries.Select(t => $"{t.State}\n{t.Text}\n{t.Error}")), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true } }, PrimaryButtonText = "Retry failed", CloseButtonText = "Close" }; if (await d.ShowAsync() == ContentDialogResult.Primary) { engine.Db.RetryFailed(); Vm.Refresh(); }
    }
    private void NavigationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args) { if (Vm is null || (args.SelectedItem as NavigationViewItem)?.Tag?.ToString() == "Tools") return; Vm.Page = args.IsSettingsSelected ? "Settings" : (args.SelectedItem as NavigationViewItem)?.Tag?.ToString() ?? "Today"; }
    private void ShowPage()
    {
        PageHeadingStrip.Visibility = Vm.Page == "Mix" ? Visibility.Collapsed : Visibility.Visible; if (Vm.Page == "Mix") NoticeBar.IsOpen = false; var activeNavigation = NavigationItems().FirstOrDefault(i => i.Tag?.ToString() == Vm.Page); if (activeNavigation is not null && !ReferenceEquals(Nav.SelectedItem, activeNavigation)) Nav.SelectedItem = activeNavigation; PageTitle.Text = UI.Localization.T(Vm.Page == "Today" ? "Add text" : Vm.Page == "Library" ? "Dictionary" : Vm.Page); MixPanel.Visibility = Vm.Page == "Mix" ? Visibility.Visible : Visibility.Collapsed; UpdateMixControls(); TodayPanel.Visibility = Vm.Page == "Today" ? Visibility.Visible : Visibility.Collapsed; BrowsePanel.Visibility = Vm.Page is "Inbox" or "Library" or "My Life" ? Visibility.Visible : Visibility.Collapsed; ReviewPanel.Visibility = Vm.Page == "Review" ? Visibility.Visible : Visibility.Collapsed; ProgressPanel.Visibility = Vm.Page == "Progress" ? Visibility.Visible : Visibility.Collapsed; LivePanel.Visibility = Vm.Page == "Live" ? Visibility.Visible : Visibility.Collapsed; ModelsPanel.Visibility = Vm.Page == "Models" ? Visibility.Visible : Visibility.Collapsed; MediaPanel.Visibility = Vm.Page == "Media" ? Visibility.Visible : Visibility.Collapsed; if (Vm.Page == "Media") UpdateMediaPreview(); SettingsPanel.Visibility = Vm.Page == "Settings" ? Visibility.Visible : Visibility.Collapsed; if (Vm.Page == "Models") BuildModelRows(); DispatcherQueue.TryEnqueue(() => UI.Localization.Apply(ContentGrid));
    }
    private void UpdateStatus() { if (Vm.Page == "Media") UpdateMediaPreview(); CaptureSourceText.Text = (engine.Settings.InterfaceLanguage == "ru" ? "Источник: " : "Source: ") + (engine.Settings.Source switch { CaptureSource.Microphone => engine.Settings.InterfaceLanguage == "ru" ? "Микрофон" : "Microphone", CaptureSource.PcAudio => engine.Settings.InterfaceLanguage == "ru" ? "Звук компьютера" : "All PC audio", _ => engine.Settings.InterfaceLanguage == "ru" ? "Микрофон + звук компьютера" : "Microphone + all PC audio" }); QueueStatus.Text = engine.Settings.InterfaceLanguage == "ru" ? $"В очереди: {engine.Db.QueueCount} · пропущено фрагментов звука: {engine.DroppedChunks}" : $"{engine.Db.QueueCount} text fragments queued · {engine.DroppedChunks} audio chunks discarded"; windows.Update(engine.Private ? "Private" : engine.CaptureError is not null ? "Error" : engine.Processing ? (engine.Listening ? "Listening · Processing" : "Processing") : engine.Listening ? "Listening" : engine.HasListened ? "Paused" : "Idle"); }
    private void FilterChanged(object sender, SelectionChangedEventArgs e) { if (Vm is not null) Vm.Filter = TopicFilter.SelectedItem?.ToString() ?? "All topics"; }
    private void AddAccelerator(VirtualKey key, VirtualKeyModifiers modifiers, Action action) { var a = new KeyboardAccelerator { Key = key, Modifiers = modifiers }; a.Invoked += (_, e) => { action(); e.Handled = true; }; Root.KeyboardAccelerators.Add(a); }
    private void KeyPressed(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (Vm.Page is not ("Review" or "Mix")) return;
        if (e.OriginalSource is TextBox or ComboBox or NumberBox) return;
        if (Vm.Page == "Mix" && e.Key == VirtualKey.K) { Vm.KnowMixCommand.Execute(null); e.Handled = true; return; }
        if (Vm.Page == "Mix" && e.Key == VirtualKey.Delete) { Vm.IgnoreMixCommand.Execute(null); e.Handled = true; return; } if (e.Key == VirtualKey.Space) { Vm.ShowAnswerCommand.Execute(null); e.Handled = true; } else if (e.Key >= VirtualKey.Number1 && e.Key <= VirtualKey.Number4) { Vm.Rate((Rating)((int)e.Key - (int)VirtualKey.Number1 + 1)); e.Handled = true; }
    }
    private void RateClick(object sender, RoutedEventArgs e) => Vm.Rate(Enum.Parse<Rating>(((Button)sender).Tag.ToString()!));
    private void SpeakSelectedClick(object sender, RoutedEventArgs e) => Speak(Vm.Selected?.Target);
    private void SpeakReviewClick(object sender, RoutedEventArgs e) => Speak(Vm.ReviewCard?.Target);
    private void Speak(string? text) { if (text is null) return; try { speaker.Speak(text, engine.Settings.Voice, engine.Settings.SpeechRate); } catch (Exception e) { Vm.Notice = e.Message; } }
    private void LoadMoreClick(object sender, RoutedEventArgs e) { foreach (var v in engine.Db.List(Vm.Search, Vm.Page == "Inbox" ? ItemStatus.New : null, topic: Vm.Filter == "All topics" ? null : Vm.Filter, offset: Vm.Items.Count, limit: 500, personalOnly: Vm.Page == "My Life")) Vm.Items.Add(v); }
    private async void ClipboardClick(object sender, RoutedEventArgs e) { try { var c = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent(); if (c.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text)) Vm.QuickText = await c.GetTextAsync(); else Vm.Notice = "Clipboard does not contain text."; } catch (Exception ex) { Vm.Notice = ex.Message; } }
    private FileOpenPicker OpenPicker(params string[] types) { var p = new FileOpenPicker(); WinRT.Interop.InitializeWithWindow.Initialize(p, WinRT.Interop.WindowNative.GetWindowHandle(this)); foreach (var t in types) p.FileTypeFilter.Add(t); return p; }
    private FileSavePicker SavePicker(string name, string label, string extension) { var p = new FileSavePicker { SuggestedFileName = name }; WinRT.Interop.InitializeWithWindow.Initialize(p, WinRT.Interop.WindowNative.GetWindowHandle(this)); p.FileTypeChoices.Add(label, new List<string> { extension }); return p; }
    private async void ImportClick(object sender, RoutedEventArgs e) { try { var f = await OpenPicker(".txt", ".csv", ".json", ".srt", ".vtt").PickSingleFileAsync(); if (f is null) return; int n = await Task.Run(() => engine.Import(f.Path)); Vm.Notice = $"{n} fragments imported for local analysis."; Vm.Refresh(); } catch (Exception ex) { Vm.Notice = ex.Message; } }
    private void ApplyTheme(string theme) => Root.RequestedTheme = theme == "Light" ? ElementTheme.Light : theme == "Dark" ? ElementTheme.Dark : ElementTheme.Default;
    private void ThemeChanged(object sender, SelectionChangedEventArgs e) { if (!ready) return; engine.Settings.Theme = ThemeChoice.SelectedItem.ToString()!; ApplyTheme(engine.Settings.Theme); engine.SaveSettings(); }
    private void SourceChanged(object sender, SelectionChangedEventArgs e) { if (!ready) return; bool restart = engine.Listening; engine.Stop(); engine.Settings.Source = (CaptureSource)SourceChoice.SelectedIndex; engine.SaveSettings(); if (restart) Vm.ToggleListeningCommand.Execute(null); }
    private void MicrophoneChanged(object sender, SelectionChangedEventArgs e) { if (!ready) return; bool restart = engine.Listening; engine.Stop(); engine.Settings.MicrophoneId = (MicrophoneChoice.SelectedItem as DeviceChoice)?.Id; engine.SaveSettings(); if (restart) Vm.ToggleListeningCommand.Execute(null); }
    private void SettingToggled(object sender, RoutedEventArgs e) { if (!ready) return; var s = engine.Settings; s.AutoCapture = AutoCaptureToggle.IsOn; engine.Audio.AutoCapture = s.AutoCapture; s.MinimizeToTray = TrayToggle.IsOn; s.GamingMode = GamingToggle.IsOn; s.OnlyWhenIdle = IdleToggle.IsOn; engine.SaveSettings(); }
    private void StartupToggled(object sender, RoutedEventArgs e) { if (!ready) return; try { WindowsIntegration.SetStartup(StartupToggle.IsOn); } catch (Exception ex) { Vm.Notice = ex.Message; } }
    private void NumberChanged(NumberBox sender, NumberBoxValueChangedEventArgs e) { if (!ready || double.IsNaN(sender.Value)) return; var s = engine.Settings; s.Retention = Math.Clamp(RetentionBox.Value, .7, .97); s.ReviewsPerDay = (int)Math.Clamp(ReviewLimitBox.Value, 1, 500); s.Threads = (int)Math.Clamp(ThreadBox.Value, 1, Environment.ProcessorCount); engine.SaveSettings(); }
    private void VoiceChanged(object sender, SelectionChangedEventArgs e) { if (!ready) return; engine.Settings.Voice = VoiceChoice.SelectedItem?.ToString(); engine.SaveSettings(); }
    private void VoiceRateChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e) { if (!ready) return; engine.Settings.SpeechRate = (int)VoiceRate.Value; engine.SaveSettings(); }
    private void PerformanceChanged(object sender, SelectionChangedEventArgs e) { if (!ready) return; engine.Settings.Performance = PerformanceChoice.SelectedItem.ToString()!; engine.SaveSettings(); }
    private void ApplyHotkeysClick(object sender, RoutedEventArgs e) { var s = engine.Settings; var old = (s.ListeningHotkey, s.PrivateHotkey, s.CaptureHotkey); try { s.ListeningHotkey = ListenKeyBox.Text; s.PrivateHotkey = PrivateKeyBox.Text; s.CaptureHotkey = CaptureKeyBox.Text; windows.SetHotkeys(s); engine.SaveSettings(); Vm.Notice = "Global shortcuts updated."; } catch (Exception ex) { (s.ListeningHotkey, s.PrivateHotkey, s.CaptureHotkey) = old; try { windows.SetHotkeys(s); } catch { } Vm.Notice = ex.Message; } }
    private void DataFolderClick(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo("explorer.exe", DataPaths.Root) { UseShellExecute = true });
    private async void ExportClick(object sender, RoutedEventArgs e) { try { var f = await SavePicker("EnglishMemory-export", "JSON", ".json").PickSaveFileAsync(); if (f is null) return; await Task.Run(() => engine.Db.Export(f.Path, engine.Settings)); Vm.Notice = "Learning data exported."; } catch (Exception ex) { Vm.Notice = ex.Message; } }
    private async void BackupClick(object sender, RoutedEventArgs e)
    {
        try { var f = await SavePicker("EnglishMemory-backup", "ZIP", ".zip").PickSaveFileAsync(); if (f is null) return; engine.SaveSettings(); await Task.Run(() => { var temp = Path.Combine(DataPaths.Root, "backup-" + Guid.NewGuid()); Directory.CreateDirectory(temp); try { engine.Db.Backup(Path.Combine(temp, "memory.sqlite")); File.Copy(DataPaths.Settings, Path.Combine(temp, "settings.json")); foreach (var folder in new[] { "Subtitles", "CardPictures" }) { var contentRoot = Path.Combine(DataPaths.Root, folder); if (Directory.Exists(contentRoot)) foreach (var file in Directory.EnumerateFiles(contentRoot, "*", SearchOption.AllDirectories)) { var target = Path.Combine(temp, folder, Path.GetRelativePath(contentRoot, file)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target); } } if (File.Exists(f.Path)) File.Delete(f.Path); ZipFile.CreateFromDirectory(temp, f.Path); } finally { Directory.Delete(temp, true); } }); Vm.Notice = "Backup saved. Models are excluded."; } catch (Exception ex) { Vm.Notice = ex.Message; }
    }
    private async void DeleteDataClick(object sender, RoutedEventArgs e) { var d = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "Delete all learning data?", Content = "This permanently removes phrases, encounters, transcripts and reviews. Your models remain installed.", PrimaryButtonText = "Delete data", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close }; if (await d.ShowAsync() == ContentDialogResult.Primary) { engine.Stop(); engine.Db.DeleteAll(); Vm.Refresh(); } }
    private async void LicensesClick(object sender, RoutedEventArgs e) => await new ContentDialog { XamlRoot = Root.XamlRoot, Title = "Open source licenses", Content = "WinUI / Windows App SDK: MIT\n.NET: MIT\nCommunityToolkit.Mvvm: MIT\nMicrosoft.Data.Sqlite: MIT; SQLite: public domain\nNAudio: MIT\nWhisper.net / whisper.cpp / Whisper models: MIT\nLLamaSharp / llama.cpp: MIT\nQwen3 models: Apache-2.0\nFSRS equations: MIT (open-spaced-repetition)\n\nSee THIRD-PARTY-NOTICES.md in the source distribution for upstream license links. No Oxford dictionary entries are bundled.", CloseButtonText = "Close" }.ShowAsync();
    private void BuildModelRows()
    {
        if (download is not null) return; ModelRows.Children.Clear(); ModelRows.Children.Add(new TextBlock { Text = "Install a multilingual speech model and a language model. Once installed, listening, analysis and reviews work offline.", TextWrapping = TextWrapping.Wrap });
        ModelRows.Children.Add(new TextBlock { Text = engine.Settings.InterfaceLanguage == "ru" ? $"Речь: {Path.GetFileName(engine.Settings.SpeechModel) ?? "не выбрана"}\nАнализ: {Path.GetFileName(engine.Settings.LanguageModel) ?? "не выбрана"}" : $"Speech: {Path.GetFileName(engine.Settings.SpeechModel) ?? "not selected"}\nLanguage: {Path.GetFileName(engine.Settings.LanguageModel) ?? "not selected"}", TextWrapping = TextWrapping.Wrap });
        var verify = new Button { Content = engine.Settings.InterfaceLanguage == "ru" ? "Проверить выбранные модели" : "Test selected models" };
        verify.Click += async (_, _) =>
        {
            verify.IsEnabled = false;
            Vm.Notice = engine.Settings.InterfaceLanguage == "ru" ? "Проверка Whisper и LLM… Первый запуск может занять минуту." : "Testing Whisper and LLM… The first load may take a minute.";
            try
            {
                await Task.Run(() => engine.Ai.ValidateSpeech(CancellationToken.None));
                var result = await Task.Run(() => engine.Ai.Analyze("Я сегодня вообще не выспался.", CancellationToken.None));
                Vm.Notice = (engine.Settings.InterfaceLanguage == "ru" ? "Модели работают. Тест: " : "Models work. Test: ") + result.NaturalEnglish;
            }
            catch (Exception ex) { Vm.Notice = ex.Message; File.AppendAllText(Path.Combine(DataPaths.Root, "diagnostics.log"), ex + Environment.NewLine); }
            finally { verify.IsEnabled = true; }
        };
        ModelRows.Children.Add(verify);
        foreach (var m in ModelCatalog.All)
        {
            var row = new StackPanel { Spacing = 8 }; var label = new TextBlock { Text = m.Name, FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }; row.Children.Add(label); var info = new TextBlock { Text = m.Installed ? $"{(engine.Settings.InterfaceLanguage == "ru" ? "Файл найден" : "File found")} · {new FileInfo(m.LocalPath).Length / 1048576d:F0} MB · {m.License}" : $"{(engine.Settings.InterfaceLanguage == "ru" ? "Не установлена" : "Not installed")} · {m.License}" }; row.Children.Add(info);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; var get = new Button { Content = m.Installed ? "Use model" : "Download" }; var remove = new Button { Content = "Delete", IsEnabled = m.Installed }; actions.Children.Add(get); actions.Children.Add(remove); row.Children.Add(actions); var bar = new ProgressBar { Minimum = 0, Maximum = 100, Visibility = Visibility.Collapsed }; row.Children.Add(bar);
            get.Click += async (_, _) => { try { if (!m.Installed) { download = new(); bar.Visibility = Visibility.Visible; get.IsEnabled = false; var meta = await ModelCatalog.Metadata(m, download.Token); info.Text = $"Downloading {meta.Size / 1048576d:F0} MB · checksum verified on completion"; await ModelCatalog.Download(m, new System.Progress<double>(v => bar.Value = v * 100), download.Token); } SelectModel(m.LocalPath, m.Kind); Vm.Notice = m.Name + " selected."; } catch (OperationCanceledException) { Vm.Notice = "Model download cancelled."; } catch (Exception ex) { Vm.Notice = ex.Message; } finally { download?.Dispose(); download = null; BuildModelRows(); } };
            remove.Click += async (_, _) => { var d = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "Delete model?", Content = m.Name, PrimaryButtonText = "Delete", CloseButtonText = "Cancel" }; if (await d.ShowAsync() != ContentDialogResult.Primary) return; try { engine.Stop(); await engine.Ai.Unload(); if (engine.Settings.SpeechModel == m.LocalPath) engine.Settings.SpeechModel = null; if (engine.Settings.LanguageModel == m.LocalPath) engine.Settings.LanguageModel = null; File.Delete(m.LocalPath); engine.SaveSettings(); BuildModelRows(); } catch (Exception ex) { Vm.Notice = ex.Message; } };
            ModelRows.Children.Add(row); ModelRows.Children.Add(new Border { Height = 1, Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray) });
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; var importSpeech = new Button { Content = "Import speech .bin" }; var importLanguage = new Button { Content = "Import language .gguf" }; var folder = new Button { Content = "Open model folder" }; var cancel = new Button { Content = "Cancel download" }; buttons.Children.Add(importSpeech); buttons.Children.Add(importLanguage); buttons.Children.Add(folder); buttons.Children.Add(cancel);
        importSpeech.Click += async (_, _) => await ImportModel("Speech", ".bin"); importLanguage.Click += async (_, _) => await ImportModel("Language", ".gguf"); folder.Click += (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", DataPaths.Models) { UseShellExecute = true }); cancel.Click += (_, _) => download?.Cancel(); ModelRows.Children.Add(buttons); UI.Localization.Apply(ModelRows);
    }
    private void SelectModel(string path, string kind) { engine.Stop(); if (kind == "Speech") engine.Settings.SpeechModel = path; else engine.Settings.LanguageModel = path; engine.SaveSettings(); }
    private async Task ImportModel(string kind, string extension) { try { var f = await OpenPicker(extension).PickSingleFileAsync(); if (f is null) return; var dest = Path.Combine(DataPaths.Models, f.Name); if (!f.Path.Equals(dest, StringComparison.OrdinalIgnoreCase)) await Task.Run(() => File.Copy(f.Path, dest, true)); SelectModel(dest, kind); BuildModelRows(); Vm.Notice = "Model imported. Compatibility is checked when inference starts."; } catch (Exception e) { Vm.Notice = e.Message; } }
    private async Task Quit() { if (quitting) return; quitting = true; timer.Stop(); download?.Cancel(); await engine.DisposeAsync(); speaker.Dispose(); windows.Dispose(); Close(); }
}
