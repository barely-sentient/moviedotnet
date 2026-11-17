# Framework changes used by MovieDotNet

MovieDotNet depends on **`Donkeh.Windowing` 1.0.36** from NuGet. The edits
below landed in that package while building the editor; they are recorded here
because the editor relies on them. Everything else uses the published API.

The app builds against NuGet by default. To work on the framework itself, point
`DonkehRoot` at a `Donkeh` checkout and pass `-p:UseLocalDonkeh=true`.

## 1. `Host.Post` — marshal worker results onto the frame thread

**Problem.** Components and state setters are bound to the render thread, but
media analysis and rendering run on workers. The editor needed a safe way to
publish results.

**Change** (`Donkeh/Hosting/Source/Host.cs`)

```csharp
public static void Post(Action? action);          // ConcurrentQueue<Action>
public static event Action<double>? FrameTick;    // seconds since the last frame
internal static void PumpPostedActions();
internal static void RaiseFrameTick(double delta);
```

`Donkeh/Hosting` grants `InternalsVisibleTo("Donkeh.Windowing")`, and
`DonkehWindow.OnRender` drains the queue before the UI flush. A completion that
changes state is therefore applied in the same frame.

## 2. `OnReady` never fired for `video` / `audio`

**Problem.** `DonkehWindow.SyncVideoElements` computed `known` **before**
calling `Ensure` and only dispatched the ready handler when `!known`. Players
are created on a worker thread, so by the first frame `GetPlayer` was already
non-null and the callback never ran. The preview stayed blank.

**Change** (`Windowing/Source/Video/VideoHost.cs`, `Audio/AudioHost.cs`,
`DonkehWindow.cs`)

- `MarkReady(ElementNode)` records the first frame that observes a player.
- The ready callback is dispatched from that observation exactly once per player.

## 3. `VideoPlayer` could be entered after disposal

**Problem.** A player was disposed on a worker when its element left the tree,
while an effect could still hold a reference for one more commit. `Play()` then
touched a released native handle — `0xC0000005`.

**Change** (`Windowing/Source/Video/VideoPlayer.cs`)

- A gate lock serialises every public member with `Dispose`.
- Members return early when `_disposed` is set; getters return safe defaults.
- Dispose releases handles inside the same lock.

The app also prunes players that are no longer rendered.

## 4. Multi-targeting never worked

**Problem.** `DonkehTargetFrameworks` used a property function that MSBuild
escaped, producing one target named `net10.0;net10.0-android`.

**Change** (`Donkeh/Build/Donkeh.props`)

`DonkehTargetFrameworks` is assembled with plain conditional properties so the
semicolon separator stays unescaped.

## 5. Themeable range slider colours

**Problem.** `PaintRange` hardcoded the track (`#D0D7DE`), fill and thumb
(`#0969DA`) and a white rim. `accent-color` and the input background are ignored
for host-painted sliders.

**Change** (`Donkeh/Windowing/Source/DonkehWindow.cs`)

- `PaintRange` reads `--range-track`, `--range-fill`, `--range-thumb` and
  `--range-rim` from the input's computed style.
- `ReadRangeColor(box, name, fallback)` parses them with `CssColor.ParseOr` and
  falls back to the original colours.
- Values must be concrete colours; the style resolver returns custom-property
  text unsubstituted.

The app declares the palette in `theme.css` and swaps fill/thumb on hover.

## 6. Pill radii clamp to half the box

**Problem.** The rounded-box SDF expects each radius to be at most half the
box's smaller dimension. `border-radius: 999px` was passed through unchanged, so
the distance field was positive over the whole box and pills rendered
transparent.

**Change** (`Donkeh/Windowing/Source/Rendering/GLRenderer.cs`)

- `ClampRadii(rect, radius)` limits every corner to `min(width, height) / 2`.
- Applied to `AddRoundedRect`, `AddBorder`/`AddRoundedSide`, `AddShadow` and
  rounded `PushClip`.
