using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.VisualBasic.FileIO;

namespace WpfApp1;

internal sealed record RepairPrediction(
    string FaultCode, string Category, string Cause, string Repair,
    bool Uncertain, float BestScore, float Margin);

internal static class RepairCatalog
{
    private const string ModelName = "MLModel1.kiserleti.mlnet";
    private const string CsvName = "roller_hibak_tanitoadatok.csv";
    private const string Fallback = "Pontosítsd a hibát: melyik alkatrészen mi a tünet?";

    private const float MinimumScore = 0.35f;
    private const float MinimumMargin = 0.10f;
    private static readonly object Gate = new();
    private static string assetFolder = Path.Combine(AppContext.BaseDirectory, "RuntimeAssets");
    private static Dictionary<string, CatalogEntry>? entries;
    private static PredictionEngine<FaultInput, FaultOutput>? engine;
    private static string? cachedDescription;
    private static RepairPrediction? cachedPrediction;

    private sealed record CatalogEntry(string Category, string Cause, string Repair);

    public sealed class FaultInput
    {
        public string Hibaleiras { get; set; } = "";
        public string HibaKod { get; set; } = "";
    }

    public sealed class FaultOutput
    {
        [ColumnName("PredictedLabel")]
        public string PredictedLabel { get; set; } = "";
        [ColumnName("Score")]
        public float[] Score { get; set; } = Array.Empty<float>();
    }

    public static void Configure(string projectFolder)
    {
        lock (Gate) ConfigureLocked(projectFolder);
    }

