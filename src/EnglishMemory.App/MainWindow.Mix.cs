using EnglishMemory.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Security.Cryptography;
using System.Text;
namespace EnglishMemory.App;

public sealed partial class MainWindow
{
    private bool initializingMix;
    private bool RussianMix => engine.Settings.InterfaceLanguage == "ru";
    private IEnumerable<NavigationViewItem> NavigationItems()
    {
        foreach (var item in Nav.MenuItems.Concat(Nav.FooterMenuItems).OfType<NavigationViewItem>())
        {
            yield return item; foreach (var child in item.MenuItems.OfType<NavigationViewItem>()) yield return child;
        }
    }
    private void InitializeMix()
    {
        initializingMix = true;
        MixLevelChoice.SelectedIndex = Math.Clamp(LearningFilter.Level(engine.Settings.MinimumCaptureLevel) - 1, 0, 4);
        MixRatioChoice.SelectedIndex = engine.Settings.MixNewPercent switch { <= 60 => 0, >= 90 => 2, _ => 1 };
        BasicFilterToggle.IsOn = engine.Settings.FilterBasicWords; PicturesToggle.IsOn = engine.Settings.ShowCardPictures;
        MixAtlasImage.Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "learning-scenes.png")));
        initializingMix = false; UpdateMixControls(); UpdateMixPicture();
    }
    private void MixSettingsChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || initializingMix || MixLevelChoice.SelectedIndex < 0 || MixRatioChoice.SelectedIndex < 0) return;
        engine.Settings.MinimumCaptureLevel = new[] { "A1", "A2", "B1", "B2", "C1" }[MixLevelChoice.SelectedIndex];
        engine.Settings.MixNewPercent = new[] { 60, 75, 90 }[MixRatioChoice.SelectedIndex]; engine.SaveSettings(); Vm.ResetMix(); UpdateMixPicture();
    }
    private void MixOptionToggled(object sender, RoutedEventArgs e)
    {
        if (!ready || initializingMix) return;
        engine.Settings.FilterBasicWords = BasicFilterToggle.IsOn; engine.Settings.ShowCardPictures = PicturesToggle.IsOn;
        engine.SaveSettings(); Vm.ResetMix(); UpdateMixPicture();
    }
    private void UpdateMixControls()
    {
        if (Vm is null) return;
        MixRevealButton.Visibility = Vm.HasReviewCard && !Vm.AnswerShown ? Visibility.Visible : Visibility.Collapsed;
        if (Vm.Page == "Mix") UpdateMixPicture();
    }
    private void AdaptMixLayout()
    {
        bool narrow = ContentGrid.ActualWidth < 540; bool compact = ContentGrid.ActualWidth < 750 || Root.ActualHeight < 720;
        MixPictureFrame.Width = compact ? 192 : 256; MixPictureFrame.Height = compact ? 120 : 160; MixPictureScale.ScaleX = MixPictureScale.ScaleY = compact ? .75 : 1;
        CustomCardPicture.Width = MixPictureFrame.Width; CustomCardPicture.Height = MixPictureFrame.Height;
        Grid.SetColumn(MixWordPane, narrow ? 0 : 1); Grid.SetRow(MixWordPane, narrow ? 1 : 0);
        Grid.SetColumnSpan(MixPicturePane, narrow ? 2 : 1); Grid.SetColumnSpan(MixWordPane, narrow ? 2 : 1);
        MixPicturePane.Margin = narrow ? new Thickness(0, 0, 0, 16) : new Thickness(0, 0, 24, 0);
        MixPicturePane.HorizontalAlignment = narrow ? HorizontalAlignment.Center : HorizontalAlignment.Left;
    }
    private static string PictureKey(Vocabulary card) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Database.Normalize(card.Target))));
    private static string? CustomPicture(Vocabulary card)
    {
        var root = Path.Combine(DataPaths.Root, "CardPictures"); if (!Directory.Exists(root)) return null;
        return Directory.EnumerateFiles(root, PictureKey(card) + ".*").FirstOrDefault(p => Path.GetExtension(p).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp");
    }
    private void UpdateMixPicture()
    {
        if (Vm is null || MixPicturePane is null) return;
        var card = Vm.ReviewCard; MixPicturePane.Visibility = card is not null && engine.Settings.ShowCardPictures ? Visibility.Visible : Visibility.Collapsed;
        if (card is null) return;
        var file = CustomPicture(card); CustomCardPicture.Visibility = file is null ? Visibility.Collapsed : Visibility.Visible; MixPictureCanvas.Visibility = file is null ? Visibility.Visible : Visibility.Collapsed;
        if (file is not null)
        {
            CustomCardPicture.Source = new BitmapImage(new Uri(file)); MixPictureLabel.Text = RussianMix ? "Ваша картинка" : "Your picture";
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(CustomCardPicture, card.Target); return;
        }
        var (tile, caption) = CardIllustrations.Resolve(card);
        MixPictureShift.X = -(tile % 4) * 256; MixPictureShift.Y = -(tile / 4) * 160;
        MixPictureLabel.Text = (RussianMix ? "Ассоциация: " : "Association: ") + UI.Localization.T(caption);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(MixAtlasImage, UI.Localization.T(caption));
    }
    private async void ChooseCardPictureClick(object sender, RoutedEventArgs e)
    {
        var card = Vm.ReviewCard; if (card is null) return;
        try
        {
            var file = await OpenPicker(".png", ".jpg", ".jpeg", ".webp").PickSingleFileAsync(); if (file is null) return;
            if (new FileInfo(file.Path).Length > 15 * 1024 * 1024) throw new InvalidDataException(RussianMix ? "Выберите картинку до 15 МБ." : "Choose a picture under 15 MB.");
            // Validate decoding before replacing the existing attachment.
            using (var stream = await file.OpenReadAsync()) { _ = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream); }
            var dir = Path.Combine(DataPaths.Root, "CardPictures"); Directory.CreateDirectory(dir);
            var destination = Path.Combine(dir, PictureKey(card) + Path.GetExtension(file.Name).ToLowerInvariant());
            var staged = destination + ".tmp"; File.Copy(file.Path, staged, true);
            foreach (var old in Directory.EnumerateFiles(dir, PictureKey(card) + ".*").Where(p => p != staged)) File.Delete(old);
            File.Move(staged, destination, true); UpdateMixPicture();
        }
        catch (Exception ex) { Vm.Notice = ex.Message; }
    }
    private void OpenAddClick(object sender, RoutedEventArgs e) { Nav.SelectedItem = Nav.MenuItems[2]; QuickAddBox.Focus(FocusState.Programmatic); }
    private void RateMenuClick(object sender, RoutedEventArgs e) => Vm.Rate(Enum.Parse<Rating>(((MenuFlyoutItem)sender).Tag.ToString()!));
    private void HideBasicClick(object sender, RoutedEventArgs e) => Vm.HideBasicCommand.Execute(null);
    private void MixOptionsClick(object sender, RoutedEventArgs e) { Nav.SelectedItem = Nav.SettingsItem; }
    private void LibraryStateChanged(object sender, SelectionChangedEventArgs e) { if (Vm is null) return; Vm.LibraryState = LibraryStateChoice.SelectedIndex; Vm.Refresh(); }
    private void PhraseSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Vm is null) return;
        if (e.AddedItems.OfType<Vocabulary>().LastOrDefault() is { } latest) Vm.Selected = latest;
        SelectionCountText.Text = (RussianMix ? "Выбрано: " : "Selected: ") + PhraseList.SelectedItems.Count;
    }
    private IEnumerable<long> SelectedIds() => PhraseList.SelectedItems.OfType<Vocabulary>().Select(v => v.Id).ToArray();
    private void LearnSelectedClick(object sender, RoutedEventArgs e) => Vm.ChangeItems(SelectedIds(), ItemStatus.Learning);
    private void KnowSelectedClick(object sender, RoutedEventArgs e) => Vm.ChangeItems(SelectedIds(), ItemStatus.Known);
    private void HideSelectedClick(object sender, RoutedEventArgs e) => Vm.ChangeItems(SelectedIds(), ItemStatus.Suspended);
    private void KnowRowClick(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.DataContext is Vocabulary card) Vm.ChangeItems([card.Id], ItemStatus.Known); }
    private void HideRowClick(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.DataContext is Vocabulary card) Vm.ChangeItems([card.Id], ItemStatus.Suspended); }
}

