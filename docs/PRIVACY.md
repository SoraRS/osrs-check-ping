# Privacy

OSRS Check Ping has no account login, analytics, advertising, or telemetry service.

On launch and on an explicit **Check connection**, it downloads the public world catalogue from `https://oldschool.runescape.com/slr.ws?order=LPWM`. It resolves the catalogue's supplied hostnames and sends ICMP latency probes to those hosts. Those connections expose the network information normally required for DNS, HTTPS, and ICMP (including your IP address) to your network providers and the contacted services. Their own policies apply.

The app keeps `settings.json` and `worlds.json` under `%LOCALAPPDATA%\OsrsCheckPing`. They contain preferences, favorite world numbers, restored window placement, and the last successful catalogue. Latency readings and session tracking overrides stay in memory and are discarded on exit. There is no history database or upload of saved preferences.

About links open in your default browser only when clicked. Code-signing traffic belongs to the project's release pipeline; the running application does not contact SignPath.

Fully exit the application and delete its data folder to remove its saved data. Deleting or renaming the executable removes the portable application. For questions, use the project's [issue tracker](https://github.com/SoraRS/osrs-check-ping/issues).