    private static string ResolveFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            throw new ArgumentException("Nincs megadva projektmappa.", nameof(folder));
        string full = Path.GetFullPath(folder);
        string nested = Path.Combine(full, "RuntimeAssets");
        return Directory.Exists(nested) ? nested : full;
    }

    private static void ConfigureLocked(string folder)
    {
        string resolved = ResolveFolder(folder);
        if (string.Equals(assetFolder, resolved, StringComparison.OrdinalIgnoreCase)) return;
        ResetLocked();
        assetFolder = resolved;
    }

    public static void Release(string folder)
    {
        lock (Gate)
        {
            if (string.Equals(assetFolder, ResolveFolder(folder), StringComparison.OrdinalIgnoreCase))
                ResetLocked();
        }
    }

    private static void ResetLocked()
    {
        engine?.Dispose();
        engine = null;
        entries = null;
        cachedDescription = null;
        cachedPrediction = null;
    }

    public static RepairPrediction Predict(string description, string projectFolder)
    {
        lock (Gate)
        {
            ConfigureLocked(projectFolder);
            return PredictLocked(description);
        }
    }

    public static string Find(string description, string category)
    {
        if (string.IsNullOrWhiteSpace(description)) return Fallback;
        try
        {
            lock (Gate)
            {
                var result = PredictLocked(description);
                if (string.IsNullOrWhiteSpace(category))
                    return "Előbb kérj kategóriajavaslatot a hibaleíráshoz.";
                if (Normalize(category) != Normalize(result.Category))
                    return "A megadott kategória eltér a modell javaslatától. Ellenőrizd a leírást és kérj új kategóriajavaslatot. Ehhez a kézi kategóriához nincs ellenőrzött javítási találat.";

                if (IsClarification(result.FaultCode, result.Category))
                    return result.Cause + Environment.NewLine + result.Repair;

                string details = "Lehetséges hiba: " + result.Cause + Environment.NewLine +
                                 "Javasolt javítás: " + result.Repair;
                return result.Uncertain
                    ? "Bizonytalan találat – ellenőrzést igényel." + Environment.NewLine + details
                    : details;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or
                                  MalformedLineException or InvalidDataException or
                                  InvalidOperationException or ArgumentException)
        {
            return "A javítási javaslat nem készíthető el. " + e.Message;
        }
    }

    private static RepairPrediction PredictLocked(string description)
    {
        description = (description ?? "").Trim();
        if (description.Length == 0)
            return new RepairPrediction("PONTOSITAS_SZUKSEGES", "Pontosítás szükséges",
                "Hiányzó hibaleírás", Fallback, true, 0, 0);
        if (cachedDescription == description && cachedPrediction is not null)
            return cachedPrediction;

        EnsureLoadedLocked();
        FaultOutput prediction = engine!.Predict(new FaultInput { Hibaleiras = description });
        string code = prediction.PredictedLabel.Trim();
        if (!entries!.TryGetValue(code, out var entry))
            throw new InvalidDataException($"A modell által jósolt hibakód ({code}) nincs a CSV-ben. A modellhez tartozó, legutóbbi CSV-t használd.");

        bool invalidScores = prediction.Score.Length < 2 ||
            prediction.Score.Any(s => !float.IsFinite(s) || s < 0);
        float[] highest = prediction.Score.OrderByDescending(s => s).Take(2).ToArray();
        float best = invalidScores ? 0 : highest[0];
        float margin = invalidScores ? 0 : highest[0] - highest[1];
        bool uncertain = invalidScores || best < MinimumScore || margin < MinimumMargin ||
                         IsClarification(code, entry.Category);
        var result = new RepairPrediction(code, entry.Category, entry.Cause, entry.Repair,
            uncertain, best, margin);
        cachedDescription = description;
        cachedPrediction = result;
        return result;
    }

    private static bool IsClarification(string code, string category) =>
        code is "PONTOSITAS_SZUKSEGES" or "TOBB_HIBA" or "HAJTAS_KIMARADAS" ||
        Normalize(category) == Normalize("Pontosítás szükséges");

    private static void EnsureLoadedLocked()
    {
        if (engine is not null && entries is not null) return;
        string modelPath = Path.Combine(assetFolder, ModelName);
        string csvPath = Path.Combine(assetFolder, CsvName);
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"Hiányzik az új hibakódmodell: {modelPath}. Ellenőrizd a RuntimeAssets fájlok kimeneti mappába másolását is.", modelPath);
        if (!File.Exists(csvPath))
            throw new FileNotFoundException($"Hiányzik a javítási CSV: {csvPath}", csvPath);

        var loadedEntries = LoadEntries(csvPath);
        var ml = new MLContext();
        ITransformer model = ml.Model.Load(modelPath, out DataViewSchema input);
        foreach (string column in new[] { "Hibaleiras", "HibaKod" })
        {
            if (!input.Any(c => c.Name == column && c.Type == TextDataViewType.Instance))
                throw new InvalidDataException($"Az új modellben hiányzó vagy hibás típusú oszlop: {column}. A külön tanító MLModel1.kiserleti.mlnet fájlját másold ide.");
        }
        DataViewSchema output = model.GetOutputSchema(input);
        var score = output.FirstOrDefault(c => c.Name == "Score");
        if (score.Name != "Score") throw new InvalidDataException("A modellnek nincs Score kimenete.");
        if (score.Annotations.Schema.Any(c => c.Name == "SlotNames"))
        {
            VBuffer<ReadOnlyMemory<char>> names = default;
            score.Annotations.GetValue("SlotNames", ref names);
            var missing = names.DenseValues().Select(n => n.ToString())
                .Where(n => !loadedEntries.ContainsKey(n)).Take(5).ToArray();
            if (missing.Length > 0)
                throw new InvalidDataException("A modell és a CSV hibakódjai eltérnek. Hiányzik a CSV-ből: " + string.Join(", ", missing));
        }
        var loadedEngine = ml.Model.CreatePredictionEngine<FaultInput, FaultOutput>(model);
        entries = loadedEntries;
        engine = loadedEngine;
    }

    private static Dictionary<string, CatalogEntry> LoadEntries(string path)
    {
        using var reader = new TextFieldParser(path, Encoding.UTF8, true)
        {
            HasFieldsEnclosedInQuotes = true,
            TextFieldType = FieldType.Delimited,
            TrimWhiteSpace = true
        };
        reader.SetDelimiters(",");
        string[] headers = reader.ReadFields() ?? throw new InvalidDataException("Üres CSV.");
        int Column(string name)
        {
            int index = Array.IndexOf(headers, name);
            if (index < 0) throw new InvalidDataException($"Hiányzó CSV-oszlop: {name}.");
            return index;
        }
        int code = Column("HibaKod"), category = Column("Kategoria"),
            cause = Column("PotencialisHiba"), repair = Column("JavasoltJavitas");
        var loaded = new Dictionary<string, CatalogEntry>(StringComparer.Ordinal);
        while (!reader.EndOfData)
        {
            string[]? row = reader.ReadFields();
            if (row is null || row.Length != headers.Length)
                throw new InvalidDataException("Hibás oszlopszám a katalógusban.");
            string key = row[code].Trim();
            var value = new CatalogEntry(row[category].Trim(), row[cause].Trim(), row[repair].Trim());
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value.Category) ||
                string.IsNullOrWhiteSpace(value.Cause) || string.IsNullOrWhiteSpace(value.Repair))
                throw new InvalidDataException("Üres hibakód, kategória, lehetséges hiba vagy javítás a CSV-ben.");
            if (loaded.TryGetValue(key, out var previous) && previous != value)
                throw new InvalidDataException($"A(z) {key} hibakódhoz eltérő kategóriák vagy javítások tartoznak. A legutóbbi egységesített CSV szükséges.");
            loaded[key] = value;
        }
        if (loaded.Count == 0) throw new InvalidDataException("A katalógusban nincs adatsor.");
        return loaded;
    }

    private static string Normalize(string text) =>
        Regex.Replace((text ?? "").ToLowerInvariant().Normalize(), @"[^\p{L}\p{N}]+", " ").Trim();
}
