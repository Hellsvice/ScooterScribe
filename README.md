# ScooterScribe

ScooterScribe is a C#/.NET 10 WPF desktop application for classifying micromobility repair issues from descriptions and suggesting possible repair actions. The project uses ML.NET for multiclass classification and optionally uses Whisper for speech-to-text.

## Repository status / A repó állapota

**Partial source upload / Részleges forrásfeltöltés.** This repository currently contains only the files successfully transferred through the available GitHub connection. **Do not expect it to build or run until the missing files below are restored.**

**Hiányzó fájlok / Missing files from the uploaded project ZIP:**
- `MainWindow.xaml` and `MainWindow.xaml.cs` — WPF interface and its code-behind
- `ScooterScribeServices.cs` and `RepairCatalog.cs` — application services and repair suggestions
- `MLModel1.training.cs` and `MLModel1.mbconfig` — training code/configuration
- `MLModel1.mlnet` — trained ML.NET model
- `RuntimeAssets/MLModel1.kiserleti.mlnet` — experimental model
- `RuntimeAssets/roller_hibak_tanitoadatok.csv` — training dataset
- `ai.md` — project AI documentation

These items are present in the user's original project ZIP, but have **not** all been uploaded here. They are not necessarily excluded for licensing reasons; they were not transferred successfully.

## External voice dependencies / Külső hangfelismerési függőségek

The original project also expects optional local Whisper files that were **not included in the supplied GitHub ZIP**:
- `RuntimeAssets/Release/whisper-cli.exe`
- a compatible `ggml-*.bin` Whisper model

These are excluded from version control by `.gitignore`. Without them, speech input is unavailable. The project can still be developed for typed descriptions after the missing source and ML.NET assets are restored.

## Requirements / Követelmények

- Windows with .NET 10 SDK
- Visual Studio with .NET desktop development workload
- NuGet packages: `NAudio`, `Microsoft.ML`, `Microsoft.ML.LightGbm` (see `WpfApp1.csproj`)

## Build / Fordítás

After restoring the missing files, open `WpfApp1.slnx` in Visual Studio, restore NuGet packages and build. The `WpfApp1.csproj` copies files from `RuntimeAssets` to the output folder.

**Important / Fontos:** This is a partial upload, not a complete release. The source files and model/data files listed above are required for the original application to work.

## Purpose / Cél

ScooterScribe is intended to help beginner micromobility technicians interpret issue descriptions and get an initial repair direction. Suggestions are guidance, not a substitute for proper diagnostics and safety procedures.

A ScooterScribe célja, hogy a kezdő mikromobilitási szerelők a hibaleírások alapján lehetséges hibakategóriát és javítási kiindulópontot kapjanak. A javaslatok nem helyettesítik a szakszerű diagnosztikát.
