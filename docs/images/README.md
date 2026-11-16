# Screenshot assets

The documentation references PNG files in this folder. They are produced from
the current build with the app's screenshot smoke-test switches and a live
window capture for the chrome and drag-and-drop states.

| File | Capture recipe |
| --- | --- |
| `editor-overview.png` | `MOVIEDOTNET_DEMO=1`, full editor, desktop. |
| `empty-state.png` | No demo/project, desktop. |
| `media-bin.png` | Demo loaded, Media rail. |
| `browser.png` | `MOVIEDOTNET_UITEST=browser`. |
| `timeline.png` | Demo loaded, timeline region. |
| `inspector.png` | `MOVIEDOTNET_UITEST=clip`, More controls open. |
| `text-presets.png` | `MOVIEDOTNET_UITEST=textpanel`. |
| `audio.png` | `MOVIEDOTNET_UITEST=audio`. |
| `looks.png` | `MOVIEDOTNET_UITEST=effects`. |
| `record-screen.png` | `MOVIEDOTNET_UITEST=record`, Screen source. |
| `record-window.png` | Record rail, Window source selected. |
| `export.png` | `MOVIEDOTNET_UITEST=export`. |
| `settings.png` | `MOVIEDOTNET_UITEST=settings`. |
| `mobile.png` | `MOVIEDOTNET_MOBILE=1`, preview page. |
| `window-chrome.png` | Live desktop window, frameless title bar visible. |
| `drag-drop.png` | Live drag of a media card over the timeline. |

Use `--screenshot <path>` for deterministic 1600 × 980 frames. Use the live
window for the frameless chrome and drag state so the desktop and window
controls are visible.
