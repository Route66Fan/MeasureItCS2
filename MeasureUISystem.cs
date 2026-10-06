using System.Collections.Generic;
using System.Text;
using Colossal.UI.Binding;
using Game.Input;
using Game.Tools;
using Game.UI;
using Unity.Mathematics;
using UnityEngine;
using GameMode = Game.GameMode;

namespace MeasureItCS2.Systems
{
    /// <summary>
    /// Bridges MeasureToolSystem (ECS/game-side state) to the React panel (Source/UI).
    /// This replaces everything ModManager.cs used to do by hand with UILabel/UIPanel/
    /// UIDragHandle: instead of imperatively pushing text into labels every Update(),
    /// we expose ValueBindings the UI subscribes to, and TriggerBindings the UI calls
    /// into (button clicks, dropdown changes) - the standard CS2 "game-ui" pattern.
    ///
    /// NOTE: segments/summary are sent as plain JSON strings (ValueBinding&lt;string&gt;)
    /// rather than as custom struct/array types. Binding a custom struct array directly
    /// (e.g. ValueBinding&lt;MeasurementRow[]&gt;) hits a runtime crash in this SDK
    /// version: ValueWriters.Create() tries to reflectively instantiate
    /// Colossal.UI.Binding.ArrayWriter&lt;T&gt; via a parameterless constructor, which
    /// that class doesn't have for custom element types (it needs an explicit element
    /// writer). Sending JSON strings sidesteps that entirely - string is a directly
    /// supported binding type - and the React side just JSON.parses it.
    /// </summary>
    public partial class MeasureUISystem : UISystemBase
    {
        private const string Group = "measureItCS2";

        // Was a public mutable static bool - any other loaded mod could flip it via
        // a direct reference, which is more write access than this really needs to
        // grant. A private field behind a public method keeps the same external
        // capability (request a reset) without letting anyone directly assign
        // arbitrary values to our internal state.
        private static bool s_ResetPanelPositionRequested;

        /// <summary>Called from Setting's "Reset Panel Position" button.</summary>
        public static void RequestResetPanelPosition()
        {
            s_ResetPanelPositionRequested = true;
        }

        private MeasureToolSystem m_MeasureToolSystem;
        private ToolSystem m_ToolSystem;
        private DefaultToolSystem m_DefaultToolSystem;
        private Game.Rendering.CameraUpdateSystem m_CameraUpdateSystem;

        // Bound to the custom keybinds declared in Setting.cs (ToggleToolBinding /
        // UndoLastPointBinding). Confirmed via ModSetting's own decompiled
        // RegisterKeyBindings()/CreateBinding(): each [SettingsUIKeyboardBinding]
        // property gets registered as a real input action whose map name is the
        // Setting instance's own "id" (public property) and whose action name
        // defaults to the property's name - so InputManager.FindAction(Mod.Settings.id,
        // nameof(Setting.XBinding)) retrieves the exact ProxyAction the framework
        // created for it. These are our own mod's actions (not shared built-ins), so
        // enabling them directly is safe.
        private ProxyAction m_ToggleToolAction;
        private ProxyAction m_UndoLastPointAction;
        private ProxyAction m_AddPointAction;

        private ValueBinding<bool> m_ToolActive;
        private ValueBinding<int> m_UnitOfDistance;
        private ValueBinding<int> m_UnitOfSlope;
        private ValueBinding<int> m_UnitOfDirection;
        private ValueBinding<string> m_SegmentsJson;
        private ValueBinding<string> m_SummaryJson;
        private ValueBinding<bool> m_PanelPlaced;
        private ValueBinding<float> m_PanelX;
        private ValueBinding<float> m_PanelY;
        private ValueBinding<string> m_PointLabelsJson;
        private ValueBinding<string> m_PointColorHex;
        private ValueBinding<bool> m_SnapToNodes;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_MeasureToolSystem = World.GetOrCreateSystemManaged<MeasureToolSystem>();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_DefaultToolSystem = World.GetOrCreateSystemManaged<DefaultToolSystem>();
            m_CameraUpdateSystem = World.GetOrCreateSystemManaged<Game.Rendering.CameraUpdateSystem>();

            m_ToggleToolAction = InputManager.instance.FindAction(Mod.Settings.id, nameof(Setting.ToggleToolBinding));
            m_UndoLastPointAction = InputManager.instance.FindAction(Mod.Settings.id, nameof(Setting.UndoLastPointBinding));
            m_AddPointAction = InputManager.instance.FindAction(Mod.Settings.id, nameof(Setting.AddPointBinding));
            m_ToggleToolAction.shouldBeEnabled = true;
            m_UndoLastPointAction.shouldBeEnabled = true;
            m_AddPointAction.shouldBeEnabled = true;

