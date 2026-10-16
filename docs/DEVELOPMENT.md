# Development and Windows acceptance

## Build and run

Install the [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).

```sh
dotnet build --configuration Release
dotnet test tests/OsrsWorldPing.Core.Tests --configuration Release
```

On Windows:

```sh
dotnet run --project src/OsrsWorldPing.Desktop
dotnet test tests/OsrsWorldPing.Desktop.Tests --configuration Release
```

Publish the framework-dependent single file:

```sh
dotnet publish src/OsrsWorldPing.Desktop --configuration Release -p:PublishProfile=win-x64 --output artifacts/win-x64
```

The output folder must contain only `OsrsWorldPing.exe`. The target machine needs the .NET 10 Desktop Runtime x64. Builds/tests use no game account or external latency API.

The core targets portable `net10.0`. WPF and UI tests target `net10.0-windows`; Linux can cross-compile them with Windows targeting, but cannot run WPF or validate Windows rendering. A successful Linux build is not evidence of visual acceptance.

## Architecture

`MonitoringEngine` owns catalogue discovery, live selection, scan coordination, and cancellation. Replaceable `IWorldCatalogueClient`, `ILatencyProbe`, `ISettingsStore`, and `IClock` keep testing deterministic. ICMP includes a two-second deadline and bounded concurrency (32 by default). Live refresh accepts 5–600 seconds; live and hidden-review deadlines are independent, so a five-minute review never causes an early live poll. Live cycles pause during scans. Hidden worlds are checked every five minutes, not on ordinary live ticks. No missed-tick backlog is queued.

Twenty-five ordinary slots use successful eligible latency, then world number. The core retains session-override compatibility; manual tracking controls are no longer exposed in the UI. Five favorite slots are separate, including unavailable entries. All dashboard projections sort by world number. Card view models are keyed by world ID and updated in place; ordinary readings do not rebuild collections or reset scrolling. The in-window settings view shares the same engine and retains the live grid below all three settings tabs.

`WorldMetadata` derives country, activity, and main-character risk presentation from catalogue data. Location and flag mappings are documented in source with links to RuneLite's public protocol definitions. Unknown codes/activities use neutral fallbacks. Fallback icons and the emblem are original vector artwork; supported activities use genuine OSRS skill/boss sprites with provenance in `Assets/Osrs/SOURCES.md`. The observatory is an original generated background. No artwork is fetched at runtime. Compact world tiles share a template across the dashboard and tracking settings. Tiles are borderless and transparent; inactive tiles fade and recover on hover. Hearts precede world numbers, flags follow latency, and filled silver stars/labels distinguish free worlds. The Show all control reveals the other worlds without probing. Glow/lift and heart animations honor Windows client-area animation preferences, and hover clocks stop when unloaded.

Settings and the cached catalogue are stored in `%LOCALAPPDATA%\OsrsWorldPing`. A missing/legacy schema marker triggers the one-time 60→30 ms migration; the migrated file is atomically saved immediately. Current-schema settings can deliberately use 60 ms. Session overrides and readings are never serialized. Startup imports missing files from the previous OsrsCheckPing directory, preserving that backup and any newer destination files. Readable old settings/cache remain available if import writes fail. Application resets preserve favorites and window placement.

Navigation uses a shared centered header and an animated auto-height reveal. It clips a naturally measured settings section while pushing the existing world grid down, including on tab changes. Gear sparks and brief rotations run only for user navigation and honor Windows animation preferences; reduced-motion and reversal paths clear clocks. The footer reserves scan-progress height independently of visibility. Polling does not change navigation state.

## CI and previews

**Build and test** establishes a 1920×1080 runner desktop before launching WPF tests, runs core tests on Linux and Windows, then verifies single-file publishing and startup/clean exit. WPF constrains windows to the available desktop; changing only the test window size on a smaller runner cannot validate the 1080p baseline. It retains the repeated `Process.Refresh()`/window-handle/title wait that fixed startup detection.

The **Windows-interface-previews** artifact contains deterministic dashboard and all three settings tabs at baseline, compact, and scaled logical sizes. These are actual WPF renders using fixture data; the running app uses real probes. A **Signed release candidate** workflow reuses these checks, then requires the configured protected environment and SignPath approval. See [signing setup](SIGNING.md).

## Windows acceptance gate

Review the actual Windows preview artifact and run these checks before approving a release. Record results against the candidate commit, Windows version, monitor size, scaling, and installed Desktop Runtime in the release review. Generated previews exercise scaled logical layouts; the monitor/DPI checks below still require actual display scaling.

| Check | Expected result |
| --- | --- |
| 1920×1080 at 100%, five favorites + twenty-five ordinary worlds | All 30 readable without scrolling; favorites occupy a separate row; numeric order; unboxed metrics; no clipping or malformed outlines. |
| 1366×768, 150% and 200%, mixed-DPI monitor moves | Columns adapt, scrolling works, settings headings and controls remain readable, taskbar is respected. |
| Background window during several live cycles and a hidden review | Foreground window stays active; no flashing, restore, focus changes, or selection. |
| Scrolled dashboard and settings during ping changes | Offset and keyboard focus stay in place; card identities/order stay stable. |
| Ctrl+, / Tab / Shift+Tab / Space / Esc | Settings navigation and controls work with visible keyboard focus; cards have no persistent selected state. |
| Gear hover and Windows animation preference | Two-notch clockwise rotation and reverse; no animated rotation when client-area animations are disabled. |
| Members/free/unknown region/activity and PvP/High Risk/Bounty | Correct star/flag/fallback graphics; warning text/tooltips; separate-profile/safe modes avoid the main-character warning. |
| Online/offline startup, cached catalogue, no cache, exit during scan | Settings always accessible; progress/notices/retry work; exit cancels probes cleanly. |
| Default30, upgraded60, later chosen60; favorite and preference changes | Migration happens once; preferences/pins persist; session overrides reset on restart; five-favorite and twenty-five-slot limits hold. |
| Minimize/close tray options, restore/settings/exit | Monitoring continues hidden; only deliberate restore activates; Exit fully quits. |
| Maximize, restore, drag, resize, snap; disconnected monitor | Startup maximized; restore bounds remain reachable; custom frame supports native interactions. |
| Explorer/taskbar/tray icon and signed candidate with runtime | Matching icons; only one EXE required; valid timestamp/signature/hash when approved; responsive launch and clean shutdown. |

Design approval requires reviewing actual Windows output against `Issues needing to be fixed.md`, not just successful compilation. Signing enrollment and manual Windows acceptance must not be represented as completed by a source change.
