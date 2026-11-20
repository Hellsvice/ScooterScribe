# ScooterScribe

ScooterScribe is a C#/.NET 10 WPF desktop application for classifying micromobility repair issues from typed or spoken descriptions and suggesting possible repair actions.

## Upload status / Feltöltés állapota

**Source code uploaded; model and dataset assets still missing. / A forráskód feltöltve, a modell- és adatfájlok még hiányoznak.**

The following three files from the supplied project ZIP could not be transferred through the available GitHub connection:

| Missing path | Size | Reason |
| --- | ---: | --- |
| `MLModel1.mlnet` | 664,181 bytes | Binary ML.NET model; the available text-file upload action cannot directly read and upload the local binary file |
| `RuntimeAssets/MLModel1.kiserleti.mlnet` | 14,498,473 bytes | Binary ML.NET model; same connector limitation, not a GitHub 100 MB limit |
| `RuntimeAssets/roller_hibak_tanitoadatok.csv` | 703,478 bytes | UTF-8 CSV but too large to reliably transfer from the local ZIP through the available text-only connector workflow |

All three files are present in the original `ScooterScribe_GitHub_ready.zip`. Restore them at the paths above to use the ML.NET models and repair suggestions. The running application specifically requires `RuntimeAssets/MLModel1.kiserleti.mlnet` and `RuntimeAssets/roller_hibak_tanitoadatok.csv`.

## Optional Whisper files / Opcionális Whisper-fájlok

These dependencies were not included in the supplied ZIP and must be obtained separately:
- `RuntimeAssets/Release/whisper-cli.exe`
- `RuntimeAssets/ggml-whisper-base-hungarian-soup-q8_0.bin`

The project ignores the local Whisper model and executable runtime folder. Speech recognition cannot work without these files. Text entry is available once the application and ML.NET assets are restored.

## Requirements / Követelmények

- Windows and .NET 10 SDK
- Visual Studio with .NET desktop development workload
- NuGet packages: NAudio, Microsoft.ML, Microsoft.ML.LightGbm (see `WpfApp1.csproj`)

## Build / Fordítás

1. Restore the three missing files at the exact paths listed above.
2. Open `WpfApp1.slnx` in Visual Studio.
3. Restore NuGet packages and build.
4. For voice input, install the compatible Whisper executable and Hungarian model in the paths above.

The code was uploaded successfully but **a full build/runtime test has not been performed in this environment**.

## English

The project is designed to help newcomers to micromobility repair interpret fault descriptions and find an initial troubleshooting direction. ML.NET classifies the description into a predefined fault code; a CSV catalogue provides the category and potential repair. Whisper.cpp provides optional local speech transcription. The suggested repairs must be checked by a technician.

For transparent AI assistance and evaluation details, see [ai.md](ai.md).

## Magyar

A ScooterScribe kezdő mikromobilitási szerelőknek segít a hibaleírás alapján hibakategóriát és lehetséges javítási kiindulópontot találni. Az ML.NET előre meghatározott hibakódokba sorolja a szöveget, a CSV-katalógus pedig kategóriát és javítási javaslatot ad. A Whisper.cpp opcionálisan helyben alakítja szöveggé a beszédet. A javítási javaslatokat szakembernek ellenőriznie kell.

Az MI-használatról és a modell kiértékeléséről az [ai.md](ai.md) fájlban olvashatsz.
