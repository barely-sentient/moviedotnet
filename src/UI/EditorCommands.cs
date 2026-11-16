using Donkeh.Hosting;
using MovieDotNet.Core;
using MovieDotNet.Core.Media;

namespace MovieDotNet.UI;

/// <summary>
/// Long-running editor commands. Work happens on the thread pool and results are
/// posted back to the frame thread with <see cref="Host.Post"/> before the store
/// is touched.
/// </summary>
public static class EditorCommands
{
    public static async Task ImportAsync(EditorStore store, IEnumerable<string> paths)
    {
        var list = paths
            .Where(MediaAnalysis.IsSupported)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (list.Count == 0)
        {
            Host.Post(() => store.Toast("No supported media files selected."));
            return;
        }

        if (!Ffmpeg.Available)
        {
            Host.Post(() => store.Toast("ffmpeg is required to analyse media. Install it and press Refresh."));
            return;
        }

        Host.Post(() => store.SetBusy(true, $"Importing {list.Count} file(s)…", 0));
        var imported = 0;
        var failed = new List<string>();

        try
        {
            for (var i = 0; i < list.Count; i++)
            {
                var path = list[i];
                var progress = (i + 1d) / list.Count;
                Host.Post(() => store.SetProgress(progress, $"Analysing {Path.GetFileName(path)}"));

                try
                {
                    var asset = await MediaAnalysis.AnalyzeAsync(path);

                    if (asset is null)
                    {
                        failed.Add(Path.GetFileName(path));
                        continue;
                    }

                    imported++;
                    Host.Post(() =>
                    {
                        store.AddAsset(asset);
                    });
                }
                catch (Exception exception)
                {
                    failed.Add(Path.GetFileName(path));
                    Host.Post(() => store.Ui.Log = $"{path}: {exception.Message}");
                }
            }
        }
        catch (Exception exception)
        {
            Host.Post(() => store.Ui.Log = exception.ToString());
        }
        finally
        {
            Host.Post(() =>
            {
                store.SetBusy(false);

                if (imported > 0)
                {
                    store.Ui.ActiveTab = "media";
                    store.Toast(failed.Count == 0
                        ? $"Imported {imported} file(s)."
                        : $"Imported {imported}; failed {failed.Count}: {string.Join(", ", failed.Take(3))}");
                }
                else
                {
                    store.Toast(failed.Count == 0
                        ? "No media was imported."
                        : $"Could not import: {string.Join(", ", failed.Take(3))}. Check ffmpeg and the file.");
                }

                store.Notify();
            });
        }
    }

