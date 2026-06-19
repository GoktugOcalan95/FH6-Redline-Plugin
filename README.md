# FH6Redline Plugin

SimHub plugin for Forza Horizon 6. Learns a car's usable rev-out RPM and exposes a
shift-light proximity value.

Most FH6 engines rev out monotonically, so the best shift point is the highest RPM the
engine holds before the limiter starts bouncing. The reported redline is higher than that
point, and the values reported during bouncing are higher still. The plugin finds the onset
of bouncing and ignores the rest.

## Overlay

Included in the release is an overlay that displays the shift lights, current gear, speed and rpms.
There is also a MPH version of the overlay included.

![FH6 Shift Lights overlay](DashTemplates/FH6%20Shift%20Lights/FH6%20Shift%20Lights.djson.png)

Overlay hides itself if the rpms are near the engine's idle rpm and the speed is below 20 kmh.

## Plugin Properties

Exposed under the `FH6Redline.` prefix:

| Property | Type | Meaning |
|---|---|---|
| `MaxUsableRpm` | int | Shift point: Calculated max usable rpm for the engine. 0 until the first rev-out. |
| `RevLimiterProximity` | double | 0 → 1 as RPM approaches `MaxUsableRpm`. Window scales by gear. |
| `CurrentCarKey` | string | Identifier for the active car/tune. |

## How it works

- A rev-out is detected at near-full throttle: RPM climbs, then falls back from its peak.
  That peak is one sample. Gear is not considered.
- The value is used from the first sample onward and averaged as more arrive.
- After 5 samples the shift point is locked and saved as `carKey → rpm`. A known car key is
  loaded from file and not recalculated.
- The shift point is the sample average minus a fixed 75 RPM offset.
- Proximity uses a fixed per-gear window so the shift LEDs stay lit for a similar duration in
  every gear — wide in low gears, narrow in high gears.

Car key is `CarOrdinal_CarPerformanceIndex_MaxRpm`. Any change starts a fresh calculation.

## Install

From a release, with SimHub closed, extract the archive into the SimHub folder (the one containing
`SimHubWPF.exe`). `FH6Redline.dll` lands in the root and the `DashTemplates` folder merges with the
existing one, adding the two shift-light overlays. Extracting into `Program Files` needs admin
rights.

Start SimHub, enable the plugin, and select an overlay. Overlays can be found by going to Dash Studio
on the left menu, going to Overlays at the top and scroll down to see the list of AVAILABLE OVERLAYS.

Overlays are named `FH6 Shift Lights` and `FH6 Shift Lights MPH`. First one is in KMPH.

## Build

.NET SDK required. Most recent SDK can target .NET Framework 4.8 for the build.

1. Set `SIMHUB_INSTALL_PATH` to the folder containing `SimHubWPF.exe` (trailing `\`), or edit
   `<SimHubPath>` in `FH6Redline.csproj`.
2. Run `dotnet build -c Release`. The DLL is copied into the SimHub folder automatically;
   close SimHub before building.
3. Start SimHub and enable the plugin.

Locked cars persist in `PluginsData\Common\FH6RedlineCars.json` across sessions. To edit it by
hand, exit SimHub completely first (including the system tray); the file is only written when a new
car finishes learning, and edits to other entries are preserved on that write.

## Notes

- `CarOrdinal` / `CarPerformanceIndex` are read from SimHub's Forza property tree
  (`DataCorePlugin.GameRawData.*`). The property paths are in `BuildKey`.
- Tuning constants (throttle gate, rpm offset, sample count, detection thresholds, gear windows) are
  at the top of `FH6Redline.cs`.
- Targets monotonic rev-out engines (power always climbs as revs increase) 
- Peaky engines might require short shifting for ideal performance. In these cases, the shift value
  calculated by this plugin will be wrong. If your power curve peaks early and slowly declines, 
  ignore the shift lights and short shift as needed.
