# Troubleshooting

## FFmpeg is not detected

The title bar shows a status dot and **Project setup** shows the pipeline
status. If FFmpeg or ffprobe is missing:

1. Install it:

   ```powershell
   winget install Gyan.FFmpeg
   ```

2. Or place `ffmpeg.exe` and `ffprobe.exe` next to the app, in
   `tools/ffmpeg`, or in `tools/ffmpeg/bin`.
3. Or point the app at an existing binary:

   ```powershell
   $env:MOVIEDOTNET_FFMPEG = "C:\ffmpeg\bin\ffmpeg.exe"
   ```

4. Restart the app or press **Refresh** in the setup panel.

The app searches the application folder, `tools`, the Winget package cache,
`C:\ffmpeg\bin`, `C:\Program Files\ffmpeg\bin` and `PATH`.

## Import fails or a file is missing from the bin

- Confirm the extension is supported. See [Editing](editing.md#media-bin).
- Check the ffmpeg status. Analysis needs both `ffmpeg` and `ffprobe`.
- If the file was moved or deleted after import, the clip cannot render.
  Re-import it and replace the clip.
- A failed analysis is reported in the toast and the render log.

## A camera does not appear or fails immediately

- Camera enumeration uses DirectShow. Virtual cameras that do not expose a
  usable DirectShow video stream will fail.
- Try a physical camera, or use **Screen**/**Window** capture instead.
- Press **Refresh** after plugging in a camera.

## Window capture is blank or shows another window

Window capture records the visible screen region occupied by the window:

- The selected window is brought forward when recording starts.
- If another window covers it, the cover is recorded.
- If it is minimised after selection, the capture fails with a toast.
- Hardware-overlay or DRM windows may capture black. Use **Screen** with the
  content visible instead.

## Render fails

- Open the export dialog and read the log. It shows the FFmpeg error tail.
- Check that every clip's source file still exists.
- Try a lower resolution or a higher CRF if the machine runs out of memory.
- Press **Stop render** to cancel a stuck process; the process tree is killed.
- The render output is `Documents/MovieDotNet/Renders/<name>.mp4`.

## The window has no native frame

That is expected on desktop. The app is frameless and draws its own title bar.
Drag the bar to move; use the **—**, **□/❐** and **×** buttons at the right.
On Android the host restores the system frame automatically.

## Android limitations

FFmpeg and ffprobe are desktop executables. On Android:

- Import, recording, analysis and render are unavailable.
- Projects can be opened, played and edited.
- The title bar and inspector use the phone shell; there are no desktop window
  controls.

## Useful test switches

| Variable | Effect |
| --- | --- |
| `MOVIEDOTNET_DEMO=1` | Load the generated demo at start-up. |
| `MOVIEDOTNET_PROJECT=<path>` | Open a project at start-up. |
| `MOVIEDOTNET_IMPORT_FILE=<path>` | Import one file at start-up. |
| `MOVIEDOTNET_UITEST=...` | Open a panel or select a clip for smoke tests. |
| `MOVIEDOTNET_MOBILE=1` | Force the phone shell on the desktop host. |
| `MOVIEDOTNET_RENDER=<path>` | Render after loading. |
| `MOVIEDOTNET_RECORD_MIC=<seconds>` | Record the default microphone. |
| `MOVIEDOTNET_RECORD_SCREEN=<seconds>` | Record the desktop. |
| `MOVIEDOTNET_RECORD_WINDOW=<seconds>` | Record a visible window. |
| `MOVIEDOTNET_NO_TOAST=1` | Suppress start-up toasts for clean screenshots. |

`dotnet run -f net10.0 -- --screenshot <path>` renders 120 frames and writes a
BMP for visual smoke tests.
