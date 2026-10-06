import { ModRegistrar } from "cs2/modding";
import { MeasureItPanel } from "mods/MeasureItPanel";
import { MeasureItButton } from "mods/MeasureItButton";
import { MeasureItLabels } from "mods/MeasureItLabels";

/**
 * Entry point the CS2 UI host loads for this mod.
 *
 * AppendHookTargets (from types/modding.d.ts) only allows:
 * "Menu" | "Editor" | "Game" | "GameTopLeft" | "GameTopRight" |
 * "GameBottomRight" | "UniversalModMenu"
 * - there is no "Toolbar" target, so the toggle button goes into
 * "UniversalModMenu" (the shared slot mods use for their menu buttons/icons)
 * instead.
 */
const register: ModRegistrar = (moduleRegistry) => {
    // Adds the "M" toggle button into the shared mod-buttons menu.
    moduleRegistry.append('UniversalModMenu', MeasureItButton);

    // The measurement readout panel. It positions itself with position:fixed (so
    // its spot can be saved), which is why it sits on the root 'Game' layer next to
    // the labels overlay - that overlay already relies on fixed, screen-pixel
    // coordinates lining up there - instead of the 'GameTopLeft' corner container.
    moduleRegistry.append('Game', MeasureItPanel);

    // Full-viewport overlay of floating point-number labels, positioned by
    // screen coordinate rather than pinned to a corner like the panel above.
    moduleRegistry.append('Game', MeasureItLabels);

    // Editor support. The 'Game' hook above belongs to the in-game screen; the
    // editor is a different screen with its own 'Editor' hook, so the panel and the
    // point-number overlay are mounted there too. Both are self-contained
    // (position:fixed over the viewport), so neither depends on a particular
    // container's layout. The panel remembers a separate position for each screen. The C# side needs nothing extra: MeasureUISystem is a
    // UISystemBase whose default gameMode is All, and the tool system isn't gated by
    // mode, so the same bindings drive both screens.
    //
    // The editor has no Universal Mod Menu (confirmed in-game), so the toggle button
    // isn't available there. No separate editor button on purpose - in the editor
    // the tool is switched on and off with the Ctrl+M keybind (rebindable in
    // Options -> Measure It! for CS2).
    moduleRegistry.append('Editor', MeasureItPanel);
    moduleRegistry.append('Editor', MeasureItLabels);
};

export default register;
