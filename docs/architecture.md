# Architecture

MovieDotNet is a single .NET app built on the Donkeh UI framework. The app owns
the editor state and all media orchestration; Donkeh owns layout, painting and
input dispatch.

## Project layout

```
Program.cs                         AppRoot, shell layout, frame clock, store wiring
src/Core
  Models.cs                        Clip, Track, MediaAsset, ProjectDocument, LayerBox
  EditorStore.cs                   document + UI state, undo/redo, commands, save/open
  Looks.cs                         transition, effect and animation registries; Motion
  TextPresets.cs                   title presets shared by the rail and timeline drops
  ProjectFiles.cs                  Documents/MovieDotNet folders and path helpers
  Device.cs                        desktop vs phone shell
  EditorContext.cs                 context key for the single store
  Media/
    FfmpegTool.cs                  binary discovery, process runner, ffprobe
    MediaAnalysis.cs               posters, filmstrips, waveforms, cache
    Capture.cs                     gdigrab/dshow recording, window enumeration
    DemoProject.cs                 generated demo reel
    RenderGraph.cs                 timeline -> ffmpeg filter graph
    ExportService.cs               render execution and progress
src/UI
  EditorCommands.cs                background import/capture/render commands
  Format.cs                        time, timecode and number formatting
src/Components
  TitleBar, Window                 custom frameless chrome and window controls
  MediaLibrary                     media, text, audio, record, looks, setup
  Timeline                         ruler, tracks, clips, drag/trim, drop target
  PreviewMonitor                   program canvas, layers, selection, transport
  Inspector                        selected clip or project summary
  ExportDialog, MediaBrowser       render and import modals
  ToastHost, Choice, IconButton    shared UI primitives
src/Styles/theme.css               design tokens, shell, atoms, responsive rules
```

## State and rendering

`EditorStore` is the single source of truth. It raises `Changed`; `App` subscribes
and bumps a revision so Donkeh re-renders the tree. Components read the store
through `EditorContext.Store` and call commands rather than mutating UI state
directly.

- `Edit(action, label)` records one undo snapshot.
- `EditCoalesced(action, label, windowMs)` folds a slider burst into one undo
  step.
- `BeginEdit` / `CommitEdit` / `CancelEdit` wrap pointer gestures that should be
  one undo step.
- Undo and redo store JSON snapshots of the document, capped at 80 entries.
- `EditorUi` holds transient state: selection, playhead, zoom, panels, capture
  settings and drag-over highlights.

Background work (import, analysis, capture, render) runs on the thread pool.
Results are marshalled with `Host.Post` before the store is touched. `Host.Post`
callbacks run on the frame thread before the next render.

## Media pipeline

1. **Probe** — `ffprobe -show_format -show_streams` returns duration, dimensions,
   frame rate, audio presence and codecs.
2. **Derivatives** — FFmpeg writes a poster, a 12-frame filmstrip and a waveform
   into a per-file cache keyed by path, size and write time.
3. **Runtime** — the Program monitor uses Donkeh `VideoPlayer`/`AudioPlayer`
   elements; poster frames stand in until the decoder publishes its first frame.
4. **Render** — `RenderGraph` builds one `-filter_complex` graph for the export
   and single-frame snapshots.

## Render graph

- Tracks are stored top-to-bottom; the bottom-most picture track is the base.
- Visual clips are trimmed, scaled to their frame, made RGBA, faded/rotated and
  overlaid bottom-up.
- Text clips use `drawtext` with escaped text files.
- Transitions are realised by overlapping clips and an `xfade` chain.
- Audio clips are trimmed, volume-adjusted and mixed.
- The result is H.264/AAC MP4 with `yuv420p` and `veryfast`.

## Drag and drop

The app uses the framework's HTML-style drag events:

- `Draggable = true` + `OnDragStart` / `OnDragEnd` on media, audio and text
  presets.
- A timeline capture surface handles `OnDragEnter` / `OnDragOver` / `OnDrop`.
- `DragEvent.LocalX/Y` map the pointer to a time and track; the lane highlights
  and a drop marker shows the landing time.
- Payloads are typed media strings: `application/x-moviedotnet-asset` and
  `application/x-moviedotnet-text-preset`.

See [Framework changes](framework-changes.md#7-html-style-drag-and-drop).

## Frameless window

`WindowChrome` forwards minimise, maximise/restore and close to the active
Donkeh desktop window. The title bar uses `app-region: drag`; interactive
controls use `no-drag`. The Android host ignores the frameless request and keeps
the system window chrome.

See [Window and mobile](window-and-mobile.md).

## Testing hooks

The app exposes environment switches for headless smoke tests:
`MOVIEDOTNET_DEMO`, `MOVIEDOTNET_PROJECT`, `MOVIEDOTNET_IMPORT_FILE`,
`MOVIEDOTNET_UITEST`, `MOVIEDOTNET_MOBILE`, `MOVIEDOTNET_MOBILE_PAGE`,
`MOVIEDOTNET_RENDER`, `MOVIEDOTNET_RECORD_MIC`, `MOVIEDOTNET_RECORD_SCREEN` and
`MOVIEDOTNET_RECORD_WINDOW`. `--screenshot <path>` renders 120 frames and writes
a BMP before exiting.
