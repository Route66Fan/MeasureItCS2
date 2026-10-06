import { bindValue, trigger, useValue } from "cs2/api";
import { Panel, Scrollable, Button, Dropdown, DropdownToggle } from "cs2/ui";
import { useEffect, useRef, useState } from "react";
import type { MouseEvent as ReactMouseEvent } from "react";
import styles from "./MeasureItPanel.module.scss";

const GROUP = "measureItCS2";

// Bindings must match the AddBinding(...) names in MeasureUISystem.cs exactly.
const toolActive$ = bindValue<boolean>(GROUP, "toolActive", false);
const unitOfDistance$ = bindValue<number>(GROUP, "unitOfDistance", 0);
const unitOfSlope$ = bindValue<number>(GROUP, "unitOfSlope", 1);
const unitOfDirection$ = bindValue<number>(GROUP, "unitOfDirection", 1);
const panelPlaced$ = bindValue<boolean>(GROUP, "panelPlaced", false);
const panelX$ = bindValue<number>(GROUP, "panelX", 0);
const panelY$ = bindValue<number>(GROUP, "panelY", 0);
const snapToNodes$ = bindValue<boolean>(GROUP, "snapToNodes", true);

export interface MeasurementRow {
    index: number;
    relief: string;
    length: string;
    distance: string;
    slope: string;
    direction: string;
}

export interface MeasurementSummary {
    pointCount: number;
    elevation: string;
    totalDistance: string;
    straightLineDistance: string;
    distanceUnitSymbol: string;
    slopeUnitSymbol: string;
    directionUnitSymbol: string;
}

const DEFAULT_SUMMARY: MeasurementSummary = {
    pointCount: 0,
    elevation: "0",
    totalDistance: "0",
    straightLineDistance: "0",
    distanceUnitSymbol: "m",
    slopeUnitSymbol: "%",
    directionUnitSymbol: "\u00b0",
};

// The C# side sends these as JSON strings (see MeasureUISystem.cs's note on why),
// so we bind them as plain strings and JSON.parse before use.
const segmentsJson$ = bindValue<string>(GROUP, "segmentsJson", "[]");
const summaryJson$ = bindValue<string>(GROUP, "summaryJson", JSON.stringify(DEFAULT_SUMMARY));

const DISTANCE_UNITS = [
    { value: 0, label: "Units\\Meters" },
    { value: 1, label: "Kilometers" },
    { value: 2, label: "Feet" },
    { value: 3, label: "Miles" },
];

const SLOPE_UNITS = [
    { value: 0, label: "Degrees" },
    { value: 1, label: "Percentage" },
];

const DIRECTION_UNITS = [
    { value: 0, label: "Degrees" },
    { value: 1, label: "Cardinal" },
];

/** JSON.parse wrapped with a fallback: the C# side always sends well-formed
 * JSON in practice, but parsing untrusted/unexpected input without a guard
 * would let one bad frame throw an uncaught exception and crash this whole
 * component's render instead of just showing stale/default data for a tick. */
function safeParse<T>(json: string, fallback: T): T {
    try {
        return JSON.parse(json) as T;
    } catch {
        return fallback;
    }
}

// Width of the window (keep in sync with .panel in the stylesheet) and how much of
// its height must stay on screen when a saved position is clamped to the current
// screen size - so a position saved at one resolution can never leave the window
// unreachable after a change of resolution or UI scale.
const PANEL_WIDTH = 420;
const MIN_VISIBLE_HEIGHT = 120;

/** A window position in screen pixels (its top-left corner). */
interface PanelSpot {
    left: number;
    top: number;
}

/** The same position as fractions (0..1) of the screen - what gets saved. */
interface PanelFraction {
    x: number;
    y: number;
}

function clamp(value: number, min: number, max: number): number {
    return Math.min(Math.max(value, min), Math.max(min, max));
}

/** Draggable measurement readout panel - replaces the CS1 UIPanel/UIDragHandle
 * "info panel" that ModManager.cs built by hand with 7 rows of UILabels. */
