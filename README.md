Built with assistance from Claude (Anthropic).

Original mod can be found here: https://github.com/keallu/CSL-MeasureIt

# Measure It! for CS2

A Cities: Skylines II port of the classic Measure It! mod for Cities: Skylines. Place a chain of points anywhere in your city — on the ground, on roads and bridges, inside tunnels & even underground pipes & power lines — and get live elevation, distance, slope, and direction readouts between them.

## Features

* **Multi-point measurement chains** — not just a start/end pair. Click as many points as you like and see a running segment-by-segment breakdown.
* **Unit choices** for every measurement type:
   * Distance: Meters, Kilometers, Feet, Miles
   * Slope: Percentage, Degrees
   * Direction: Degrees, Cardinal (N/NE/E/...)
* **Live readout panel** — draggable, keeps its position when you toggle the tool off and on, shows per-segment distance/relief/slope/direction plus running totals (total distance, straight-line distance, elevation).
* **Customizable point/line color** (Yellow, Magenta, Red, Green, Cyan, White, Orange).
* **Numbered point markers** on the map, matching the segment table's own numbering.
* Works on elevated bridges and underground tunnels, not just flat ground.
* **Snap to nodes** — clicks snap to actual road, pipe, track, power line, and subway nodes, so you can measure precisely to or through buried pipes and underground lines, not just visible surfaces. Toggle it in the readout panel.
* **Network-following lines** — when two points snap to connected nodes on the same network, the connecting line traces the real route of the road, pipe, or subway line between them instead of cutting straight across. If no connecting route is found, a straight line is drawn.
* **Editor support** — also works in the Cities: Skylines II editor. The editor has no Universal Mod Menu, so toggle the tool there with its keybind (`Ctrl+M` by default).
* Rebindable keyboard shortcuts (defaults: `Ctrl+M` toggle tool, `Ctrl+Z` add point at cursor, `Ctrl+X` undo last point).
* Toolbar button in the game's Universal Mod Menu (in-game only).

> **Note:** the readout values are always the straight-line 3D measurement between two points. The route-following line is a visual aid and does not change any number in the panel.

## Installation

Subscribe via [Paradox Mods](https://mods.paradoxplaza.com/games/cities_skylines_2) (link added once published) or download a release from this repository and drop it into your `Mods` folder:

```
%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\MeasureItCS2
```

## Usage

1. Click the ruler icon in the Universal Mod Menu, or press `Ctrl+M`, to activate the tool. In the editor, use `Ctrl+M`.
2. Left-click on the map to place a point. Keep clicking to build a chain.
3. Right-click (or `Ctrl+X`) to undo the last point.
4. Distance, slope, and direction units and the Snap to nodes toggle are in the readout panel. Point color, the panel-position reset, and the keybinds are in Options → Measure It! for CS2.

## Building from source

This is a standard [CS2 modding toolchain](https://cs2.paradoxwikis.com/Modding_Toolchain) project: a C# `.csproj` for the game-side systems, plus a separate React/TypeScript UI project under `UI/` built with the toolchain's `create-csii-ui-mod` template. After cloning, run `npm install` once inside `UI/` (`node_modules` isn't committed), then build the C# project; its build step runs the UI build automatically. See `UI/README` (or the toolchain docs) for the UI build steps if you haven't set that up before.

## Changelog

### v1.01

* Added support for the Cities: Skylines II editor (toggle the tool with `Ctrl+M`).
* Point numbers now follow the game's active camera.

### v1.0

* Initial release.

## Credits

* Original CS1 Measure It! mod concept by keallu.
* Ported to Cities: Skylines II's ECS/React architecture, with unit expansion (Kilometers, Miles), multi-point support, bridge/tunnel measurement, node snapping, network-following lines, editor support, and a rebuilt React UI.

## License

Add your preferred license here (e.g. MIT).