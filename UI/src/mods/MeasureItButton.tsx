import { bindValue, trigger, useValue } from "cs2/api";
import { Button } from "cs2/ui";
import measureIcon from "./measureIcon.svg";
import styles from "./MeasureItButton.module.scss";

const GROUP = "measureItCS2";
const toolActive$ = bindValue<boolean>(GROUP, "toolActive", false);

/**
 * In-game toggle button for the measure tool, replacing the CS1 "M"
 * control-panel button (ModManager.cs's _measureButton).
 *
 * Uses the generic Button component (cs2/ui) rather than MenuButton.
 * Confirmed against bruceyboy24804/NodeController's real, working CS2
 * source (its nodeControllerToggle.tsx): Button's `src` renders as an
 * actual <img> element showing the SVG's own real colors, styled directly
 * via a plain `img { ... }` CSS selector in their stylesheet - no masking,
 * no special centering needed, it's centered by default. MenuButton (what
 * we tried before) renders `src` as a CSS mask instead, which is why our
 * SVG's colors were being ignored and stayed off-center no matter what we
 * tried to override.
 */
export function MeasureItButton() {
    const active = useValue(toolActive$);

    return (
        <Button
            src={measureIcon}
            variant="floating"
            selected={active}
            className={`${styles.toggle} ${active ? styles.selected : ""}`}
            tooltipLabel="Measure It! for CS2 - click points on the map to measure between them"
            onSelect={() => trigger(GROUP, "toggleTool")}
        />
    );
}