export function MeasureItPanel() {
    const toolActive = useValue(toolActive$);
    const unitOfDistance = useValue(unitOfDistance$);
    const unitOfSlope = useValue(unitOfSlope$);
    const unitOfDirection = useValue(unitOfDirection$);
    const segmentsJson = useValue(segmentsJson$);
    const summaryJson = useValue(summaryJson$);
    const panelPlaced = useValue(panelPlaced$);
    const panelX = useValue(panelX$);
    const panelY = useValue(panelY$);
    const snapToNodes = useValue(snapToNodes$);

    // Parsed once per render from the JSON strings the C# side sends.
    const segments: MeasurementRow[] = safeParse(segmentsJson, []);
    const summary: MeasurementSummary = safeParse(summaryJson, DEFAULT_SUMMARY);

    // Deliberately never unmount the panel (no early "return null" here): hiding it
    // with CSS keeps the component instance alive across tool toggles, and avoids
    // the layout "flash" a remount used to cause.
    const visible = toolActive;

    // ---- Window position --------------------------------------------------
    // This component positions the window itself instead of using the game's own
    // draggable Panel. That Panel only accepts an initialPosition on first mount
    // and never reports where it ended up (confirmed in types/ui.d.ts), so a
    // position could neither be saved nor restored with it. So the Panel is used as
    // plain (non-draggable) chrome inside a position:fixed container that is moved
    // from the title bar here. The spot is saved on the C# side - separately for
    // the city and the editor - as fractions of the screen, and comes back through
    // the panelPlaced / panelX / panelY bindings.
    const anchorRef = useRef<HTMLDivElement>(null);
    const [dragSpot, setDragSpot] = useState<PanelSpot | null>(null);
    const [pendingSpot, setPendingSpot] = useState<PanelFraction | null>(null);
    const stopDragRef = useRef<(() => void) | null>(null);

    // After a drop, keep showing the dropped position until the saved one has made
    // the round trip through C# and back via the bindings - otherwise the window
    // would flick back to its old spot for a frame or two. A timeout guarantees it
    // can never stay stuck overriding the saved position.
    useEffect(() => {
        if (!pendingSpot) {
            return undefined;
        }

        const arrived = Math.abs(panelX - pendingSpot.x) < 0.0005 && Math.abs(panelY - pendingSpot.y) < 0.0005;
        if (arrived) {
            setPendingSpot(null);
            return undefined;
        }

        const timeout = setTimeout(() => setPendingSpot(null), 2000);
        return () => clearTimeout(timeout);
    }, [panelX, panelY, pendingSpot]);

    // If the panel unmounts mid-drag (e.g. a scene change), detach the listeners.
    useEffect(() => () => stopDragRef.current?.(), []);

    const viewportWidth = window.innerWidth;
    const viewportHeight = window.innerHeight;
    const spotFromFraction = (x: number, y: number): PanelSpot => ({
        left: clamp(x * viewportWidth, 0, viewportWidth - PANEL_WIDTH),
        top: clamp(y * viewportHeight, 0, viewportHeight - MIN_VISIBLE_HEIGHT),
    });

    // null = never moved: the stylesheet's default (centered) placement applies.
    let spot: PanelSpot | null = null;
    if (dragSpot) {
        spot = dragSpot;
    } else if (pendingSpot) {
        spot = spotFromFraction(pendingSpot.x, pendingSpot.y);
    } else if (panelPlaced) {
        spot = spotFromFraction(panelX, panelY);
    }

    const onHeaderMouseDown = (event: ReactMouseEvent) => {
        const anchor = anchorRef.current;
        if (event.button !== 0 || !anchor) {
            return;
        }
        event.preventDefault();

        // Measured at the start of the drag, so this also works from the default
        // (centered) placement, whose pixel position isn't known up front.
        const rect = anchor.getBoundingClientRect();
        const startX = event.clientX;
        const startY = event.clientY;
        const clampSpot = (left: number, top: number): PanelSpot => ({
            left: clamp(left, 0, window.innerWidth - rect.width),
            top: clamp(top, 0, window.innerHeight - rect.height),
        });

        let last = clampSpot(rect.left, rect.top);
        let moved = false;
        setDragSpot(last);

        const onMove = (move: MouseEvent) => {
            const dx = move.clientX - startX;
            const dy = move.clientY - startY;
            if (!moved && Math.abs(dx) + Math.abs(dy) > 2) {
                moved = true;
            }
            last = clampSpot(rect.left + dx, rect.top + dy);
            setDragSpot(last);
        };

        const stop = () => {
            window.removeEventListener("mousemove", onMove);
            window.removeEventListener("mouseup", onUp);
            stopDragRef.current = null;
        };

        const onUp = () => {
            stop();
            setDragSpot(null);

            // A plain click on the title bar isn't a move - don't save anything.
            if (!moved) {
                return;
            }

            const fraction: PanelFraction = {
                x: last.left / window.innerWidth,
                y: last.top / window.innerHeight,
            };
            setPendingSpot(fraction);
            trigger(GROUP, "setPanelPosition", fraction.x, fraction.y);
        };

        stopDragRef.current = stop;
        window.addEventListener("mousemove", onMove);
        window.addEventListener("mouseup", onUp);
    };

    return (
        <div
            ref={anchorRef}
            className={`${styles.anchor} ${spot ? "" : styles.anchorDefault}`}
            style={spot ? { left: `${spot.left}px`, top: `${spot.top}px` } : undefined}
        >
            <Panel
                className={`${styles.panel} ${visible ? "" : styles.hidden}`}
                header={
                    <div className={styles.header} onMouseDown={onHeaderMouseDown}>
                        <span>Measure It! for CS2</span>
                    </div>
                }
                transition={null}
                transitionSounds={null}
            >
                <div className={styles.unitsRow}>
                    <UnitDropdown
                        label="Distance"
                        value={unitOfDistance}
                        options={DISTANCE_UNITS}
                        onChange={(value) => trigger(GROUP, "setUnitOfDistance", value)}
                        active={toolActive}
                    />
                    <UnitDropdown
                        label="Slope"
                        value={unitOfSlope}
                        options={SLOPE_UNITS}
                        onChange={(value) => trigger(GROUP, "setUnitOfSlope", value)}
                        active={toolActive}
                    />
                    <UnitDropdown
                        label="Direction"
                        value={unitOfDirection}
                        options={DIRECTION_UNITS}
                        onChange={(value) => trigger(GROUP, "setUnitOfDirection", value)}
                        active={toolActive}
                    />
                </div>

                <div className={styles.optionsRow}>
                    <Checkbox
                        checked={snapToNodes}
                        onChange={(checked) => trigger(GROUP, "setSnapToNodes", checked)}
                        label="Snap to nodes"
                    />
                </div>

                <div className={styles.summaryGrid}>
                    <SummaryRow label={`Elevation (${summary.distanceUnitSymbol})`} value={summary.elevation} />
                    <SummaryRow
                        label={`Total distance (${summary.distanceUnitSymbol})`}
                        value={summary.totalDistance}
                        tooltip="Sum of the true 3D distance of every segment."
                    />
                    {/* Always rendered (it just reads 0 until there are 2+ points) - see the
                        note above the segment table: the window's height has to stay constant. */}
                    <SummaryRow
                        label={`Straight line (${summary.distanceUnitSymbol})`}
                        value={summary.straightLineDistance}
                        tooltip="Direct distance from the first point to the last point."
                    />
                </div>

                {/* The segment table is always rendered, at a fixed height, even with no
                    points. This window is a centered Panel, so its top edge moves whenever
                    its height changes: rows appearing/disappearing (or a new session
                    starting with an empty chain) made it jump up or down. Keeping the
                    size constant means it can't move. The cost is a blank table area
                    before the first segment - the list height is set in the stylesheet. */}
                <div className={styles.segmentTable}>
                    <div className={`${styles.segmentRow} ${styles.segmentHeaderRow}`}>
                        <span className={styles.colIndex}>#</span>
                        <span className={styles.colValue}>{`Distance (${summary.distanceUnitSymbol})`}</span>
                        <span className={styles.colValue}>{`Relief (${summary.distanceUnitSymbol})`}</span>
                        <span className={styles.colValue}>{`Slope (${summary.slopeUnitSymbol})`}</span>
                        <span className={styles.colValue}>{`Direction (${summary.directionUnitSymbol})`}</span>
                    </div>
                    <Scrollable className={styles.segmentList}>
                        {segments.map((row) => (
                            <div className={styles.segmentRow} key={row.index}>
                                <span className={styles.colIndex}>{row.index}</span>
                                <span className={styles.colValue}>{row.distance}</span>
                                <span className={styles.colValue}>{row.relief}</span>
                                <span className={styles.colValue}>{row.slope}</span>
                                <span className={styles.colValue}>{row.direction}</span>
                            </div>
                        ))}
                    </Scrollable>
                </div>

                <div className={styles.actions}>
                    <Button variant="flat" onSelect={() => trigger(GROUP, "clearPoints")}>
                        Clear all
                    </Button>
                </div>

                <div className={styles.hint}>Left-click: add point &nbsp;|&nbsp; Right-click: undo last point</div>
            </Panel>
        </div>
    );
}

