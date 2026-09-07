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

    // Adds the draggable measurement readout panel to the general in-game UI layer.
    moduleRegistry.append('GameTopLeft', MeasureItPanel);

    // Full-viewport overlay of floating point-number labels, positioned by
    // screen coordinate rather than pinned to a corner like the panel above.
    moduleRegistry.append('Game', MeasureItLabels);
};

export default register;