            AddBinding(m_ToolActive = new ValueBinding<bool>(Group, "toolActive", false));
            AddBinding(m_UnitOfDistance = new ValueBinding<int>(Group, "unitOfDistance", (int)Mod.Settings.UnitOfDistance));
            AddBinding(m_UnitOfSlope = new ValueBinding<int>(Group, "unitOfSlope", (int)Mod.Settings.UnitOfSlope));
            AddBinding(m_UnitOfDirection = new ValueBinding<int>(Group, "unitOfDirection", (int)Mod.Settings.UnitOfDirection));
            AddBinding(m_SegmentsJson = new ValueBinding<string>(Group, "segmentsJson", "[]"));
            AddBinding(m_SummaryJson = new ValueBinding<string>(Group, "summaryJson", EmptySummaryJson()));
            AddBinding(m_PanelPlaced = new ValueBinding<bool>(Group, "panelPlaced", false));
            AddBinding(m_PanelX = new ValueBinding<float>(Group, "panelX", 0f));
            AddBinding(m_PanelY = new ValueBinding<float>(Group, "panelY", 0f));
            AddBinding(m_PointLabelsJson = new ValueBinding<string>(Group, "pointLabelsJson", "[]"));
            AddBinding(m_PointColorHex = new ValueBinding<string>(Group, "pointColorHex", MeasureMath.ColorHex(Mod.Settings.MeasureColor)));
            AddBinding(m_SnapToNodes = new ValueBinding<bool>(Group, "snapToNodes", Mod.Settings.SnapToNodes));

