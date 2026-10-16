# OSRS World Ping

See your connection latency to Old School RuneScape worlds, keep your favorites close, and find a world with a lower ping. World Ping is a small Windows app with a fantasy-inspired interface and world cards that stay in world-number order while their readings update.

## Download and open

1. Use **Windows x64** and install Microsoft's **[.NET 10 Desktop Runtime (x64)](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)**. Select **Desktop Runtime**; the standard or ASP.NET runtime alone is not enough.
2. Download `OsrsWorldPing.exe` from [Releases](https://github.com/SoraRS/osrs-world-ping/releases) when a release is available. For testing builds, follow the steps below.
3. Extract the download to a folder you want to keep, then open `OsrsWorldPing.exe`. No installer or administrator access is normally required.
4. The app starts maximized and checks every world once. Open the **gear** to see progress or any connection notice.

**To download a testing build:** sign into GitHub, open [Build and test](https://github.com/SoraRS/osrs-world-ping/actions/workflows/build.yml), and choose the latest successful run for **main**. Scroll to **Artifacts** and download **OsrsWorldPing-win-x64**, then extract its ZIP. The interface previews artifact contains pictures, rather than the application. [GitHub's download guide](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/download-workflow-artifacts) explains the sign-in and download steps.

The compact card interface described here is **version 1.2.0 or newer**; check **Settings → About**. An older workflow run contains an older executable, even if a newer build is currently running. Testing builds may be unsigned.

## Updating an older copy

Fully exit the old app first. If it is in the system tray, right-click its icon and choose **Exit**. Replace the old executable with the new download, then open that file and update any shortcut pointing elsewhere. Existing favorites and preferences stay on your device.

Downloading a new build does not replace other copies automatically. If you still see the old table with **Connection**, **Access**, and **Region** columns, check the file your shortcut opens and the version in About.

## Read your world cards

The dashboard shows up to **20 eligible worlds**, plus **five favorites** in their own section. Automatic selection favors the lowest pings at or below **30 ms** by default. Both sections display worlds by number, so a ping change does not shuffle the cards.

| On the card | What it means |
| --- | --- |
| Country flag | The world's region; a globe is used when the region is unknown. |
| Filled gold star | Members world. |
| Outlined gold star | Free-to-play world. |
| Red heart | One of your always-tracked favorites. Click the heart to add or remove a favorite. |
| Activity symbol and name | Genuine OSRS skill and boss sprites identify supported activities. Total-level worlds use the experience bar graph; unfamiliar activities keep a generic icon. Hover for long names. |
| Number in ms and five signal bars beside the world number | Your latest network round-trip time. Lower is better. |
| Red world/activity text | A main-character PvP, High Risk, or Bounty Hunter warning. Hover for the reason and read the game's warning before entering. |
| Timeout, unreachable, or unavailable | A usable reading could not be obtained, or a saved favorite is missing from the catalogue. |

Live worlds update every **five seconds** by default. Other worlds stay hidden and receive no regular live checks. Every **five minutes**, a background review checks the hidden worlds and updates the selection. No countdown or manual refresh is needed to keep that review running. Both world views use compact grids over a translucent surface, leaving the landscape visible. Hovering adds a soft animated glow, and hearts respond when enabled; movement follows Windows' animation preference.

Ping measures your network connection, **not server ticks**. A low reading does not guarantee a lag-free game, and an ICMP timeout does not necessarily mean a world is offline.

## Settings

Open the **gear** or press **Ctrl+,**. Settings replaces the dashboard inside the same window. Select **Worlds** or press **Esc** to return.

- **World tracking:** search by world number, country, or activity. Turn on **Browse hidden worlds** to manage the rest of the catalogue; browsing alone sends no probes. Use the **power icon** to turn tracking on or off for this session. Inactive cards fade; hovering makes their controls clearer. Manually enabled worlds take priority within the 20 ordinary slots. **Reset session choices** returns to automatic selection.
- **Heart:** save up to five favorites. A red heart means always tracked; a faded heart is available to add. Favorites use separate slots and keep being checked through high latency and failures. Their power control stays on and locked while favorited. Remove a favorite before adding a sixth. Removing its heart restores the underlying session or automatic choice.
- **Check connection:** check every world again. Use this after changing the cutoff if you want to find newly eligible worlds immediately. Your favorites and session choices survive the scan. Hidden-world readings are labeled **Scan result**.
- **Application:** change the latency cutoff, refresh interval, always-on-top, or tray options, then select **Apply settings**. Tray options and always-on-top start disabled. When close-to-tray is enabled, use the tray menu's **Exit** command to quit fully.
- **About:** find the version, project and support links, privacy information, and license.

Ordinary worlds leave the live selection after one minute's worth of consecutive slow or failed readings—12 at the default interval. A good reading resets that count. They can return at the next background review, through **Check connection**, or when manually enabled. Favorites are exempt from removal. Session tracking choices reset on exit; favorites and application preferences are saved.

## Common problems

**I still see the old interface.** Fully exit any older copy, download the application artifact from the correct successful build, and launch the extracted file directly. Check **About** for version 1.2.0 or newer and update your shortcut.

**No ordinary worlds appear.** Open settings to check progress or an error notice. The default 30 ms cutoff may be too low for your location. Raise it in **Application**, select **Apply settings**, then use **Check connection**. You can also favorite a world regardless of its ping.

**Everything times out.** Check your connection, VPN, firewall, and network rules. Some networks block ICMP even when the game works. Try another network before assuming every world is offline; administrator access is not normally required.

**The world list will not download.** The app uses its last saved catalogue when possible. Settings shows its saved time and the connection notice. Connect to the internet and select **Check connection** to retry.

**Windows asks for a runtime.** Install the **x64 .NET 10 Desktop Runtime** linked above, then reopen the app.

**Windows says “Don't run” or “unknown publisher.”** Current testing builds are unsigned. Confirm that the file came from this project's release or Actions page. Signed releases can be checked under **Properties → Digital Signatures**, using the signature and checksum information accompanying that release. See [Windows warnings and signing status](docs/SIGNING.md) for details. Do not disable Windows protection globally.

**The app disappeared.** If a tray option is enabled, look in the system tray. Double-click the icon to restore the app, or right-click for **Settings** and **Exit**.

**My cutoff changed from 60 to 30 ms.** Existing installations using the old 60 ms default move to 30 ms once. Other custom cutoffs are kept. You can choose 60 ms again and save it afterward.

**Preferences do not survive a restart.** Check the settings page for a save error. Current choices can still work for that session. The app needs access to `%LOCALAPPDATA%\OsrsWorldPing`. To reset saved data, fully exit and rename that folder; the app creates it again next launch.

## Privacy and help

Preferences, favorites, restored window placement, and the last world catalogue are saved locally. The app downloads Jagex's public catalogue and probes its supplied world hosts. There is no game account login, analytics, or saved latency history. Read [Privacy](docs/PRIVACY.md) for details.

[Report a problem](https://github.com/SoraRS/osrs-world-ping/issues) with your app version, Windows version, display scaling, and what happened. Build instructions, publishing, and Windows acceptance checks are in [the developer guide](docs/DEVELOPMENT.md).

Application code and original artwork: [Apache License 2.0](LICENSE). OSRS sprites: © Jagex Ltd.; [sources and credits](src/OsrsWorldPing.Desktop/Assets/Osrs/SOURCES.md). Independent software, not affiliated with or endorsed by Jagex.
