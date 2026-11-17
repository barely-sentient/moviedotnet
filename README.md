# MovieDotNet

A cross-platform, non-linear video editor built with
[Donkeh](https://barely-sentient.github.io/donkeh-ui) (real CSS, GPU
layout/paint, native desktop and Android hosts) and FFmpeg.

The UI is entirely C# + CSS: timeline, program monitor, inspector, media bin and
a Looks workspace for transitions, effects and motion presets. The shell is built
around five rail destinations (Media, Text, Audio, Record, Looks) and keeps
advanced controls behind progressive disclosure, so the first screen stays calm.
FFmpeg does the media work: probing, poster frames, filmstrips, waveforms and the
final render.

## Download

[**Download the latest MovieDotNet release**](https://github.com/barely-sentient/moviedotnet/releases/latest) **— requires .NET 10 Runtime** for desktop.

Releases include Windows x64, Linux x64, macOS Intel, macOS Apple silicon and a
sideloadable Android APK. Install the
[.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) for desktop
builds; FFmpeg is also required. Linux additionally needs the distro `libvlc`
package. See [docs/getting-started.md](docs/getting-started.md).

## Documentation

The full feature guide, capture and export notes, window/mobile behaviour,
architecture and screenshots live in [`docs/`](docs/README.md). Start with
[docs/getting-started.md](docs/getting-started.md) for requirements and the
first-run flow.

## Features

- **Media bin** with thumbnail posters, duration and resolution, an in-app file
  browser (no native picker needed) and a generated demo project. Video assets
  can be added to the base track or straight to an overlay as a
  picture-in-picture layer.
- **Drag and drop** — drag a media asset, an audio row or a title preset from the
  rail onto any timeline lane. The lane highlights, a drop marker shows the
  landing time, and dropping a video on an overlay track creates a
  picture-in-picture layer. HTML-style `OnDragStart`, `OnDragOver`, `OnDrop` and
  related events live in the framework, not just this app.
- **Zoomable timeline** — tracks for video/overlay/text/audio, filmstrip clips,
  waveform clips, snapping, drag to move (including between video and overlay
  tracks), trim handles, split, duplicate, delete, per-track mute/hide/lock and
  undo/redo.
- **Program monitor** — live composite of the playhead using native video/audio
  players plus CSS layers; click a layer to select it, drag it to move and drag
  its edges or corners to resize. Layers resize to fit their transform box, so
  screen capture, webcam and image overlays can be arranged freely.
- **Inspector** — frame, opacity and audio level for the selected clip, with
  timing, rotation, text styling, motion, transition and effects behind a
  **More controls** toggle, plus full-frame and picture-in-picture framing
  presets.
- **Transition API** — 30+ transitions mapped to FFmpeg `xfade` names with an
  approximated live preview (fade, dips, wipes, slides, shapes, motion, stylised).
- **Effects API** — brightness, contrast, saturation, blur, monochrome, sepia and
  vignette; each maps to a CSS filter for preview and a filter for export.
- **Motion presets** — enter/exit animations for clips and titles, evaluated from
  the playhead so scrubbing shows the real state.
- **Capture** — a Record tab for screen, single-window, webcam and microphone
  capture through FFmpeg. Pick a source, press record, and the result is fitted
  to the project canvas (scaled and letterboxed, never cropped) before it lands
  in the bin and timeline. On Windows, screen/window capture uses FFmpeg
  `gdigrab` and webcam/microphone capture uses DirectShow. Window capture brings
  the selected window forward and records its composited screen region; return to
  MovieDotNet to stop.
- **Export** — resolution presets, CRF quality, progress, cancel, ffmpeg log, and
  a filter graph built from the timeline (xfade chain, overlays, drawtext, audio
  mix). A frame snapshot button renders the current frame through the same graph.
- **Frameless window** — the native frame is hidden (`AppRoot(Frameless = true)`)
  and the app draws its own title bar. Drag the bar to move the window; the
  minimise, maximise/restore and close buttons sit at the right. Rounded desktop
  corners square off automatically when maximized. The Android host keeps the
  system frame, and the mobile bar omits these controls.
- **Projects** — save/open JSON projects (`*.mdproj`) in Documents/MovieDotNet.

## Requirements

- .NET SDK 10 for source builds, or the **.NET 10 Runtime** for release builds.
- **FFmpeg** and **ffprobe** on `PATH` (or next to the app / in `tools/ffmpeg`).
  On Windows: `winget install Gyan.FFmpeg`; on macOS: `brew install ffmpeg`; on
  Debian/Ubuntu: `sudo apt install ffmpeg`. The app detects common install
  locations and shows status in the title bar and Project setup panel.
- A GPU with OpenGL 3.3 on desktop.
- **LibVLC** — bundled on Windows and macOS through
  `VideoLAN.LibVLC.Windows` / `VideoLAN.LibVLC.Mac`; install the distro `libvlc`
  package on Linux. Android uses `VideoLAN.LibVLC.Android`.
- **Android** (optional): the .NET Android workload, an Android SDK and a JDK.
  The project picks up `%LOCALAPPDATA%\Android\Sdk` and `%LOCALAPPDATA%\Android\Jdk`
  automatically on Windows.

## Run

```powershell
dotnet run -f net10.0
```

The project references `Donkeh.Windowing` from NuGet and builds desktop by
default. For Android, let the CLI wire the target:

```powershell
donkeh enable-target android
dotnet build -f net10.0-android --no-restore
```

`donkeh enable-target android` enables the target, adds the framework Android
host package, restores both target frameworks and prints the next command.

Useful development switches:

| Variable | Effect |
| --- | --- |
| `MOVIEDOTNET_DEMO=1` | Build/load the generated demo reel at start-up. |
| `MOVIEDOTNET_PROJECT=<path>` | Open a `.mdproj` at start-up. |
| `MOVIEDOTNET_IMPORT_FILE=<path>` | Import a media file at start-up (diagnostic/test helper). |
| `MOVIEDOTNET_AUTOPLAY=1` | Start playing after the demo loads. |
| `MOVIEDOTNET_MOBILE=1` | Force the phone shell on the desktop host. |
| `MOVIEDOTNET_UITEST=1\|clip\|text\|textpanel\|audio\|export\|browser\|transitions\|effects\|animations\|record\|settings` | Select a clip / open a rail panel or dialog for screenshot smoke tests. |
| `MOVIEDOTNET_RENDER=<path>` | Render the demo (or an opened project) through the normal export command after loading. |
| `MOVIEDOTNET_RECORD_MIC=<seconds>` | Record the default microphone for N seconds and ingest it. |
| `MOVIEDOTNET_RECORD_SCREEN=<seconds>` | Record the desktop for N seconds and ingest it. |
| `MOVIEDOTNET_RECORD_WINDOW=<seconds>` | Record a visible window for N seconds and ingest it. |

The generated desktop host also supports `--screenshot <path>` (renders 120
frames, writes a BMP and exits), which is what the smoke tests use.

## Android

```powershell
dotnet build -f net10.0-android
```

The signed APK lands in `bin/Debug/net10.0-android/`. The phone shell shows one
page at a time — **Edit** (preview above the timeline), **Media** and **Look** —
with a bottom navigation bar. The preview canvas is sized in viewport units, the
timeline uses compact track rows and the inspector collapses to a single column.

Mobile note: FFmpeg and ffprobe are desktop executables, so importing, analysing,
recording and rendering stay desktop features. Android can open projects, play
the timeline and edit clips/transitions/effects, but the FFmpeg banner shows as
missing until a mobile media backend is added.

## Project layout

```
Program.cs                     AppRoot, shell layout, frame clock and store wiring
src/Core
  Models.cs                    Clip, Track, MediaAsset, ProjectDocument
  EditorStore.cs               document + UI state, commands, undo/redo, save/open
  Looks.cs                     transition, effect and animation registries + Motion
  TextPresets.cs               title presets shared by the rail and timeline drops
  ProjectFiles.cs              Documents/MovieDotNet folders
  Media/
    FfmpegTool.cs              binary discovery, process runner, ffprobe
    MediaAnalysis.cs           posters, filmstrips, waveforms, cache
    RenderGraph.cs             timeline -> ffmpeg filter graph
    ExportService.cs           render + single-frame snapshot
    DemoProject.cs             generated demo media and project
src/UI
  EditorCommands.cs            import, demo, save/open, render, snapshot, transitions
  Format.cs                    time formatting
src/Components                 one folder per component (code + CSS)
src/Styles/theme.css           tokens, shell chrome and shared controls
```

Components are created with the framework CLI:

```powershell
donkeh new component MyComponent
```

## FFmpeg pipeline

`RenderGraph.Build` fits every visual segment inside its transform box — sources
with a different aspect ratio are scaled down and letterboxed against the canvas
colour instead of being cropped. The bottom-most picture track is the base layer;
tracks above it are composited in order (text last), and base clips can also be
moved/resized so they become part of the composite. The graph builds the base
video track as a `concat`/`xfade` chain (gaps become colour segments, overlaps
use the incoming clip's transition), then composites overlay, image and text
tracks with `overlay` + `setpts`/`fade`, and mixes audio with `amix` + `adelay`.
Text uses `drawtext` with a generated `textfile` and a real system font. The same
plan can render a single frame (`-frames:v 1 -update 1`).

Projects default to **60 fps** (the demo is rendered at 60 too). The canvas frame
rate is chosen in Project setup and the export dialog offers 24/25/30/50/60; the
value flows into the graph as `fps=` filters and `-r`.

## Extending

```csharp
// A new transition: an ffmpeg xfade name plus a preview motion.
TransitionRegistry.Register(new TransitionDefinition(
    "spinout", "Spin Out", "Stylised", "zoomin", "zoomin"));

// A new grading effect; return null to skip the ffmpeg filter.
EffectRegistry.Register(new EffectDefinition(
    "warmth", "Warmth",
    amount => $"sepia({amount:0.##})",
    amount => $"colorbalance=rs={amount * 0.3:0.###}"));
```

Effects are stored per clip as `EffectInstance { Id, Amount }`; animations are
named presets resolved by `Motion.For(clip, clipTime)` in the preview and by
`Motion.AlphaExpression` in the export graph.

## Notes and limits

- Preview playback uses native players per visible clip; export is always the
  authoritative composite. Playback is muted for overlay video tracks.
- Transitions are realised by overlapping clips. Choosing a transition from the
  Inspector or the Transitions panel forms the overlap automatically.
- Per-clip speed changes affect video trimming and audio tempo (0.5×–2× tempo);
  extreme speeds fall back to the untimed audio rate.
- Canvas presets change the output size; clips are resized to fit the canvas
  without cropping, and the preview shows the same letterboxed framing.
- Picture layers stack in timeline order (the top track paints last). Drag a clip
  between video and overlay tracks, or use the layer button in the media bin to
  drop a video into a bottom-right picture-in-picture box; then drag its box in
  the program monitor or use the Inspector presets to place it.
- The timeline opens fitted to the whole edit; the fit button re-fits after zooming. The manual zoom range is 8–320 px/second and the lane scrolls horizontally when zoomed in.
- Android runs the phone shell; video decoding uses the LibVLC Android natives.

## Building against a local framework checkout

The editor references **`Donkeh.Windowing` 1.0.31** from NuGet. The framework
changes it relies on are documented in
[`docs/framework-changes.md`](docs/framework-changes.md).

To work on the framework itself, build against a checkout:

```powershell
dotnet build -p:UseLocalDonkeh=true -p:DonkehRoot="C:\path\to\Donkeh"
```

NuGet is the default; omit both switches for a normal build.
