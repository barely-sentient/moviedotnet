# Framework changes used by MovieDotNet

These are the edits made to the local Donkeh checkout while building the editor.
They are small and self-contained; everything else uses the published API.

## 1. `Host.Post` — marshal worker results onto the frame thread

**Problem.** A component's hooks and state setters are bound to the renderer
thread, but `Host` exposed no way back from a worker. The editor analyses media
and renders with ffmpeg on background tasks and needs to publish results without
racing the dirty queue.

**Change** (`Donkeh/Hosting/Source/Host.cs`)

```csharp
public static void Post(Action? action);          // ConcurrentQueue<Action>
public static event Action<double>? FrameTick;    // seconds since last frame
internal static void PumpPostedActions();         // drained by the host
internal static void RaiseFrameTick(double delta);
```

`Donkeh/Hosting` now grants `InternalsVisibleTo("Donkeh.Windowing")` so the desktop
host can drain the queue.

**Wiring** (`Donkeh/Windowing/Source/DonkehWindow.cs`, in `OnRender`)

```csharp
BeforeFrame?.Invoke(this, delta);
Host.PumpPostedActions();
Host.RaiseFrameTick(delta);
// … then the UILab flush
```

`Post` callbacks therefore run before the flush, so a completion that changes
state is applied in the same frame. Both entry points are exception-isolated so a
bad handler cannot take down the loop. The browser host (`DomHost`) has no frame
loop yet; it would need the same two calls from its sync loop.

## 2. `OnReady` never fired for `video` / `audio`

**Problem.** `DonkehWindow.SyncVideoElements` (and `SyncAudioElements`) computed
`known = _host.GetPlayer(element) is not null` **before** calling `Ensure`, then
dispatched the ready handler only when `!known`. Players are created on a worker
thread, so by the time a frame observes the player `GetPlayer` is already
non-null and the callback was skipped forever. Any app driving a player from
`OnReady` (seek, play, clamp volume) never got control — here it left the preview
permanently blank.

**Change** (`Windowing/Source/Video/VideoHost.cs`, `Audio/AudioHost.cs`)

```csharp
private readonly HashSet<ElementNode> _readySignaled = [];
public bool MarkReady(ElementNode element) { … return _readySignaled.Add(element); }
// RemoveLocked also removes the element from the set
```

and in `DonkehWindow`:

```csharp
if (_videoHost.MarkReady(element) && element.VideoReadyHandler is { } ready)
{
    try { ready.DynamicInvoke(player); } catch { }
}
```

The notification is now tied to "first frame that observes a player" rather than
to an impossible same-frame creation. The element identity is stable across
renders, so it fires exactly once per player.

## 3. `VideoPlayer` could be entered after disposal (native crash)

**Problem.** `DonkehWindow` disposes a video player on a worker thread as soon as
its element leaves the tree. An effect can still hold a reference for one more
commit, and `MediaPlayer.Play()` then calls `libvlc_media_player_get_media` on a
released native handle — `0xC0000005`, not a managed exception. This is what
crashed the editor when playback reached the end of the timeline and the last
clips unmounted.

**Change** (`Windowing/Source/Video/VideoPlayer.cs`)

- A private `_gate` lock now serializes every public member (`Play`, `Pause`,
  `Stop`, `Seek`, `Time`, `Duration`, `Volume`, `IsMuted`, `PlaybackRate`,
  `IsPlaying`) with `Dispose`.
- Each member returns early when `_disposed` is set; getters return safe
  defaults instead of touching the native player.
- `Dispose` sets the flag and releases the handles inside the same lock, so a
  call cannot interleave with teardown.

The app also prunes its player dictionaries to the clips rendered in the current
frame, so it stops calling methods on players that have left the tree.

## 4. Multi-targeting never worked (`DonkehTargetFrameworks`)

**Problem.** `Donkeh.props` built `DonkehTargetFrameworks` with
`$([System.String]::Copy('$(_DonkehTargets)').Trim(';'))`. MSBuild escapes the
semicolons returned by a property function, so the SDK saw one target named
`net10.0;net10.0-android` and multi-target builds failed with `NETSDK1046`.

**Change** (`Donkeh/Build/Donkeh.props`)

`DonkehTargetFrameworks` is now assembled with plain conditional properties, so
the separator stays unescaped. Any app that enables a second host (`donkeh
enable-target android|web`) builds again.

## Notes

- No other framework behaviour had to change. Everything else is public API
  (`Provider`, hooks, `Props`, `VideoProps`/`VideoHost`, `CanvasProps`,
  `Stylesheet`, `Image`, `AppRoot`).
- The desktop host's `--screenshot` flag and the app's `MOVIEDOTNET_UI*`
  switches are what the smoke tests use; no engine support was needed.
- Rendering quirks worked around in app CSS rather than the engine: intrinsic
  height of wrapped grids/flex rows inside fixed-height flex columns, and
  `height: 100vh` on an absolutely positioned overlay inside a flex column. Both
  are avoided with explicit heights / percentage sizes in `theme.css` and the
  component styles.
- If these changes are folded into the framework upstream, the app only needs
  `DonkehRoot` switched back to NuGet; no app code changes.
