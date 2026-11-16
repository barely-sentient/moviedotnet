# MovieDotNet

A desktop, non-linear video editor built with [Donkeh](https://barely-sentient.github.io/donkeh-ui)
(real CSS, GPU layout/paint, native desktop host) and FFmpeg.

The UI is entirely C# + CSS: timeline, program monitor, inspector, media bin,
transitions, effects and motion presets. FFmpeg does the media work: probing,
poster frames, filmstrips, waveforms and the final render.

## Features

- **Media bin** with thumbnail posters, duration and resolution, an in-app file
  browser (no native picker needed) and a generated demo project.
- **Zoomable timeline** — tracks for video/overlay/text/audio, filmstrip clips,
  waveform clips, snapping, drag to move, trim handles, split, duplicate, delete,
  per-track mute/hide/lock and undo/redo.
- **Program monitor** — live composite of the playhead using native video/audio
  players plus CSS layers; draggable/resizable transform box, transport,
  timecode, frame export.
- **Inspector** — timing, transform, opacity, audio level, text styling, motion,
  transition, effects, all bound to the selected clip.
- **Transition API** — 30+ transitions mapped to FFmpeg `xfade` names with an
  approximated live preview (fade, dips, wipes, slides, shapes, motion, stylised).
- **Effects API** — brightness, contrast, saturation, blur, monochrome, sepia and
  vignette; each maps to a CSS filter for preview and a filter for export.
- **Motion presets** — enter/exit animations for clips and titles, evaluated from
  the playhead so scrubbing shows the real state.
- **Export** — resolution presets, CRF quality, progress, cancel, ffmpeg log, and
  a filter graph built from the timeline (xfade chain, overlays, drawtext, audio
  mix). A frame snapshot button renders the current frame through the same graph.
- **Projects** — save/open JSON projects (`*.mdproj`) in Documents/MovieDotNet.

## Requirements

- .NET SDK 10.
- **FFmpeg** and **ffprobe** on `PATH` (or next to the app / in `tools/ffmpeg`).
  On Windows: `winget install Gyan.FFmpeg`. The app detects common install
  locations and shows status in the title bar and Project setup panel.
- A GPU with OpenGL 3.3 (Donkeh desktop host).
- LibVLC is shipped with the app through `VideoLAN.LibVLC.Windows` on desktop and
  `VideoLAN.LibVLC.Android` on Android; no system VLC install is required.
- **Android** (optional): the .NET Android workload, an Android SDK and a JDK.
  The project picks up `%LOCALAPPDATA%\Android\Sdk` and `%LOCALAPPDATA%\Android\Jdk`
  automatically when they exist.

## Run

```powershell
dotnet run -f net10.0
```

The project multi-targets desktop and Android, so the framework is required.

Useful development switches:

| Variable | Effect |
| --- | --- |
| `MOVIEDOTNET_DEMO=1` | Build/load the generated demo reel at start-up. |
| `MOVIEDOTNET_PROJECT=<path>` | Open a `.mdproj` at start-up. |
| `MOVIEDOTNET_AUTOPLAY=1` | Start playing after the demo loads. |
| `MOVIEDOTNET_MOBILE=1` | Force the phone shell on the desktop host. |
| `MOVIEDOTNET_UITEST=1\|text\|export\|browser\|transitions` | Select a clip / open a panel for screenshot smoke tests. |
| `MOVIEDOTNET_RENDER=<path>` | Render the demo through the normal export command after loading. |

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

Mobile note: FFmpeg and ffprobe are desktop executables, so importing, analysing
and rendering stay desktop features. Android can open projects, play the
timeline and edit clips/transitions/effects, but the FFmpeg banner shows as
missing until a mobile encoder backend is added.

## Project layout

```
Program.cs                     AppRoot, shell layout, frame clock and store wiring
src/Core
  Models.cs                    Clip, Track, MediaAsset, ProjectDocument
  EditorStore.cs               document + UI state, commands, undo/redo, save/open
  Looks.cs                     transition, effect and animation registries + Motion
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

`RenderGraph.Build` normalises every visual segment to the output size, builds the
base video track as a `concat`/`xfade` chain (gaps become colour segments,
overlaps use the incoming clip's transition), then composites overlay, image and
text tracks with `overlay` + `setpts`/`fade`, and mixes audio with `amix` +
`adelay`. Text uses `drawtext` with a generated `textfile` and a real system font.
The same plan can render a single frame (`-frames:v 1 -update 1`).

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
- Canvas presets change the output size; the preview box keeps a fixed pixel
  height and shows the true aspect ratio.
- The timeline opens fitted to the whole edit; the fit button re-fits after zooming. The manual zoom range is 8–320 px/second and the lane scrolls horizontally when zoomed in.
- Android runs the phone shell; video decoding uses the LibVLC Android natives.

## Building against a local framework checkout

The editor needs `Host.Post`/`Host.FrameTick` (see
[`docs/framework-changes.md`](docs/framework-changes.md)). By default the project
imports the framework at `..\UI Framework\CSS-Engine\Donkeh`. Override it with:

```powershell
dotnet build -p:DonkehRoot="C:\path\to\Donkeh"
```

Once the framework release containing those APIs is published, set
`-p:UseLocalDonkeh=false` to build against the NuGet package instead.