internal static class CardIllustrations
{
    public static (int Tile, string Caption) Resolve(Vocabulary card)
    {
        var target = Database.Normalize(card.Target);
        foreach (var (terms, tile, label) in new[] {
            (new[]{"decision", "decide", "choice", "choose"}, 0, "Making a decision"),
            (new[]{"figure out", "solve", "puzzle", "understand", "work out"}, 1, "Solving a puzzle"),
            (new[]{"deadline", "hurry", "late", "run out of time"}, 2, "Meeting a deadline"),
            (new[]{"sleep", "tired", "rest", "exhausted"}, 4, "Rest and sleep"),
            (new[]{"care of", "help", "support", "lend a hand"}, 13, "Helping someone"),
            (new[]{"run out", "empty", "used up"}, 14, "Running out"),
            (new[]{"keep in touch", "contact", "call back", "reach out"}, 15, "Keeping in touch") })
            if (terms.Any(term => (" " + target + " ").Contains(" " + term + " ", StringComparison.Ordinal))) return (tile, label);
        return card.Topic switch { "Work" => (5,"Work"), "Development" => (6,"Development"), "Streaming" => (7,"Streaming"), "Games" => (8,"Games"), "Movies" => (9,"Movies"), "Travel" => (10,"Travel"), "Home" => (11,"Home"), "Food" => (12,"Food"), _ => (3,"Conversation") };
    }
}
