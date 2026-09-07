import { bindValue, useValue } from "cs2/api";
import styles from "./MeasureItLabels.module.scss";

const GROUP = "measureItCS2";

const toolActive$ = bindValue<boolean>(GROUP, "toolActive", false);
const pointLabelsJson$ = bindValue<string>(GROUP, "pointLabelsJson", "[]");
const pointColorHex$ = bindValue<string>(GROUP, "pointColorHex", "#FFD900");

interface PointLabel {
    index: number;
    x: number;
    y: number;
    visible: boolean;
}

/** JSON.parse wrapped with a fallback so one malformed frame can't throw an
 * uncaught exception and crash this component's render - see the same
 * pattern (and rationale) in MeasureItPanel.tsx. */
function safeParse<T>(json: string, fallback: T): T {
    try {
        return JSON.parse(json) as T;
    } catch {
        return fallback;
    }
}

/**
 * Floating point-number labels ("1", "2", "3"...) drawn over the game view at
 * each placed point's screen position. OverlayRenderSystem.Buffer (used for
 * the yellow circles/lines) only has DrawCircle/DrawLine - confirmed via
 * decompiling Game.dll, no text-drawing method exists there - so numbers have
 * to be plain HTML positioned over the 3D view instead, using screen
 * coordinates MeasureUISystem.cs computes each frame via
 * Camera.main.WorldToScreenPoint and sends down as JSON.
 *
 * Registered into the "Game" module slot (a full-viewport overlay layer),
 * not into GameTopLeft like the measurement panel, since these need to be
 * positioned anywhere on screen based on each point's projected location.
 */
export function MeasureItLabels() {
    const toolActive = useValue(toolActive$);
    const pointLabelsJson = useValue(pointLabelsJson$);
    const pointColorHex = useValue(pointColorHex$);

    if (!toolActive) {
        return null;
    }

    const labels: PointLabel[] = safeParse(pointLabelsJson, []);

    return (
        <div className={styles.overlay}>
            {labels
                // The first point (index 0) is deliberately left unnumbered - its
                // number would otherwise not correspond to anything in the segment
                // table, since segments are indexed by the point they END at
                // (segment 1 runs to point 1, segment 2 to point 2, etc.). Skipping
                // it here still draws its plain circle via the C# overlay
                // (DrawOverlay in MeasureToolSystem.cs); it just gets no number badge.
                .filter((label) => label.visible && label.index > 0)
                .map((label) => (
                    <div
                        key={label.index}
                        className={styles.label}
                        style={{
                            left: `${label.x}px`,
                            top: `${label.y}px`,
                            color: pointColorHex,
                            borderColor: pointColorHex,
                        }}
                    >
                        {label.index}
                    </div>
                ))}
        </div>
    );
}