- `PushClip` clamps the already-packed radii with `Vector4.Min`.

## 7. HTML-style drag and drop

**Problem.** The host exposed click, pointer and context-menu events but no drag
lifecycle, so media could not be dragged from the rail to the timeline.

**Change** (`Donkeh/UILab/Source/Drag.cs`, `Props.cs`, `Node.cs`,
`Donkeh/Windowing/Source/DonkehWindow.cs`)

- `Props` gains `Draggable`, `OnDragStart`, `OnDrag`, `OnDragEnd`,
  `OnDragEnter`, `OnDragOver`, `OnDragLeave` and `OnDrop`.
- `DragEvent` and `DataTransfer` mirror the browser objects. `LocalX/Y` are
  relative to the handled element.
- A press on the nearest `draggable="true"` ancestor becomes pending. After a
  four-pixel move, `dragstart` fires; movement dispatches `drag` and
  `dragenter`/`dragover`/`dragleave`; release dispatches `drop`, then `dragend`.
- A missed release is detected from the mouse-button state and ends with
  `dragend` only.
- Starting a drag clears pointer capture, pressed and text-selection state.
- `Donkeh.UILab` grants `InternalsVisibleTo("Donkeh.Windowing")`.

The app uses it for media cards, audio rows and title presets; the timeline
capture surface handles the drop target.

## 8. Window actions for app-drawn title bars

**Problem.** `AppRoot(Frameless = true)` hides the native frame, and
`app-region: drag` moves the window, but there was no public way to minimise,
maximise/restore or close it.

**Change** (`Donkeh/Windowing/Source/WindowChrome.cs`,
`Donkeh/Windowing/Source/DonkehWindow.cs`)

- Public `Donkeh.Windowing.WindowChrome` exposes `Minimize()`,
  `ToggleMaximize()`, `Close()` and `IsMaximized`.
- `DonkehWindow` attaches itself while `RunView` owns the frame loop and detaches
  in `Dispose`.
- Internal `MinimizeWindow`, `ToggleMaximizeWindow`, `CloseWindow` and
  `IsWindowMaximized` forward to the Silk.NET window state.
- Calls are no-ops on hosts without a native window.

The app adds `src/Components/Window` (the `Window` component, `WindowControls`
and CSS) and opts into `Frameless = true`. `app-region: drag`/`no-drag` controls
which parts of the bar move the window.

## 9. Context-menu events

**Problem.** The editor needs Cut/Copy/Paste, duplicate, split and delete menus
for timeline clips, media assets and track headers.

**Change** (`Donkeh/UILab/Source/Props.cs`, `ContextMenu.cs` and
`Donkeh/Windowing/Source/DonkehWindow.cs`)

```csharp
public Action<ContextMenuEvent>? OnContextMenu { get; init; }

public sealed class ContextMenuEvent
{
    public ElementNode Target { get; }        // deepest node under the pointer
    public ElementNode CurrentTarget { get; } // node that owns the handler
    public float X, Y;                        // local to CurrentTarget
    public float WindowX, WindowY;            // window CSS pixels
}
```

Desktop dispatches `contextmenu` on the right-button press. The local framework
also dispatches it after a 500 ms stationary long press on mobile, which is what
Android uses; that trigger needs the local checkout until it is published in a
new package.

## Notes

- Everything else is public framework API: `Provider`, hooks, `Props`,
  `VideoProps`/`VideoHost`, `CanvasProps`, `Stylesheet`, `Image`, `AppRoot`.
- The desktop host's `--screenshot` flag and the app's `MOVIEDOTNET_*` switches
  are what the smoke tests use.
- These changes are part of the `Donkeh.Windowing` 1.0.x line; the current
  package is 1.0.36. For further framework work, build against the local
  checkout with `UseLocalDonkeh=true`; when the change ships, bump
  `DonkehVersion` and return to NuGet.
