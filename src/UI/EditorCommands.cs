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
        var list = paths.Where(MediaAnalysis.IsSupported).Distinct().ToList();

        if (list.Count == 0)
        {
            store.Toast("No supported media files selected.");
            return;
        }

        if (!Ffmpeg.Available)
        {
            store.Toast("ffmpeg is required to analyse media. Install it and press Refresh.");
            return;
        }

        store.SetBusy(true, $"Importing {list.Count} file(s)…", 0);

        try
        {
            for (var i = 0; i < list.Count; i++)
            {
                var path = list[i];
                var progress = (i + 1d) / list.Count;
                store.SetProgress(progress, $"Analysing {Path.GetFileName(path)}");

                var asset = await MediaAnalysis.AnalyzeAsync(path);

                if (asset is null)
                {
                    continue;
                }

                var captured = asset;
                Host.Post(() =>
                {
                    store.AddAsset(captured);
                    store.Toast($"Added {captured.Name}");
                });
            }
        }
        finally
        {
            Host.Post(() => store.SetBusy(false));
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

    public static IReadOnlyList<CaptureDevice> VideoDevices { get; private set; } = [];
    public static IReadOnlyList<CaptureDevice> AudioDevices { get; private set; } = [];
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
                VideoDevices = devices.Where(d => d.IsVideo).ToList();
                AudioDevices = devices.Where(d => !d.IsVideo).ToList();
                DevicesLoaded = true;

                if (store.Ui.CameraDevice is null && VideoDevices.Count > 0)
                {
                    store.Ui.CameraDevice = VideoDevices[0].Name;
                }

                if (store.Ui.MicDevice is null && AudioDevices.Count > 0)
                {
                    store.Ui.MicDevice = AudioDevices[0].Name;
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

    public static void RefreshCaptureDevices(EditorStore store)
    {
        _devicesRequested = false;
        DevicesLoaded = false;
        VideoDevices = [];
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

        var video = VideoDevices.FirstOrDefault(d => d.Name == store.Ui.CameraDevice) ?? VideoDevices.FirstOrDefault();

        if (video is null)
        {
            store.Toast("No camera found.");
            return;
        }

        var audio = store.Ui.RecordWithMic
            ? AudioDevices.FirstOrDefault(d => d.Name == store.Ui.MicDevice) ?? AudioDevices.FirstOrDefault()
            : null;

        var path = Path.Combine(
            ProjectFiles.RecordingsDirectory,
            $"webcam-{DateTime.Now:yyyyMMdd-HHmmss}.mp4");

        try
        {
            _cameraRecorder = CaptureRecorder.Start(
                video, audio, store.Ui.CaptureWidth, store.Ui.CaptureHeight, store.Ui.CaptureFps, path);

            store.Toast($"Recording {video.Name}…");
        }
        catch (Exception exception)
        {
            store.Toast($"Camera failed: {exception.Message}");
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
        store.SetBusy(true, "Finishing webcam recording…", 0);

        var ok = await recorder.StopAsync();
        recorder.Dispose();

        if (!ok)
        {
            Host.Post(() =>
            {
                store.SetBusy(false);
                store.Toast("Camera recording failed — no device frames?");
            });

            return;
        }

        await IngestRecordingAsync(store, recorder.OutputPath, "Webcam");
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
            _microphoneRecorder = CaptureRecorder.Start(null, audio, 0, 0, 0, path);
            store.Toast($"Recording {audio.Name}…");
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
}
