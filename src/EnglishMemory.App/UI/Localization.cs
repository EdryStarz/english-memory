using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
namespace EnglishMemory.App.UI;

internal static class Localization
{
    public static string Language { get; set; } = "ru";
    private static readonly Dictionary<string, string> Russian = new()
    {
        ["Mix"]="Микс", ["Dictionary"]="Словарь", ["Add text"]="Добавить", ["Tools"]="Инструменты",
        ["Words from audio"]="Слова из озвучки", ["New cards"]="Новые карточки", ["Show translation · Space"]="Показать перевод · Пробел", ["3 · Remembered"]="3 · Помню", ["Not needed"]="Не нужно", ["Picture…"]="Картинка…", ["Hide basic / invalid words"]="Убрать базовые / ошибочные", ["Undo"]="Отменить", ["Mix options"]="Настройки микса", ["Mix settings"]="B1+ / микс",
        ["New words arrive automatically. Add a phrase, import subtitles or start listening. Scheduled reviews will also return here."]="Новые слова появляются автоматически. Добавьте фразу, импортируйте субтитры или включите прослушивание. Слова к повторению тоже появятся здесь.",
        ["Add to mix"]="Добавить в микс", ["New phrases appear in the mix automatically."]="Новые фразы автоматически появятся в миксе.",
        ["To learn"]="Для обучения", ["All words"]="Все слова", ["Known words"]="Известные", ["Hidden words"]="Убранные", ["Learn selected"]="Учить выбранные", ["Know selected"]="Знаю выбранные", ["Hide selected"]="Убрать выбранные",
        ["Skip basic words even if AI labels them B1"]="Пропускать базовые слова, даже если ИИ ставит им B1", ["Pictures on cards"]="Картинки в карточках",
        ["Making a decision"]="принять решение", ["Solving a puzzle"]="разобраться", ["Meeting a deadline"]="успеть к сроку", ["Rest and sleep"]="отдохнуть и поспать", ["Helping someone"]="помочь другому", ["Running out"]="запасы заканчиваются", ["Keeping in touch"]="оставаться на связи",
        ["Media"]="Источник видео", ["Video source"]="Источник видео",
        ["Choose video filename"]="Выбрать видеофайл (только название)",
        ["Attach a film, series or episode to new encounters. This applies to PC Audio, imported subtitles and quotes added here."]="Привяжите фильм, сериал или серию к новым встречам. Метка применяется к звуку ПК, импорту субтитров и репликам, добавленным здесь.",
        ["Save video source context"]="Сохранять контекст видео", ["Read player or browser window caption"]="Брать название из окна плеера или браузера", ["Player or browser window"]="Окно плеера или браузера", ["Open windows"]="Открытые окна", ["Refresh windows"]="Обновить список", ["Use selected window"]="Выбрать окно", ["Use foreground window"]="Использовать активное окно",
        ["A window caption is a suggestion. All PC audio may contain other apps; it does not prove the film is the audio source. A browser caption follows the active tab of the selected window."]="Название окна — подсказка. В звук ПК могут попадать другие приложения, поэтому метка не доказывает источник звука. Для браузера используется название активной вкладки выбранного окна.",
        ["Manual source · takes priority"]="Ручное название · имеет приоритет", ["Film / series title"]="Название фильма / сериала", ["Leave empty to use window or subtitle filename"]="Оставьте пустым для названия из окна или файла субтитров", ["Content type"]="Тип контента", ["Video / unknown"]="Видео / неизвестно", ["Movie"]="Фильм", ["Series"]="Сериал", ["Animated series"]="Мультсериал", ["Season (optional)"]="Сезон (необязательно)", ["Episode (optional)"]="Серия (необязательно)", ["Episode title (optional)"]="Название серии (необязательно)", ["Save source"]="Сохранить источник", ["Clear manual title"]="Очистить ручное название",
        ["Subtitles for this source"]="Субтитры для этого источника", ["Choose SRT / VTT"]="Выбрать SRT / VTT", ["Detach subtitles"]="Отключить субтитры",
        ["Use subtitles in the spoken language. Unique matching lines can add a subtitle timestamp. Short or repeated lines keep their time unknown. S01E02 and 1x02 in filenames can supply season and episode; no internet lookup is performed."]="Используйте субтитры на языке речи. Для однозначно совпавшей реплики сохраняется таймкод из субтитров. Коротким или повторяющимся репликам время не приписывается. S01E02 и 1x02 в названии позволяют извлечь сезон и серию. Поиск в интернете не выполняется.",
        ["Add a quote from this source"]="Добавить реплику из этого источника", ["Analyze quote with source"]="Обработать реплику с источником", ["Use PC Audio"]="Использовать звук ПК",
        ["System"]="Системная", ["Light"]="Светлая", ["Dark"]="Тёмная", ["Microphone"]="Микрофон", ["PC Audio"]="Звук компьютера", ["Both"]="Оба источника", ["All topics"]="Все темы", ["Conversation"]="Общение", ["Work"]="Работа", ["Development"]="Разработка", ["Streaming"]="Стримы", ["Games"]="Игры", ["Movies"]="Фильмы", ["Travel"]="Путешествия", ["Home"]="Дом", ["Food"]="Еда", ["Eco"]="Экономный", ["Balanced"]="Сбалансированный", ["Fast"]="Быстрый", ["Open"]="Открыть", ["Pause / Resume Listening"]="Пауза / продолжить", ["Capture Last Moment"]="Обработать последние 30 секунд", ["Private Mode"]="Приватный режим", ["Quit"]="Выход", ["Today"]="Сегодня", ["Inbox"]="Входящие", ["Library"]="Библиотека", ["My Life"]="Моя жизнь", ["Progress"]="Прогресс", ["Settings"]="Настройки", ["Models"]="Модели", ["Live"]="Расшифровка", ["Review"]="Повторение",
        ["Live transcript"]="Расшифровка", ["AI Models"]="ИИ-модели", ["Listen / Pause"]="Слушать / Пауза", ["Private mode"]="Приватный режим", ["Your English, remembered."]="Ваш английский — в памяти.", ["Start review"]="Начать повторение", ["Capture last moment"]="Обработать последние 30 секунд",
        ["Quick add"]="Добавить текст", ["A phrase you said, heard or want to remember."]="Фраза, которую вы сказали, услышали или хотите запомнить.", ["Paste English or Russian text…"]="Вставьте текст на русском или английском…", ["Add to Inbox"]="Во входящие", ["From clipboard"]="Из буфера обмена", ["Import file…"]="Импорт файла…", ["Recently encountered"]="Недавние фразы",
        ["Nothing captured yet. Start Listening or add a phrase manually."]="Пока нет записанных фраз. Включите прослушивание или добавьте текст.", ["No phrases here yet. Add text from Today or start Listening."]="Здесь пока нет фраз. Добавьте текст на странице «Сегодня» или включите прослушивание.", ["No account. No API key. Recognition and analysis run on your PC. Audio is kept in RAM and discarded after processing."]="Без аккаунта и API-ключа. Распознавание и анализ выполняются на компьютере. Звук хранится в оперативной памяти и удаляется после обработки.",
        ["Search phrases and real quotes"]="Поиск фраз и цитат", ["Learn"]="Учить", ["Important"]="Важное", ["Ignore"]="Пропустить", ["I know this"]="Я это знаю", ["More"]="Ещё", ["Pronounce"]="Произнести", ["Show encounters"]="Показать контекст", ["Queue & needs review"]="Очередь и ошибки", ["Load 500 more"]="Загрузить ещё 500", ["Say the meaning aloud, then reveal the answer."]="Назовите значение, затем откройте ответ.", ["Show answer · Space"]="Показать ответ · Пробел", ["REAL CONTEXT"]="РЕАЛЬНЫЙ КОНТЕКСТ", ["1 · Again"]="1 · Снова", ["2 · Hard"]="2 · Трудно", ["3 · Good"]="3 · Хорошо", ["4 · Easy"]="4 · Легко",
        ["Live transcript · separate sources · raw audio is not saved"]="Расшифровка · отдельные источники · звук не сохраняется",
        ["Appearance"]="Внешний вид", ["Theme"]="Тема", ["Interface language"]="Язык интерфейса", ["Listening & privacy"]="Прослушивание и приватность", ["Audio source"]="Источник звука", ["Microphone device"]="Микрофон", ["Automatically extract speech"]="Автоматически выделять речь", ["Close window to tray"]="При закрытии сворачивать в трей", ["Start with Windows (recording stays off)"]="Запускать с Windows (без записи)",
        ["Learning"]="Обучение", ["Target retention (0.70–0.97)"]="Целевая запоминаемость (0,70–0,97)", ["Maximum reviews per session"]="Максимум повторений за сессию", ["Installed English voice"]="Английский голос Windows", ["Speech speed"]="Скорость произношения", ["Local AI performance"]="Производительность локального ИИ", ["Processing mode"]="Режим обработки", ["Maximum CPU threads"]="Максимум потоков процессора", ["Gaming mode · pause heavy language processing"]="Игровой режим · приостановить анализ текста", ["Process language only when PC is idle"]="Анализировать текст только при простое компьютера",
        ["Global shortcuts"]="Глобальные горячие клавиши", ["Toggle Listening"]="Включить / выключить прослушивание", ["Toggle Private Mode"]="Включить / выключить приватный режим", ["Apply shortcuts"]="Применить сочетания", ["Your data"]="Ваши данные", ["Open data folder"]="Открыть папку данных", ["Export JSON"]="Экспорт JSON", ["Backup ZIP"]="Резервная копия ZIP", ["Delete all learning data…"]="Удалить данные обучения…", ["Open source licenses"]="Лицензии",
        ["LOCAL · OFFLINE READY AFTER MODEL SETUP"]="ЛОКАЛЬНО · ОФЛАЙН ПОСЛЕ НАСТРОЙКИ МОДЕЛЕЙ", ["Your data lives in LocalAppData\\EnglishMemory. Models are separate from your vocabulary backup."]="Данные находятся в LocalAppData\\EnglishMemory. Модели не входят в резервную копию словаря.", ["CPU backend. GPU acceleration is not enabled in this build. Gaming mode is controlled manually; speech recognition continues."]="Обработка на процессоре. GPU в этой версии не используется. Игровой режим включается вручную; распознавание речи продолжается.",
        ["Install a multilingual speech model and a language model. Once installed, listening, analysis and reviews work offline."]="Выберите Whisper Base и Qwen2.5 1.5B Fast. После установки моделей распознавание, анализ и повторения работают офлайн.", ["Use model"]="Выбрать модель", ["Download"]="Скачать", ["Delete"]="Удалить", ["Cancel"]="Отмена", ["Close"]="Закрыть", ["Import speech .bin"]="Импорт Whisper .bin", ["Import language .gguf"]="Импорт LLM .gguf", ["Open model folder"]="Открыть папку моделей", ["Cancel download"]="Отменить загрузку", ["not selected"]="не выбрана", ["Delete model?"]="Удалить модель?",
        ["Loading speech model…"]="Загрузка и проверка модели речи…", ["Listening started. Speak, then pause to finish a phrase."]="Прослушивание включено. Говорите, затем сделайте паузу для обработки фразы.", ["Select Whisper Base in AI Models before listening."]="Выберите Whisper Base на странице «ИИ-модели».", ["Turn off Private Mode before listening."]="Выключите приватный режим перед прослушиванием.", ["Download or import a speech model in Models before listening."]="Выберите модель речи на странице «ИИ-модели».", ["Text queued for local analysis."]="Текст добавлен в очередь локального анализа.", ["Your review queue is clear."]="На сегодня повторений нет.", ["Select a phrase to see its real encounters."]="Выберите фразу, чтобы увидеть её контекст.", ["Learn a phrase from Inbox to add it to your reviews."]="Выберите «Учить» у фразы во входящих, чтобы добавить её к повторениям."
    };
    public static string T(string text)
    {
        if (Language != "ru") return text;
        if (Russian.TryGetValue(text, out var translated)) return translated;
        foreach (var (en, ru) in new[] { ("Listening could not start: ", "Не удалось включить прослушивание: "), ("Audio capture stopped: ", "Захват звука остановлен: ") })
            if (text.StartsWith(en)) return ru + T(text[en.Length..]);
        return text;
    }
    public static void Apply(DependencyObject root)
    {
        Apply(root, new HashSet<DependencyObject>());
    }
    private static void Apply(DependencyObject root, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(root)) return;
        if (root is Button flyoutButton && flyoutButton.Flyout is Flyout flyout && flyout.Content is not null) Apply(flyout.Content, visited);
        if (root is Button button && button.Flyout is MenuFlyout menu)
            foreach (var item in menu.Items.OfType<MenuFlyoutItem>()) item.Text = Convert(item.Text);
        if (root is TextBlock text && text.ReadLocalValue(TextBlock.TextProperty) is string s) text.Text = Convert(s);
        if (root is ContentControl content && content.Content is string c) content.Content = Convert(c);
        if (root is TextBox box) { box.PlaceholderText = Convert(box.PlaceholderText); if (box.Header is string h) box.Header = Convert(h); }
        if (root is ComboBox combo && combo.Header is string ch) combo.Header = Convert(ch);
        if (root is ToggleSwitch toggle) { if (toggle.Header is string th) toggle.Header = Convert(th); toggle.OnContent = Language == "ru" ? "Вкл." : "On"; toggle.OffContent = Language == "ru" ? "Выкл." : "Off"; }
        if (root is NumberBox number && number.Header is string nh) number.Header = Convert(nh);
        if (root is Slider slider && slider.Header is string sh) slider.Header = Convert(sh);
        // Collapsed and newly opened ScrollViewer content may not have a visual tree yet.
        if (root is Panel panel) foreach (var child in panel.Children) Apply(child, visited);
        if (root is Border border && border.Child is not null) Apply(border.Child, visited);
        if (root is ContentControl control && control.Content is DependencyObject contentRoot) Apply(contentRoot, visited);
        if (root is ItemsControl items) foreach (var item in items.Items.OfType<DependencyObject>()) Apply(item, visited);
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) Apply(VisualTreeHelper.GetChild(root, i), visited);
    }
    private static string Convert(string value)
    {
        var original = Russian.FirstOrDefault(x => x.Value == value).Key ?? value;
        return T(original);
    }
}

public sealed class LocalizedLabelConverter : Microsoft.UI.Xaml.Data.IValueConverter
{
 public object Convert(object value, Type targetType, object parameter, string language) => Localization.T(value?.ToString() ?? "");
 public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