            // Triggers the React side calls into.
            AddBinding(new TriggerBinding(Group, "toggleTool", ToggleTool));
            AddBinding(new TriggerBinding(Group, "undoLastPoint", () => m_MeasureToolSystem.RemoveLastPoint()));
            AddBinding(new TriggerBinding(Group, "clearPoints", () => m_MeasureToolSystem.ClearPoints()));
            AddBinding(new TriggerBinding<int>(Group, "setUnitOfDistance", value =>
            {
                Mod.Settings.UnitOfDistance = (DistanceUnit)value;
                Mod.Settings.ApplyAndSave();
            }));
            AddBinding(new TriggerBinding<int>(Group, "setUnitOfSlope", value =>
            {
                Mod.Settings.UnitOfSlope = (SlopeUnit)value;
                Mod.Settings.ApplyAndSave();
            }));
            AddBinding(new TriggerBinding<int>(Group, "setUnitOfDirection", value =>
            {
                Mod.Settings.UnitOfDirection = (DirectionUnit)value;
                Mod.Settings.ApplyAndSave();
            }));
            AddBinding(new TriggerBinding<bool>(Group, "setSnapToNodes", value =>
            {
                Mod.Settings.SnapToNodes = value;
                Mod.Settings.ApplyAndSave();
            }));
            AddBinding(new TriggerBinding<float, float>(Group, "setPanelPosition", (x, y) =>
            {
                // Sent by the panel when a drag ends: its top-left corner as fractions
                // of the screen. Saved into the slot for whichever screen (city or
                // editor) is loaded right now, so each remembers its own spot.
                if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(y) || float.IsInfinity(y))
                {
                    return;
                }

                x = math.clamp(x, 0f, 1f);
                y = math.clamp(y, 0f, 1f);

                if (IsEditorMode())
                {
                    Mod.Settings.EditorPanelPlaced = true;
                    Mod.Settings.EditorPanelX = x;
                    Mod.Settings.EditorPanelY = y;
                }
                else
                {
                    Mod.Settings.CityPanelPlaced = true;
                    Mod.Settings.CityPanelX = x;
                    Mod.Settings.CityPanelY = y;
                }

                Mod.Settings.ApplyAndSave();
            }));
        }

        private void ToggleTool()
        {
            if (m_ToolSystem.activeTool == m_MeasureToolSystem)
            {
                // Confirmed via ToolSystem's decompiled IL: the framework's own "revert
                // to no tool" logic assigns its cached DefaultToolSystem instance to
                // activeTool, never null. Setting activeTool = null left ToolSystem's
                // activePrefab getter (and anything reading it, like the built-in
                // ToolbarUISystem/ToolUISystem) dereferencing a null tool reference,
                // which is what threw the NullReferenceException on the second Ctrl+M
                // press.
                m_ToolSystem.activeTool = m_DefaultToolSystem;
            }
            else
            {
                m_ToolSystem.activeTool = m_MeasureToolSystem;
            }
        }

        /// <summary>
        /// True while the editor (rather than a city) is the loaded scene.
        /// ToolSystem.actionMode is simply the Game.GameMode the current scene was
        /// loaded in (confirmed via IL), so it tells city from editor.
        /// </summary>
        private bool IsEditorMode()
        {
            return (m_ToolSystem.actionMode & GameMode.Editor) != 0;
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();

            bool active = m_ToolSystem.activeTool == m_MeasureToolSystem;

            if (m_ToggleToolAction.WasPressedThisFrame())
            {
                ToggleTool();
                active = m_ToolSystem.activeTool == m_MeasureToolSystem;
            }

            if (active && m_UndoLastPointAction.WasPressedThisFrame())
            {
                m_MeasureToolSystem.RemoveLastPoint();
            }

            if (active && m_AddPointAction.WasPressedThisFrame() && m_MeasureToolSystem.HasCursorPosition)
            {
                m_MeasureToolSystem.AddPoint(m_MeasureToolSystem.CursorPosition);
            }

            m_ToolActive.Update(active);

            m_UnitOfDistance.Update((int)Mod.Settings.UnitOfDistance);
            m_UnitOfSlope.Update((int)Mod.Settings.UnitOfSlope);
            m_UnitOfDirection.Update((int)Mod.Settings.UnitOfDirection);
            m_PointColorHex.Update(MeasureMath.ColorHex(Mod.Settings.MeasureColor));
            m_SnapToNodes.Update(Mod.Settings.SnapToNodes);

            if (s_ResetPanelPositionRequested)
            {
                // "Reset Panel Position": forget the remembered spot on BOTH screens.
                // The window then falls back to its default, centered position as
                // soon as the cleared flags reach it through the bindings below - no
                // remount trick needed, since its position now comes from these
                // settings rather than from the game's own Panel internals.
                Mod.Settings.CityPanelPlaced = false;
                Mod.Settings.EditorPanelPlaced = false;
                Mod.Settings.ApplyAndSave();
                s_ResetPanelPositionRequested = false;
            }

            // Publish the remembered position for whichever screen is loaded now, so
            // the window follows the right slot when you move between a city and the
            // editor. Updated before the inactive early-out below on purpose, so it's
            // already correct by the time the tool is toggled on.
            bool editorMode = IsEditorMode();
            m_PanelPlaced.Update(editorMode ? Mod.Settings.EditorPanelPlaced : Mod.Settings.CityPanelPlaced);
            m_PanelX.Update(editorMode ? Mod.Settings.EditorPanelX : Mod.Settings.CityPanelX);
            m_PanelY.Update(editorMode ? Mod.Settings.EditorPanelY : Mod.Settings.CityPanelY);

            if (!active)
            {
                m_PointLabelsJson.Update("[]");
                return;
            }

            var points = m_MeasureToolSystem.Points;

            // Project each point's 3D world position to 2D screen space so the
            // React side can render a floating number label at each one -
            // OverlayRenderSystem.Buffer only has DrawCircle/DrawLine (confirmed via
            // IL), no text-drawing method, so labels have to be plain HTML
            // positioned over the game view rather than drawn in the 3D overlay.
            //
            // Prefers the camera the game itself treats as active:
            // CameraUpdateSystem.activeCamera is activeViewer?.camera (confirmed via
            // IL) - the same source the tool raycast uses, so it follows the right
            // camera in both the game and the editor, whereas Camera.main depends on
            // which camera happens to carry the "MainCamera" tag in each mode. Falls
            // back to Camera.main (what this used exclusively before, known to work
            // in-game) when there's no active viewer yet.
            var pointLabelsBuilder = new StringBuilder();
            pointLabelsBuilder.Append('[');

            Camera projectionCamera = m_CameraUpdateSystem.activeCamera;
            if (projectionCamera == null)
            {
                projectionCamera = Camera.main;
            }

            if (projectionCamera != null)
            {
                bool firstLabel = true;

                for (int i = 0; i < points.Count; i++)
                {
                    // Fully qualified on purpose: if anything in the project also imports
                    // System.Numerics (an editor "add using" quick fix can add that line
                    // without anyone noticing), a bare Vector3 becomes ambiguous between
                    // System.Numerics.Vector3 and UnityEngine.Vector3 and the build fails
                    // with CS0104. Spelling out the Unity type can't be ambiguous.
                    UnityEngine.Vector3 screenPoint = projectionCamera.WorldToScreenPoint(new UnityEngine.Vector3(points[i].x, points[i].y, points[i].z));

                    // WorldToScreenPoint's Y is bottom-up (Unity convention) and its Z
                    // is distance in front of the camera (negative/zero = behind).
                    bool visible = screenPoint.z > 0f;
                    float screenX = screenPoint.x;
                    float screenY = Screen.height - screenPoint.y;

                    if (!firstLabel)
                    {
                        pointLabelsBuilder.Append(',');
                    }
                    firstLabel = false;

                    pointLabelsBuilder.Append('{');
                    pointLabelsBuilder.Append("\"index\":").Append(i).Append(',');
                    pointLabelsBuilder.Append("\"x\":").Append(screenX.ToString("0.#")).Append(',');
                    pointLabelsBuilder.Append("\"y\":").Append(screenY.ToString("0.#")).Append(',');
                    pointLabelsBuilder.Append("\"visible\":").Append(visible ? "true" : "false");
                    pointLabelsBuilder.Append('}');
                }
            }

            pointLabelsBuilder.Append(']');
            m_PointLabelsJson.Update(pointLabelsBuilder.ToString());

            var segmentsBuilder = new StringBuilder();
            segmentsBuilder.Append('[');

            float totalDistance = 0f;
            bool firstRow = true;

            for (int i = 1; i < points.Count; i++)
            {
                float3 a = points[i - 1];
                float3 b = points[i];

                float relief = MeasureMath.Relief(a, b);
                float length = MeasureMath.LengthXZ(a, b);
                float distance = MeasureMath.Distance3D(a, b);
                float slope = MeasureMath.SlopeRatio(relief, length);
                float direction = MeasureMath.Direction(a, b);

                totalDistance += distance;

                if (!firstRow)
                {
                    segmentsBuilder.Append(',');
                }
                firstRow = false;

                segmentsBuilder.Append('{');
                segmentsBuilder.Append("\"index\":").Append(i).Append(',');
                AppendJsonStringProperty(segmentsBuilder, "relief", MeasureMath.DisplayDistance(Mod.Settings.UnitOfDistance, relief));
                segmentsBuilder.Append(',');
                AppendJsonStringProperty(segmentsBuilder, "distance", MeasureMath.DisplayDistance(Mod.Settings.UnitOfDistance, distance));
                segmentsBuilder.Append(',');
                AppendJsonStringProperty(segmentsBuilder, "slope", MeasureMath.DisplaySlope(Mod.Settings.UnitOfSlope, slope));
                segmentsBuilder.Append(',');
                AppendJsonStringProperty(segmentsBuilder, "direction", MeasureMath.DisplayDirection(Mod.Settings.UnitOfDirection, direction));
                segmentsBuilder.Append('}');
            }

            segmentsBuilder.Append(']');
            m_SegmentsJson.Update(segmentsBuilder.ToString());

            string elevation = points.Count > 0
                ? MeasureMath.DisplayDistance(Mod.Settings.UnitOfDistance, points[0].y)
                : "0";

            string totalDistanceStr = MeasureMath.DisplayDistance(Mod.Settings.UnitOfDistance, totalDistance);

            string straightLineStr = "0";
            if (points.Count > 1)
            {
                float straightLine = MeasureMath.Distance3D(points[0], points[points.Count - 1]);
                straightLineStr = MeasureMath.DisplayDistance(Mod.Settings.UnitOfDistance, straightLine);
            }

            var summaryBuilder = new StringBuilder();
            summaryBuilder.Append('{');
            summaryBuilder.Append("\"pointCount\":").Append(points.Count).Append(',');
            AppendJsonStringProperty(summaryBuilder, "elevation", elevation);
            summaryBuilder.Append(',');
            AppendJsonStringProperty(summaryBuilder, "totalDistance", totalDistanceStr);
            summaryBuilder.Append(',');
            AppendJsonStringProperty(summaryBuilder, "straightLineDistance", straightLineStr);
            summaryBuilder.Append(',');
            AppendJsonStringProperty(summaryBuilder, "distanceUnitSymbol", MeasureMath.UnitSymbol(Mod.Settings.UnitOfDistance));
            summaryBuilder.Append(',');
            AppendJsonStringProperty(summaryBuilder, "slopeUnitSymbol", MeasureMath.UnitSymbol(Mod.Settings.UnitOfSlope));
            summaryBuilder.Append(',');
            AppendJsonStringProperty(summaryBuilder, "directionUnitSymbol", MeasureMath.UnitSymbol(Mod.Settings.UnitOfDirection));
            summaryBuilder.Append('}');

            m_SummaryJson.Update(summaryBuilder.ToString());
        }

        private static string EmptySummaryJson()
        {
            return "{\"pointCount\":0,\"elevation\":\"0\",\"totalDistance\":\"0\"," +
                   "\"straightLineDistance\":\"0\",\"distanceUnitSymbol\":\"m\",\"slopeUnitSymbol\":\"%\",\"directionUnitSymbol\":\"\\u00b0\"}";
        }

        /// <summary>Appends "key":"value" to a JSON string being built by hand, escaping the value.</summary>
        private static void AppendJsonStringProperty(StringBuilder sb, string key, string value)
        {
            sb.Append('"').Append(key).Append("\":\"");
            AppendJsonEscaped(sb, value ?? string.Empty);
            sb.Append('"');
        }

        private static void AppendJsonEscaped(StringBuilder sb, string value)
        {
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
        }
    }
}