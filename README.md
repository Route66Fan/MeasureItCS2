# Measure It! for CS2

A Cities: Skylines II port of the classic **Measure It!** mod for Cities: Skylines. Place a chain of points anywhere in your city — on the ground, on roads and bridges, inside tunnels, on building rooftops — and get live elevation, distance, slope, and direction readouts between them.

## Features

- **Multi-point measurement chains** — not just a start/end pair. Click as many points as you like and see a running segment-by-segment breakdown.
- **Unit choices** for every measurement type:
  - Distance: Meters, Kilometers, Feet, Miles
  - Slope: Percentage, Degrees
  - Direction: Degrees, Cardinal (N/NE/E/...)
- **Live readout panel** — draggable, remembers its position, shows per-segment distance/relief/slope/direction plus running totals (total distance, straight-line distance, elevation).
- **Customizable point/line color** (Yellow, Magenta, Red, Green, Cyan, White, Orange).
- **Numbered point markers** on the map, matching the segment table's own numbering.
- Works on elevated bridges and underground tunnels, not just flat ground.
- Rebindable keyboard shortcuts (defaults: `Ctrl+M` toggle tool, `Ctrl+Z` add point at cursor, `Ctrl+X` undo last point).
- Toolbar button in the game's Universal Mod Menu.

## Installation

Subscribe via [Paradox Mods](https://mods.paradoxplaza.com/games/cities_skylines_2) (link added once published) or download a release from this repository and drop it into your `Mods` folder:

```
%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\MeasureItCS2
```

## Usage

1. Click the ruler icon in the Universal Mod Menu, or press `Ctrl+M`, to activate the tool.
2. Left-click on the map to place a point. Keep clicking to build a chain.
3. Right-click (or `Ctrl+X`) to undo the last point.
4. Adjust units and point color from the panel or from Options → Measure It! for CS2.

## Building from source

This is a standard [CS2 modding toolchain](https://cs2.paradoxwikis.com/Modding_Toolchain) project: a C# `.csproj` for the game-side systems, plus a separate React/TypeScript UI project under `UI/` built with the toolchain's `create-csii-ui-mod` template. See `UI/README` (or the toolchain docs) for the UI build steps if you haven't set that up before.

## Credits

- Original CS1 **Measure It!** mod concept.
- Ported to Cities: Skylines II's ECS/React architecture, with unit expansion (Kilometers, Miles), multi-point support, bridge/tunnel measurement, and a rebuilt React UI.

## License

_Add your preferred license here (e.g. MIT)._
