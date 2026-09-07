import { bindValue, trigger, useValue } from "cs2/api";
import { Panel, Scrollable, Button, Dropdown, DropdownToggle } from "cs2/ui";
import { useEffect, useRef, useState } from "react";
import styles from "./MeasureItPanel.module.scss";

const GROUP = "measureItCS2";

// Bindings must match the AddBinding(...) names in MeasureUISystem.cs exactly.
const toolActive$ = bindValue<boolean>(GROUP, "toolActive", false);
const unitOfDistance$ = bindValue<number>(GROUP, "unitOfDistance", 0);
const unitOfSlope$ = bindValue<number>(GROUP, "unitOfSlope", 1);
const unitOfDirection$ = bindValue<number>(GROUP, "unitOfDirection", 1);
const resetPanelPositionCounter$ = bindValue<number>(GROUP, "resetPanelPositionCounter", 0);
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

/** Draggable measurement readout panel - replaces the CS1 UIPanel/UIDragHandle
 * "info panel" that ModManager.cs built by hand with 7 rows of UILabels. */
export function MeasureItPanel() {
    const toolActive = useValue(toolActive$);
    const unitOfDistance = useValue(unitOfDistance$);
    const unitOfSlope = useValue(unitOfSlope$);
    const unitOfDirection = useValue(unitOfDirection$);
    const segmentsJson = useValue(segmentsJson$);
    const summaryJson = useValue(summaryJson$);
    const resetPanelPositionCounter = useValue(resetPanelPositionCounter$);
    const snapToNodes = useValue(snapToNodes$);

    // Parsed once per render from the JSON strings the C# side sends.
    const segments: MeasurementRow[] = safeParse(segmentsJson, []);
    const summary: MeasurementSummary = safeParse(summaryJson, DEFAULT_SUMMARY);

    // Deliberately never unmount the panel (no early "return null" here). The
    // Panel component's draggable position is tracked internally with no public
    // callback to read it back out (DraggablePanelProps only exposes
    // initialPosition, used once on first mount - confirmed in types/ui.d.ts).
    // If we unmount/remount on every tool toggle, that internal position resets
    // each time. Hiding via CSS instead keeps the component instance alive, so
    // wherever the user last dragged it to is preserved across toggles.
    const visible = toolActive;

    // "Reset Panel Position" (Options page button) works by deliberately forcing
    // a remount instead - since there's no way to imperatively move an
    // already-mounted Panel, changing its `key` is what makes React tear down
    // the old instance and mount a fresh one, which drops the previous drag
    // position and falls back to the framework's own default placement.
    // resetPanelPositionCounter increments by 1 in C# every time the button is
    // pressed - it's a counter rather than a bool specifically so a *second*
    // press is still observable (a bool stuck at `true` wouldn't change again).
    const [panelKey, setPanelKey] = useState(0);
    const lastResetCounter = useRef(resetPanelPositionCounter);
    useEffect(() => {
        if (resetPanelPositionCounter !== lastResetCounter.current) {
            lastResetCounter.current = resetPanelPositionCounter;
            setPanelKey((key) => key + 1);
        }
    }, [resetPanelPositionCounter]);

    return (
        <Panel
            key={panelKey}
            className={`${styles.panel} ${visible ? "" : styles.hidden}`}
            header={
                <div className={styles.header}>
                    <span>Measure It! for CS2</span>
                </div>
            }
            draggable
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
                {summary.pointCount > 1 && (
                    <SummaryRow
                        label={`Straight line (${summary.distanceUnitSymbol})`}
                        value={summary.straightLineDistance}
                        tooltip="Direct distance from the first point to the last point."
                    />
                )}
            </div>

            {segments.length > 0 && (
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
            )}

            <div className={styles.actions}>
                <Button variant="flat" onSelect={() => trigger(GROUP, "clearPoints")}>
                    Clear all
                </Button>
            </div>

            <div className={styles.hint}>Left-click: add point &nbsp;|&nbsp; Right-click: undo last point</div>
        </Panel>
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
