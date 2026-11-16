# Getting started

## Requirements

- **.NET SDK 10**.
- **FFmpeg** and **ffprobe** on `PATH`, next to the app, in `tools/ffmpeg`, or in
  the Winget package cache. On Windows:

  ```powershell
  winget install Gyan.FFmpeg
  ```

  The app discovers the Gyan package automatically and shows its status in the
  title bar (green dot) and **Project setup** panel.
- **A GPU with OpenGL 3.3** for the desktop host.
- LibVLC is shipped with the app, so no system VLC install is required.
- Android (optional): .NET Android workload, Android SDK and JDK. The project
  picks up `%LOCALAPPDATA%\Android\Sdk` and `%LOCALAPPDATA%\Android\Jdk`.

## Run

```powershell
dotnet run -f net10.0
```

The app opens frameless with the MovieDotNet title bar. Drag the title bar to
move the window; use the **—**, **□/❐** and **×** buttons at the far right to
minimise, maximise/restore and close.

![MovieDotNet first launch](images/empty-state.png)

## First run

1. **Import media** — press **Import** in the media bin. The in-app browser opens
   over the project; choose one or more files and confirm. FFmpeg analyses each
   file and generates a poster, filmstrip and waveform.
2. **Or load the demo** — press **Demo**. The app generates a short demo reel
   with three scenes, a music bed and titles, then shows it in the bin and
   timeline.
3. **Play** — use the transport under the Program monitor or the play button.
4. **Select a clip** — click it on the timeline or in the Program monitor. The
   inspector switches to that clip.
5. **Make a change** — move or trim the clip, adjust opacity, add a title, apply
   a look, or drop an asset onto the timeline.
6. **Save** — use the floppy icon in the title bar or **Project setup → Save**.

## The first screen

| Region | Purpose |
| --- | --- |
| Title bar | Project name, canvas metadata, undo/redo, open/save, FFmpeg status, settings, Render, window controls. |
| Left rail | Media, Text, Audio, Record and Looks destinations. |
| Left panel | The active destination's content. |
| Program monitor | The live composite, transport, timecode and frame snapshot. |
| Timeline | Tracks, clips, playhead, snapping, zoom and fit. |
| Inspector | Frame/opacity/audio first; **More controls** reveals advanced settings. |

## Opening and saving projects

- Projects are JSON files with the `.mdproj` extension in
  `Documents/MovieDotNet/Projects`.
- The folder icon in the title bar opens the project picker.
- The floppy icon saves to the current path, or asks for a name on first save.
- The project name field in the title bar renames the project. The file is only
  written when you save.

## Next steps

- [Editing](editing.md) — the full workflow.
- [Capture](capture.md) — record a screen, window, camera or microphone.
- [Export](export.md) — render the timeline to MP4.
- [Troubleshooting](troubleshooting.md) — if FFmpeg or capture is not detected.
