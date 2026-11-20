using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace WpfApp1;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<Ticket> history = [];
    private readonly ScooterScribeServices services = new(
    Path.Combine(AppContext.BaseDirectory, "RuntimeAssets"));
    private CancellationTokenSource? stopRecording;
    private bool busy;
    private bool categorySuggested;
    private bool updatingForm;
    private bool suggestionUncertain;
    private string repairSuggestion = "";

    public MainWindow()
    {
        InitializeComponent();
        HistoryItems.ItemsSource = history;
        Loaded += Window_Loaded;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await RunAsync(async () =>
        {
            await LoadHistoryAsync(
                "Készen áll. Mondd be a hibát, vagy írd be kézzel.");
        });
    }

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string folder = AppContext.BaseDirectory;
#if DEBUG
            for (DirectoryInfo? candidate = new DirectoryInfo(folder);
                 candidate is not null; candidate = candidate.Parent)
            {
                if (File.Exists(Path.Combine(candidate.FullName, "WpfApp1.csproj")))
                {
                    folder = candidate.FullName;
                    break;
                }
            }
#endif
            Process.Start(new ProcessStartInfo(folder)
            {
                UseShellExecute = true
            })?.Dispose();
        }
        catch (Exception error)
        {
            ShowStatus(error.Message, error: true);
        }
    }

    private async Task LoadHistoryAsync(string status)
    {
        history.Clear();
        RefreshHistory();
        var result = await services.ReadTicketsAsync();
        foreach (Ticket ticket in result.Tickets)
            history.Add(ticket);

        RefreshHistory();
        ShowStatus(result.Skipped == 0 ? status : $"{status} {result.Skipped} hibás JSONL-sor kimaradt az előzményekből.");
    }

    private async void Record_Click(object sender, RoutedEventArgs e)
    {
        if (stopRecording is not null)
        {
            stopRecording.Cancel();
            return;
        }

        if (!CanReplaceDescription())
            return;

        await RunAsync(async () =>
        {
            services.CheckWhisper();
            string path = NewAudioPath();
            try
            {
                await CaptureAsync(path, TimeSpan.FromSeconds(5), "Felvétel folyamatban. Most mondd a hibát.", canStopEarly: true);
                await FillDescriptionAsync(path);
            }
            finally
            {
                DeleteTemporaryAudio(path);
            }
        });
    }

    private async void OpenAudio_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog
        {
            Title = "Válassz egy hangfájlt",
            InitialDirectory = AppContext.BaseDirectory,
            Filter = "Hangfájlok (*.wav;*.mp3;*.flac;*.ogg)|*.wav;*.mp3;*.flac;*.ogg",
            CheckFileExists = true
        };

        if (picker.ShowDialog(this) != true)
            return;

        if (!CanReplaceDescription())
            return;

        string selectedFile = picker.FileName;

        await RunAsync(async () =>
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = selectedFile,
                    UseShellExecute = true
                })?.Dispose();
            }
            catch (Exception error)
            {
                MessageBox.Show(
                    this,
                    $"A lejátszó megnyitása sikertelen.\n{error.Message}",
                    "Hangfájl megnyitása",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            await FillDescriptionAsync(selectedFile);
        });
    }

    private async Task FillDescriptionAsync(string path)
    {
        ShowStatus("A Whisper átírja a felvételt…");
        string text = await services.TranscribeAsync(path);
        if (string.IsNullOrWhiteSpace(text))
        {
            ShowStatus("Nem sikerült beszédet felismerni. Próbáld újra, vagy írd be a hibát.", error: true);
            return;
        }

        updatingForm = true;
        DescriptionBox.Text = text;
        updatingForm = false;
        SetCategory("", suggested: false);
        await UpdateCategoryAsync();
    }

    private async void Suggest_Click(object sender, RoutedEventArgs e) => await RunAsync(UpdateCategoryAsync);

    private async Task UpdateCategoryAsync()
    {
        string description = DescriptionBox.Text.Trim();
        if (description.Length == 0)
            return;

        SetCategory("", suggested: false);
        ShowStatus("Kategória és javítási javaslat keresése…");
        var result = await Task.Run(() =>
        {
            var prediction = services.Predict(description);
            string repair = string.IsNullOrWhiteSpace(prediction.Category)
                ? "" : GetRepairSuggestion(description, prediction.Category);
            return (Category: prediction.Category, Uncertain: prediction.Uncertain, Repair: repair);
        });
        if (string.IsNullOrWhiteSpace(result.Category))
        {
            ShowStatus("Nem érkezett kategóriajavaslat. Pontosítsd a leírást, vagy add meg kézzel a kategóriát.");
            return;
        }

        SetCategory(result.Category, suggested: true,
            uncertain: result.Uncertain, repair: result.Repair);
        ShowStatus(result.Uncertain
            ? "Ellenőrzést igénylő javaslat. Pontosítsd a leírást, vagy ellenőrzés után hagyd jóvá a kategóriát."
            : "Ellenőrizd a hibaleírást és a javasolt kategóriát, majd mentsd a hibajegyet.");
    }

    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (!HasCompleteDraft)
            return;

        await RunAsync(async () =>
        {
            services.CheckWhisper();
            string path = NewAudioPath();
            try
            {
                await CaptureAsync(path, TimeSpan.FromSeconds(3), "Most mondd: igen vagy nem.", canStopEarly: false);
                ShowStatus("A válasz felismerése…");
                string answer = (await services.TranscribeAsync(path)).Trim().Trim('.', ',', '!', '?', '-', ' ')
                    .ToLowerInvariant();
                if (answer is "igen" or "igyn")
                {
                    if (suggestionUncertain)
                        ReviewCheckBox.IsChecked = true;
                    await SaveTicketAsync();
                }
                else
                    ShowStatus(answer == "nem"
                        ? "Javíthatod a hibajegy adatait, majd újra jóváhagyhatod."
                        : "A válasz bizonytalan. Ismételd meg, vagy használd a mentés gombot.");
            }
            finally
            {
                DeleteTemporaryAudio(path);
            }
        });
    }

    private async Task CaptureAsync(string path, TimeSpan duration, string status, bool canStopEarly)
    {
        using var stop = new CancellationTokenSource();
        if (canStopEarly)
            stopRecording = stop;

        ShowStatus(status);
        UpdateControls();
        try
        {
            await ScooterScribeServices.RecordAsync(path, duration, stop.Token);
        }
        finally
        {
            stopRecording = null;
            UpdateControls();
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (CanSave)
            await RunAsync(SaveTicketAsync);
    }

    private async Task SaveTicketAsync()
    {
        if (!CanSave)
            return;

        Ticket ticket = await services.SaveAsync(DescriptionBox.Text.Trim(), CategoryBox.Text.Trim());
        history.Insert(0, ticket);
        RefreshHistory();
        ClearForm();
        ShowStatus($"Hibajegy elmentve: {services.TicketsPath}");
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (!CanReplaceDescription())
            return;

        ClearForm();
        DescriptionBox.Focus();
        ShowStatus("Új hibajegyet vehetsz fel vagy beírhatod a hibát.");
    }

    private void ClearForm()
    {
        updatingForm = true;
        DescriptionBox.Clear();
        CategoryBox.Clear();
        updatingForm = false;
        ResetSuggestion();
        UpdateControls();
    }

    private void ResetSuggestion()
    {
        categorySuggested = false;
        suggestionUncertain = false;
        repairSuggestion = "";
        if (ReviewCheckBox is not null)
            ReviewCheckBox.IsChecked = false;
    }

    private void SetCategory(string category, bool suggested, bool uncertain = false, string repair = "")
    {
        updatingForm = true;
        CategoryBox.Text = category;
        updatingForm = false;
        categorySuggested = suggested;
        suggestionUncertain = suggested && uncertain;
        repairSuggestion = suggested ? repair : "";
        ReviewCheckBox.IsChecked = false;
        UpdateControls();
    }

    private void Description_Changed(object sender, TextChangedEventArgs e)
    {
        if (!updatingForm)
        {
            if (categorySuggested)
            {
                updatingForm = true;
                CategoryBox.Clear();
                updatingForm = false;
                ShowStatus("A leírás változott. Kérj új kategóriajavaslatot, vagy add meg kézzel.");
            }
            ResetSuggestion();
        }
        UpdateControls();
    }

    private void Category_Changed(object sender, TextChangedEventArgs e)
    {
        if (!updatingForm)
            ResetSuggestion();
        UpdateControls();
    }

    private void Review_Changed(object sender, RoutedEventArgs e) => UpdateControls();

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string folder = Path.GetDirectoryName(services.TicketsPath)!;
            Directory.CreateDirectory(folder);

            Process.Start(new ProcessStartInfo(folder)
            {
                UseShellExecute = true
            })?.Dispose();
        }
        catch (Exception error)
        {
            ShowStatus(error.Message, error: true);
        }
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (busy)
            return;

        busy = true;
        UpdateControls();
        try
        {
            await action();
        }
        catch (Exception error)
        {
            ShowStatus(error.Message, error: true);
        }
        finally
        {
            busy = false;
            UpdateControls();
        }
    }

    private bool HasDescription => !string.IsNullOrWhiteSpace(DescriptionBox.Text);
    private bool HasCategory => !string.IsNullOrWhiteSpace(CategoryBox.Text);
    private bool HasDraft => HasDescription || HasCategory;
    private bool HasCompleteDraft => HasDescription && HasCategory;
    private bool CanSave => HasCompleteDraft &&
        (!suggestionUncertain || ReviewCheckBox?.IsChecked == true);

    private bool CanReplaceDescription() => !HasDraft || MessageBox.Show(this,
        "Az új hibaleírás felülírja a jelenlegi piszkozatot. Folytatod?",
        "Piszkozat cseréje", MessageBoxButton.YesNo, MessageBoxImage.Question,
        MessageBoxResult.No) == MessageBoxResult.Yes;

    private static string GetRepairSuggestion(string description, string category)
    {
        return RepairCatalog.Find(description, category);
    }

    private void UpdateControls()
    {
        if (ConfirmButton is null || Activity is null || RepairPanel is null ||
            RepairText is null || ReviewNotice is null || ReviewCheckBox is null)
            return;

        RecordButton.Content = stopRecording is null ? "Felvétel indítása" : "Felvétel befejezése";
        RecordButton.IsEnabled = !busy || stopRecording is not null;
        Activity.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        AudioButton.IsEnabled = !busy;
        FolderButton.IsEnabled = !busy;
        DescriptionBox.IsReadOnly = busy;
        CategoryBox.IsReadOnly = busy;
        ClearButton.IsEnabled = !busy && HasDraft;
        SuggestButton.IsEnabled = !busy && HasDescription;
        SaveButton.IsEnabled = !busy && CanSave;
        ConfirmButton.IsEnabled = !busy && HasCompleteDraft;
        RepairPanel.Visibility = HasCompleteDraft ? Visibility.Visible : Visibility.Collapsed;
        ReviewNotice.Visibility = suggestionUncertain ? Visibility.Visible : Visibility.Collapsed;
        ReviewCheckBox.Visibility = suggestionUncertain ? Visibility.Visible : Visibility.Collapsed;
        ReviewCheckBox.IsEnabled = !busy;
        RepairPanel.Background = (System.Windows.Media.Brush)FindResource(
            suggestionUncertain ? "ReviewBackground" : "SuggestionBackground");
        RepairText.Text = !HasCompleteDraft ? "" : categorySuggested
            ? repairSuggestion
            : "Kézzel megadott kategória. A javítási javaslathoz kattints a Kategória javaslata gombra; ez felülírhatja a kategóriát.";
    }

    private void RefreshHistory()
    {
        HistoryCount.Text = $"{history.Count} mentett hibajegy";
        HistoryEmpty.Visibility = history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowStatus(string message, bool error = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = error ? System.Windows.Media.Brushes.Firebrick
            : (System.Windows.Media.Brush)FindResource("Muted");
    }

    private static string NewAudioPath() => Path.Combine(Path.GetTempPath(), $"scooterscribe-{Guid.NewGuid():N}.wav");

    private static void DeleteTemporaryAudio(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (busy)
        {
            e.Cancel = true;
            if (stopRecording is not null)
                stopRecording.Cancel();
            ShowStatus("Várd meg a folyamat végét az ablak bezárása előtt.");
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        services.Dispose();
        base.OnClosed(e);
    }
}