    public static async Task LoadDemoAsync(EditorStore store)
    {
        if (!Ffmpeg.Available)
        {
            store.Toast("Install ffmpeg first — the demo is generated with it.");
            return;
        }

        store.SetBusy(true, "Generating demo media…", 0);

        try
        {
            var document = await DemoProject.BuildAsync(label => Host.Post(() => store.SetProgress(store.Ui.BusyProgress, label)));
            Host.Post(() =>
            {
                store.ReplaceDocument(document, "Load demo project");
                store.Ui.Playhead = 1.2;
                store.Ui.Playing = Environment.GetEnvironmentVariable("MOVIEDOTNET_AUTOPLAY") is "1" or "true";
                store.SetBusy(false);

                var mobilePage = Environment.GetEnvironmentVariable("MOVIEDOTNET_MOBILE_PAGE");

                if (!string.IsNullOrWhiteSpace(mobilePage))
                {
                    store.Ui.MobilePage = mobilePage;
                }

                var uiTest = Environment.GetEnvironmentVariable("MOVIEDOTNET_UITEST");

                if (uiTest is "1" or "clip")
                {
                    var clip = document.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Kind == TrackKind.Video);

                    if (clip is not null)
                    {
                        store.Ui.SelectedClipId = clip.Id;
                        store.Ui.SelectedAssetId = clip.AssetId;
                    }
                }
                else if (uiTest == "record")
                {
                    store.Ui.ActiveTab = "record";
                }
                else if (uiTest == "settings")
                {
                    store.Ui.ActiveTab = "settings";
                }
                else if (uiTest == "textpanel")
                {
                    store.Ui.ActiveTab = "text";
                }
                else if (uiTest == "audio")
                {
                    store.Ui.ActiveTab = "audio";
                }
                else if (uiTest == "text")
                {
                    var clip = document.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Kind == TrackKind.Text);

                    if (clip is not null)
                    {
                        store.Ui.SelectedClipId = clip.Id;
                    }
                }
                else if (uiTest == "export")
                {
                    store.Ui.ShowExport = true;
                }
                else if (uiTest == "browser")
                {
                    store.Ui.ShowBrowser = true;
                    store.Ui.BrowserMode = "media";
                }
                else if (uiTest == "transitions")
                {
                    store.Ui.ActiveTab = "transitions";
                    var clip = document.Tracks.SelectMany(t => t.Clips).LastOrDefault(c => c.Kind == TrackKind.Video);
                    store.Ui.SelectedClipId = clip?.Id;
                }
                else if (uiTest is "effects" or "animations")
                {
                    store.Ui.ActiveTab = uiTest;
                    var clip = document.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Kind == TrackKind.Video);
                    store.Ui.SelectedClipId = clip?.Id;
                }

                store.Toast("Demo project ready — press play.");

                // Headless smoke test: render the demo through the same command
                // path the UI uses.
                var renderPath = Environment.GetEnvironmentVariable("MOVIEDOTNET_RENDER");

                if (!string.IsNullOrWhiteSpace(renderPath))
                {
                    RenderAsync(store, new RenderSettings
                    {
                        Width = 960,
                        Height = 540,
                        Fps = document.Fps,
                        Crf = 24,
                        Background = document.Background
                    }, renderPath);
                }
            });
        }
        catch (Exception exception)
        {
            Host.Post(() =>
            {
                store.SetBusy(false);
                store.Toast($"Demo failed: {exception.Message}");
            });
        }
    }

    public static void SaveProject(EditorStore store, string? path = null)
    {
        var target = path ?? ProjectFiles.ProjectPath(store.Document.Name);

        try
        {
            store.Save(target);
            store.Toast($"Saved {Path.GetFileName(target)}");
        }
        catch (Exception exception)
        {
            store.Toast($"Save failed: {exception.Message}");
        }
    }

    public static void OpenProject(EditorStore store, string path)
    {
        try
        {
            var loaded = EditorStore.Load(path);
            store.ReplaceDocument(loaded.Document, "Open project");
            store.SetProjectPath(path);
            store.Ui.ShowBrowser = false;
            store.Ui.SelectedClipId = null;
            store.Toast($"Opened {Path.GetFileName(path)}");
        }
        catch (Exception exception)
        {
            store.Toast($"Open failed: {exception.Message}");
        }
    }

    public static async Task RenderAsync(
        EditorStore store,
        RenderSettings settings,
        string outputPath,
        CancellationToken cancellation = default)
    {
        if (!Ffmpeg.Available)
        {
            store.Toast("ffmpeg is required to render. Install it first.");
            return;
        }

        store.Ui.ShowExport = false;
        store.SetBusy(true, "Rendering…", 0);
        var started = DateTime.Now;

        try
        {
            var (ok, output) = await ExportService.RenderAsync(
                store.Document,
                settings,
                outputPath,
                MediaAnalysis.CacheRoot,
                progress => Host.Post(() => store.SetProgress(progress, $"Rendering… {progress:P0}")),
                line => Host.Post(() => store.Ui.Log = line),
                cancellation);

            Host.Post(() =>
            {
                store.SetBusy(false);

                if (ok)
                {
                    var seconds = (DateTime.Now - started).TotalSeconds;
                    store.Toast($"Rendered {Path.GetFileName(outputPath)} in {seconds:0.0}s");
                    store.Ui.Log = outputPath;
                }
                else
                {
                    store.Toast("Render failed — see the log in the export dialog.");
                    store.Ui.Log = output;
                    store.Ui.ShowExport = true;
                }
            });
        }
        catch (OperationCanceledException)
        {
            Host.Post(() =>
            {
                store.SetBusy(false);
                store.Toast("Render cancelled.");
            });
        }
        catch (Exception exception)
        {
            Host.Post(() =>
            {
                store.SetBusy(false);
                store.Toast($"Render failed: {exception.Message}");
            });
        }
    }

    public static async Task SnapshotAsync(EditorStore store, RenderSettings settings, string outputPath)
    {
        var (ok, message) = await ExportService.SnapshotAsync(
            store.Document,
            settings,
            store.Ui.Playhead,
            outputPath,
            MediaAnalysis.CacheRoot);

        Host.Post(() => store.Toast(ok ? $"Frame saved to {Path.GetFileName(outputPath)}" : $"Frame failed: {message}"));
    }

    /// <summary>Applies a transition to the selected clip and overlaps it with its neighbour.</summary>
    public static void ApplyTransition(EditorStore store, string transitionId, double duration)
    {
        var clip = store.SelectedClip;

        if (clip is null)
        {
            store.Toast("Select a clip on a video track first.");
            return;
        }

        var track = store.Document.TrackOf(clip);

        if (track is null)
        {
            return;
        }

        store.Edit(_ =>
        {
            clip.TransitionId = transitionId;
            clip.TransitionDuration = duration;

            var ordered = track.Clips.OrderBy(c => c.Start).ToList();
            var index = ordered.FindIndex(c => c.Id == clip.Id);

            if (index > 0 && transitionId != "none")
            {
                var previous = ordered[index - 1];
                var overlap = Math.Min(duration, Math.Min(previous.Duration * 0.9, clip.Duration * 0.9));
                var desired = previous.End - overlap;

                if (clip.Start > desired)
                {
                    clip.Start = Math.Max(0, desired);
                }
            }
        }, "Apply transition");

        store.Toast($"{TransitionRegistry.Get(transitionId).Name} applied");
    }

    // --------------------------------------------------------------- capture

    private static CaptureRecorder? _cameraRecorder;
    private static CaptureRecorder? _microphoneRecorder;
    private static bool _devicesRequested;
    private static string _cameraLabel = "Capture";

    public static IReadOnlyList<CaptureDevice> VideoDevices { get; private set; } = [];
    public static IReadOnlyList<CaptureDevice> WindowDevices { get; private set; } = [];
    public static IReadOnlyList<CaptureDevice> ScreenDevices { get; private set; } = [];
    public static IReadOnlyList<CaptureDevice> AudioDevices { get; private set; } = [];
    public static CaptureDevice? ScreenDevice => ScreenDevices.FirstOrDefault();
    public static bool DevicesLoaded { get; private set; }
    public static bool CameraRecording => _cameraRecorder?.IsRecording == true;
    public static bool MicrophoneRecording => _microphoneRecorder?.IsRecording == true;
    public static bool AnyRecording => CameraRecording || MicrophoneRecording;
    public static TimeSpan CameraElapsed => _cameraRecorder?.Elapsed ?? TimeSpan.Zero;
    public static TimeSpan MicrophoneElapsed => _microphoneRecorder?.Elapsed ?? TimeSpan.Zero;

    /// <summary>Loads the capture device list once, then re-renders the tab.</summary>
    public static void EnsureCaptureDevices(EditorStore store)
    {
        if (_devicesRequested)
        {
            return;
        }

        _devicesRequested = true;

        Task.Run(async () =>
        {
            var devices = await CaptureRecorder.ListDevicesAsync();
            Host.Post(() =>
            {
                VideoDevices = devices
                    .Where(d => d.IsVideo && !d.IsScreen && !d.IsWindow)
                    .ToList();
                WindowDevices = devices.Where(d => d.IsWindow).ToList();
                ScreenDevices = devices.Where(d => d.IsScreen).ToList();
                AudioDevices = devices.Where(d => !d.IsVideo).ToList();
                DevicesLoaded = true;

                if (store.Ui.CameraDevice is null && VideoDevices.Count > 0)
                {
                    store.Ui.CameraDevice = VideoDevices[0].Name;
                }

                if (store.Ui.CaptureWindow is null && WindowDevices.Count > 0)
                {
                    store.Ui.CaptureWindow = WindowDevices[0].Name;
                }

                if (store.Ui.MicDevice is null && AudioDevices.Count > 0)
                {
                    store.Ui.MicDevice = AudioDevices[0].Name;
                }

                if (store.Ui.CaptureSource == "camera" && VideoDevices.Count == 0)
                {
                    store.Ui.CaptureSource = "screen";
                }
                else if (store.Ui.CaptureSource == "window" && WindowDevices.Count == 0)
                {
                    store.Ui.CaptureSource = "screen";
                }

                store.Notify();

                // Headless capture smoke test: record the microphone for N seconds.
                var seconds = Environment.GetEnvironmentVariable("MOVIEDOTNET_RECORD_MIC");

                if (int.TryParse(seconds, out var duration) && duration > 0)
                {
                    store.Ui.ActiveTab = "record";
                    StartMicrophone(store);
                    _ = StopAfterAsync(store, duration);
                }

                var screenSeconds = Environment.GetEnvironmentVariable("MOVIEDOTNET_RECORD_SCREEN");

                if (int.TryParse(screenSeconds, out var screenDuration) && screenDuration > 0)
                {
                    store.Ui.ActiveTab = "record";
                    store.Ui.CaptureSource = "screen";
                    store.Ui.RecordWithMic = false;
                    StartCamera(store);
                    _ = StopScreenAfterAsync(store, screenDuration);
                }

                var windowSeconds = Environment.GetEnvironmentVariable("MOVIEDOTNET_RECORD_WINDOW");

                if (int.TryParse(windowSeconds, out var windowDuration) && windowDuration > 0)
                {
                    store.Ui.ActiveTab = "record";
                    store.Ui.CaptureSource = "window";
                    store.Ui.CaptureWindow = WindowDevices
                        .FirstOrDefault(d => d.Name.Contains("MovieDotNet", StringComparison.OrdinalIgnoreCase))?.Name
                        ?? WindowDevices.FirstOrDefault()?.Name;
                    store.Ui.RecordWithMic = false;
                    StartCamera(store);
                    _ = StopScreenAfterAsync(store, windowDuration);
                }
            });
        });
    }

    private static async Task StopAfterAsync(EditorStore store, int seconds)
    {
        await Task.Delay(TimeSpan.FromSeconds(seconds));

        if (MicrophoneRecording)
        {
            await StopMicrophoneAsync(store);
        }
    }

    private static async Task StopScreenAfterAsync(EditorStore store, int seconds)
    {
        await Task.Delay(TimeSpan.FromSeconds(seconds));

        if (CameraRecording)
        {
            await StopCameraAsync(store);
        }
    }

    public static void RefreshCaptureDevices(EditorStore store)
    {
        _devicesRequested = false;
        DevicesLoaded = false;
        VideoDevices = [];
        WindowDevices = [];
        ScreenDevices = [];
        AudioDevices = [];
        EnsureCaptureDevices(store);
    }

    public static void StartCamera(EditorStore store)
    {
        if (CameraRecording)
        {
            return;
        }

        if (!Ffmpeg.Available)
        {
            store.Toast("Install ffmpeg to record.");
            return;
        }

        var source = store.Ui.CaptureSource;
        var video = source switch
        {
            "screen" => ScreenDevice,
            "window" => WindowDevices.FirstOrDefault(d => d.Name == store.Ui.CaptureWindow)
                ?? WindowDevices.FirstOrDefault(),
            _ => VideoDevices.FirstOrDefault(d => d.Name == store.Ui.CameraDevice)
                ?? VideoDevices.FirstOrDefault()
        };

        if (video is null)
        {
            store.Toast(source switch
            {
                "window" => "No window selected. Pick a window and refresh.",
                "camera" => "No camera found.",
                _ => "Screen capture is not available on this host."
            });

            return;
        }

        var audio = store.Ui.RecordWithMic
            ? AudioDevices.FirstOrDefault(d => d.Name == store.Ui.MicDevice) ?? AudioDevices.FirstOrDefault()
            : null;

        // Captures match the project canvas, exactly like OBS records at the
        // canvas resolution. The recorder scales/pads the source to this size.
        var width = Math.Max(2, store.Document.Width);
        var height = Math.Max(2, store.Document.Height);
        var fps = Math.Clamp((int)Math.Round(store.Document.Fps), 24, 60);
        store.Ui.CaptureWidth = width;
        store.Ui.CaptureHeight = height;
        store.Ui.CaptureFps = fps;

        var prefix = video.IsScreen ? "screen" : video.IsWindow ? "window" : "webcam";
        _cameraLabel = video.IsScreen ? "Screen recording" : video.IsWindow ? "Window recording" : "Webcam recording";
        var path = Path.Combine(
            ProjectFiles.RecordingsDirectory,
            $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss}.mp4");

        try
        {
            var recorder = CaptureRecorder.Start(video, audio, width, height, fps, path);
            _cameraRecorder = recorder;
            store.Toast(video.IsScreen
                ? $"Recording the desktop at {width}×{height}…"
                : video.IsWindow
                    ? $"Recording {video.Name} at {width}×{height} — return here to stop."
                    : $"Recording {video.Name} at {width}×{height}…");
            store.Notify();
            _ = WatchCaptureAsync(store, recorder, camera: true);
        }
        catch (Exception exception)
        {
            store.Toast($"Capture failed: {exception.Message}");
        }
    }

    public static async Task StopCameraAsync(EditorStore store)
    {
        var recorder = _cameraRecorder;

        if (recorder is null)
        {
            return;
        }

        _cameraRecorder = null;
        store.SetBusy(true, "Finishing capture…", 0);

        var ok = await recorder.StopAsync();
        recorder.Dispose();

        if (!ok)
        {
            Host.Post(() =>
            {
                store.SetBusy(false);
                store.Toast("Capture failed — no device frames?");
            });

            return;
        }

        await IngestRecordingAsync(store, recorder.OutputPath, _cameraLabel);
    }

    public static void StartMicrophone(EditorStore store)
    {
        if (MicrophoneRecording)
        {
            return;
        }

        if (!Ffmpeg.Available)
        {
            store.Toast("Install ffmpeg to record.");
            return;
        }

        var audio = AudioDevices.FirstOrDefault(d => d.Name == store.Ui.MicDevice) ?? AudioDevices.FirstOrDefault();

        if (audio is null)
        {
            store.Toast("No microphone found.");
            return;
        }

        var path = Path.Combine(
            ProjectFiles.RecordingsDirectory,
            $"voice-{DateTime.Now:yyyyMMdd-HHmmss}.m4a");

        try
        {
            var recorder = CaptureRecorder.Start(null, audio, 0, 0, 0, path);
            _microphoneRecorder = recorder;
            store.Toast($"Recording {audio.Name}…");
            store.Notify();
            _ = WatchCaptureAsync(store, recorder, camera: false);
        }
        catch (Exception exception)
        {
            store.Toast($"Microphone failed: {exception.Message}");
        }
    }

    public static async Task StopMicrophoneAsync(EditorStore store)
    {
        var recorder = _microphoneRecorder;

        if (recorder is null)
        {
            return;
        }

        _microphoneRecorder = null;
        store.SetBusy(true, "Finishing audio recording…", 0);

        var ok = await recorder.StopAsync();
        recorder.Dispose();

        if (!ok)
        {
            Host.Post(() =>
            {
                store.SetBusy(false);
                store.Toast("Audio recording failed.");
            });

            return;
        }

        await IngestRecordingAsync(store, recorder.OutputPath, "Audio");
    }

    private static async Task IngestRecordingAsync(EditorStore store, string path, string label)
    {
        var asset = await MediaAnalysis.AnalyzeAsync(path);

        Host.Post(() =>
        {
            store.SetBusy(false);

            if (asset is null)
            {
                store.Toast($"{label} saved to {path}");
                return;
            }

            store.AddAsset(asset);
            store.Ui.ActiveTab = "media";
            store.Toast($"{label} recording added to the bin.");
        });
    }

    private static async Task WatchCaptureAsync(EditorStore store, CaptureRecorder recorder, bool camera)
    {
        await recorder.Completion;

        Host.Post(() =>
        {
            var current = camera ? _cameraRecorder : _microphoneRecorder;

            // A user stop clears the field before awaiting process shutdown.
            // Only an unexpected FFmpeg exit should clear state and show an error.
            if (!ReferenceEquals(current, recorder))
            {
                return;
            }

            if (camera)
            {
                _cameraRecorder = null;
            }
            else
            {
                _microphoneRecorder = null;
            }

            var log = recorder.Log.Trim();
            recorder.Dispose();
            store.Toast(string.IsNullOrEmpty(log)
                ? "Capture stopped unexpectedly. Check the selected device."
                : $"Capture failed: {log.Split('\n').LastOrDefault()}");
            store.Notify();
        });
    }
}
