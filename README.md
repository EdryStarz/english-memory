# English Memory

A Windows application that turns English heard through a microphone or PC
audio into a personal vocabulary library and spaced-repetition practice.

**Status: Prototype, source version 0.1.4.** Includes a native WinUI 3 interface,
local speech recognition and local language-model analysis. Model-generated
translations and CEFR labels need human review.

## Features

- Explicit start/pause controls for microphone, PC audio or both
- Local Whisper transcription and llama.cpp-based language analysis
- Vocabulary cards, a mixed practice feed and FSRS-style review scheduling
- Durable processing queue and SQLite storage
- Manual text entry and TXT, CSV, JSON, SRT and VTT imports
- Subtitle timestamps and optional media context
- JSON export and SQLite backup
- Model download/selection with recovery of existing downloaded model files

## Build and run

Requires Windows 11 x64 and .NET SDK 10.0.401, as specified in `global.json`.
Dependencies are restored from NuGet. The self-contained build includes the
.NET and Windows App SDK runtimes.

```powershell
./Build.ps1
./release/Windows-x64/EnglishMemory.App.exe
```

Alternatively, publish the app without running the build script's checks:

```powershell
dotnet publish src/EnglishMemory.App/EnglishMemory.App.csproj -c Release -p:Platform=x64 -o release/Windows-x64
```

In Tools → AI Models, download or select Whisper Base and Qwen2.5 1.5B Instruct
Q4_K_M. Select an audio source in Settings, then press Listen. Starting the app
does not automatically start recording. Models are downloaded separately;
initial downloads require internet access and several GB of disk space.

## Checks

```powershell
dotnet run --project tests/EnglishMemory.Checks -c Release
```

The default checks use temporary data. Optional `--models`, `--audio-probe`,
`--e2e` and `--scale` modes exercise model loading, audio devices and larger
workflows. Model/audio modes may download models or open audio devices.
Run the application's `--ui-check` only with a separate `ENGLISH_MEMORY_DATA`
directory: it creates diagnostic fixtures in the selected database.

Publication verification: the Windows x64 publish completed and all 74 default
checks passed. Live microphone capture and model inference were not rerun for
this source publication.

## Architecture

- `src/EnglishMemory.App`: WinUI 3 views, view models and desktop integration
- `src/EnglishMemory.Core`: audio capture, models, queue, SQLite and review logic
- `tests/EnglishMemory.Checks`: executable behavior checks
- `installer/`: optional WiX installer definition

Stack: C#, .NET 10, WinUI 3, CommunityToolkit.Mvvm, NAudio, Whisper.net,
LLamaSharp and Microsoft.Data.Sqlite.

## Privacy and distribution

Personal data lives in `%LOCALAPPDATA%\EnglishMemory`; models are stored in its
`Models` directory. Back up data before moving or removing it. This repository
includes source and UI assets, but no personal vocabulary, recordings, settings,
downloaded models or installers.

An optional MSI can be built with WiX 5:

```powershell
./Build.ps1 -Installer -Wix path/to/wix.exe
```

Review [third-party notices](THIRD-PARTY-NOTICES.md) before distributing binaries.
This prototype does not implement every feature of the original product plan.