function SummaryRow({ label, value, tooltip }: { label: string; value: string; tooltip?: string }) {
    return (
        <div className={styles.summaryRow} title={tooltip}>
            <span className={styles.summaryLabel}>{label}</span>
            <span className={styles.summaryValue}>{value}</span>
        </div>
    );
}

/** Custom checkbox built from plain styled elements rather than a native
 * <input type="checkbox">. This UI engine (Coherent/cohtml) doesn't render
 * native checkboxes correctly - confirmed empirically, it showed up as an
 * oversized blank white box instead of a small checkbox square, the same
 * class of issue as native HTML <table> not laying out as a table earlier
 * in this project. Building it from a div sidesteps that entirely, matching
 * how the dropdown options list already works around a similar gap. */
function Checkbox({ checked, onChange, label }: { checked: boolean; onChange: (checked: boolean) => void; label: string }) {
    return (
        <div className={styles.checkboxLabel} onClick={() => onChange(!checked)}>
            <div className={`${styles.checkboxBox} ${checked ? styles.checkboxBoxChecked : ""}`}>
                {checked && <span className={styles.checkmark}>{"\u2713"}</span>}
            </div>
            <span>{label}</span>
        </div>
    );
}

function UnitDropdown({
    label,
    value,
    options,
    onChange,
    className,
    active,
}: {
    label: string;
    value: number;
    options: { value: number; label: string }[];
    onChange: (value: number) => void;
    className?: string;
    active: boolean;
}) {
    const current = options.find((o) => o.value === value) ?? options[0];

    // While inactive, this renders a plain, non-interactive placeholder instead of
    // the real Dropdown component - no Dropdown instance exists in the tree at all,
    // so there's no portal it could leave stuck open. A key-based remount of the
    // same component type was tried first (forcing React to tear down and recreate
    // Dropdown whenever the tool toggled), but its popup content still survived
    // that in practice - apparently something in its own internal
    // transition/cleanup timing doesn't complete synchronously with an abrupt
    // unmount. Swapping to an entirely different component type at this same JSX
    // position is a stronger guarantee: React unmounts the previous subtree (and
    // whatever portal it created) unconditionally before mounting the replacement,
    // rather than relying on the same component's own internal teardown to run.
    if (!active) {
        return (
            <div className={`${styles.unitDropdown} ${className ?? ""}`}>
                <span className={styles.unitLabel}>{label}</span>
                <span className={styles.dropdownToggleLabel}>{current.label}</span>
            </div>
        );
    }

    // NOTE: cs2/ui's exported "DropdownItem" doesn't resolve as a usable JSX
    // component in this project's TS setup (it only resolves as the type
    // describing an item's shape, not the actual list-item component) - see
    // types/ui.d.ts, where the interface and the re-exported function share
    // the same external name but don't merge the way you'd expect. Building
    // the option list with plain buttons inside Dropdown's `content` sidesteps
    // that entirely; only Dropdown/DropdownToggle (which are real value
    // exports) are used from the library here.
    return (
        <div className={`${styles.unitDropdown} ${className ?? ""}`}>
            <span className={styles.unitLabel}>{label}</span>
            <Dropdown
                content={
                    <div className={styles.dropdownList}>
                        {options.map((option) => (
                            <button
                                key={option.value}
                                type="button"
                                className={`${styles.dropdownOption} ${
                                    option.value === value ? styles.dropdownOptionSelected : ""
                                }`}
                                onClick={() => onChange(option.value)}
                            >
                                {option.label}
                            </button>
                        ))}
                    </div>
                }
            >
                <DropdownToggle>
                    <span className={styles.dropdownToggleLabel}>{current.label}</span>
                </DropdownToggle>
            </Dropdown>
        </div>
    );
}