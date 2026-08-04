# ASIO Signal Generator

A WPF (.NET 8) desktop app built on **NAudio** for sending test signals (sine,
square, triangle, sawtooth, white noise, pink noise) to any ASIO output
device, on one or several output channels at once, with adjustable dB gain
and a live log.

## Features

- **ASIO device selection** — lists every ASIO driver installed on the
  system (`AsioOut.GetDriverNames()`).
- **Sample rate selection with live validation** — as soon as you pick a
  driver, the app probes it (`AsioOut.IsSampleRateSupported`) against the
  standard rates (44.1 / 48 / 88.2 / 96 / 176.4 / 192 kHz) and greys out /
  disables any rate the driver doesn't support, defaulting to 48 kHz if
  available.
- **32-bit float pipeline** — all generators run as 32-bit IEEE float
  internally; NAudio converts automatically to whatever native bit depth
  the ASIO driver actually uses (ASIO doesn't expose a user-selectable bit
  depth the way WASAPI does — the driver's native format is fixed and
  NAudio's `AsioOut` abstracts the conversion away, so there's nothing
  meaningful to pick there).
- **Waveforms**: Sine, Square, Triangle, Sawtooth, White Noise, Pink Noise.
- **Frequency control** via slider or exact numeric entry (20 Hz – 20 kHz).
- **Multi-channel routing** — check any combination of output channels
  (not just a contiguous pair); the same signal is duplicated to all
  selected channels. Toggling channels works live while playing.
- **Volume in dB** (-60 dB to +6 dB), adjustable live while playing.
- **Start / Stop** transport.
- **Open ASIO Control Panel** — opens the driver's own configuration
  utility (`AsioOut.ShowControlPanel()`), e.g. for ASIO4ALL or an audio
  interface's native control panel.
- **Live scrolling log** of everything that happens (device load, start/stop,
  errors, channel toggles, etc.).

## Requirements

- **Windows** (ASIO and WPF are Windows-only).
- [.NET 8 SDK](https://dotnet.microsoft.com/download) (or newer, adjust
  `TargetFramework` in the `.csproj` if you use a different major version).
- An ASIO driver. If you don't have an audio interface with a native ASIO
  driver, install the free **[ASIO4ALL](https://www.asio4all.org/)** to test
  with your regular sound card.

## Build & Run

### Visual Studio
1. Open `AsioSignalGenerator.sln`.
2. Set `AsioSignalGenerator` as the startup project (it already is, it's
   the only project).
3. Press **F5** (Debug) or **Ctrl+F5** (Run without debugging).
   NuGet will restore the `NAudio` package automatically.

### Command line
```bash
cd AsioSignalGenerator
dotnet restore
dotnet build -c Release
dotnet run -c Release
```

Or produce a self-contained, double-click-able build:
```bash
dotnet publish AsioSignalGenerator -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```
The executable will be under
`AsioSignalGenerator\bin\Release\net8.0-windows\win-x64\publish\`.

## Using the app

1. Pick your ASIO driver from the **ASIO Device** dropdown. The app loads
   it immediately, lists its output channels below, and tests standard
   sample rates against it.
2. Pick a **Sample Rate** — rates the driver doesn't support are shown
   greyed out and can't be selected.
3. (Optional) Click **Control Panel...** to open the driver's own settings
   dialog (buffer size, routing, clock source, etc. — this is the same
   window you'd get from the driver's own control app).
4. Choose a **Waveform** and, for tonal waveforms, set the **Frequency**.
5. Check one or more **Output Channels** to send the signal to.
6. Set the **Gain** in dB.
7. Click **▶ Start**. Click **■ Stop** to stop.
8. Watch the **Log** panel on the right for status and errors.

While playing, you can freely change frequency, gain, and which channels
are active — those update live. Changing the *waveform type* while playing
requires a Stop/Start to take effect (the log will remind you).

## Project layout

```
AsioSignalGenerator/
├── AsioSignalGenerator.sln
└── AsioSignalGenerator/
    ├── AsioSignalGenerator.csproj
    ├── App.xaml / App.xaml.cs
    ├── MainWindow.xaml / MainWindow.xaml.cs      <- UI + ASIO orchestration
    └── Audio/
        ├── SignalGenerators.cs                   <- Sine/Square/Triangle/Sawtooth/White/Pink
        ├── ChannelRoutingSampleProvider.cs        <- routes mono signal to N output channels
        └── FloatSampleToWaveProvider.cs           <- ISampleProvider -> IWaveProvider adapter
```

## Notes / things you might want to extend

- Currently the same generated signal is duplicated to every channel you
  select. If you want **different signals per channel simultaneously**
  (e.g. sine on channel 1, noise on channel 2), duplicate the generator +
  volume chain per channel and feed each into a multi-channel mixer — the
  `ChannelRoutingSampleProvider` class is a good starting point to extend.
- Buffer size / ASIO latency is controlled entirely by the driver's own
  control panel (step 2 above) — NAudio's `AsioOut` uses whatever the
  driver is currently configured for.
- Pink noise uses the common Paul Kellet "economy" filter approximation.
