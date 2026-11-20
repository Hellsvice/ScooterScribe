using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using NAudio.Wave;

namespace WpfApp1;

internal sealed class ScooterScribeServices : IDisposable
{
    private const string WhisperModel = "ggml-whisper-base-hungarian-soup-q8_0.bin";

    public string ProjectFolder { get; }

    public ScooterScribeServices(string projectFolder)
    {
        ProjectFolder = projectFolder;
        RepairCatalog.Configure(projectFolder);
    }
    public string TicketsPath =>
    Path.Combine(AppContext.BaseDirectory, "hibajegyek.jsonl");

    public void CheckWhisper()
    {
        RequireFile(Path.Combine(ProjectFolder, "Release", "whisper-cli.exe"), "Whisper program");
        RequireFile(Path.Combine(ProjectFolder, WhisperModel), "magyar Whisper-modell");
    }

    public static async Task RecordAsync(string path, TimeSpan maxDuration, CancellationToken stopRecording)
    {
        using var microphone = new WaveInEvent { WaveFormat = new WaveFormat(16000, 16, 1) };
        using var buffer = new MemoryStream();
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using (var writer = new WaveFileWriter(buffer, microphone.WaveFormat))
        {
            microphone.DataAvailable += (_, audio) => writer.Write(audio.Buffer, 0, audio.BytesRecorded);
            microphone.RecordingStopped += (_, result) =>
            {
                if (result.Exception is Exception error)
                    stopped.TrySetException(error);
                else
                    stopped.TrySetResult();
            };

            microphone.StartRecording();
            await Task.WhenAny(stopped.Task, Task.Delay(maxDuration, stopRecording));
            if (!stopped.Task.IsCompleted)
                microphone.StopRecording();

            await stopped.Task;
            if (writer.Length == 0)
                throw new InvalidOperationException("A mikrofonról nem érkezett hang. Ellenőrizd a hangbemenetet.");
        }

        await File.WriteAllBytesAsync(path, buffer.ToArray());
    }

    public async Task<string> TranscribeAsync(string path)
    {
        CheckWhisper();
        string executable = Path.Combine(ProjectFolder, "Release", "whisper-cli.exe");
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (string argument in new[]
                 {
                     "-m", Path.Combine(ProjectFolder, WhisperModel), "-l", "hu", "-f", path, "-nt", "-np"
                 })
            start.ArgumentList.Add(argument);

        using var whisper = Process.Start(start) ?? throw new InvalidOperationException("A Whisper indítása sikertelen.");
        Task<string> output = whisper.StandardOutput.ReadToEndAsync();
        Task<string> errors = whisper.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));

        try
        {
            await whisper.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            if (!whisper.HasExited)
                whisper.Kill(entireProcessTree: true);
            await whisper.WaitForExitAsync();
            await Task.WhenAll(output, errors);
            throw new TimeoutException("A Whisper 3 perc alatt nem fejezte be az átírást.");
        }

        string log = await errors;
        string transcript = (await output).Trim();
        if (whisper.ExitCode != 0)
        {
            string? detail = log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(line => line.Contains("error", StringComparison.OrdinalIgnoreCase));
            throw new InvalidOperationException(detail is null
                ? "A Whisper nem tudta átírni a hangfájlt."
                : $"A Whisper nem tudta átírni a hangfájlt. {detail}");
        }

        return transcript;
    }

    public (string Category, bool Uncertain) Predict(string description)
    {
        RepairPrediction result = RepairCatalog.Predict(description, ProjectFolder);
        return (result.Category, result.Uncertain);
    }

    public async Task<(List<Ticket> Tickets, int Skipped)> ReadTicketsAsync()
    {
        if (!File.Exists(TicketsPath))
            return ([], 0);

        List<Ticket> tickets = [];
        int skipped = 0;
        foreach (string line in await File.ReadAllLinesAsync(TicketsPath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                Ticket? ticket = JsonSerializer.Deserialize<Ticket>(line);
                if (ticket is null || string.IsNullOrWhiteSpace(ticket.Hibaleiras) ||
                    string.IsNullOrWhiteSpace(ticket.Kategoria))
                    skipped++;
                else
                    tickets.Add(ticket);
            }
            catch (JsonException)
            {
                skipped++;
            }
        }

        return (tickets.OrderByDescending(ticket => ticket.Letrehozva).ToList(), skipped);
    }

    public async Task<Ticket> SaveAsync(string description, string category)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(TicketsPath)!);
        var ticket = new Ticket(Guid.NewGuid(), DateTimeOffset.Now, description, category);
        await using var file = new FileStream(TicketsPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        string prefix = "";
        if (file.Length > 0)
        {
            file.Seek(-1, SeekOrigin.End);
            if (file.ReadByte() != '\n')
                prefix = Environment.NewLine;
        }

        byte[] row = Encoding.UTF8.GetBytes(prefix + JsonSerializer.Serialize(ticket) + Environment.NewLine);
        await file.WriteAsync(row);
        await file.FlushAsync();
        return ticket;
    }

    private static void RequireFile(string path, string name)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Hiányzik a következő fájl ({name}): {path}");
    }

    public void Dispose() => RepairCatalog.Release(ProjectFolder);
}

internal sealed record Ticket(Guid Id, DateTimeOffset Letrehozva, string Hibaleiras, string Kategoria)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTime LocalTime => Letrehozva.LocalDateTime;
}
