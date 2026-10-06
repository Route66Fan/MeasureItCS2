using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;
using Game.Input;
using Game.UI.Widgets;
using Game.UI.Localization;
using MeasureItCS2.Systems;
using UnityEngine;

namespace MeasureItCS2
{
    /// <summary>
    /// Replaces ModConfig.cs (persisted values) and the OnSettingsUI() method from the
    /// CS1 ModInfo.cs (the options-menu widgets). CS2's Settings framework generates the
    /// options UI from these attributes instead of hand-built UIHelperBase calls.
    /// </summary>
    [FileLocation(nameof(MeasureItCS2))]
    [SettingsUIGroupOrder(GroupPanel, GroupKeybinds)]
    [SettingsUIShowGroupName(GroupPanel, GroupKeybinds)]
    public class Setting : ModSetting
    {
        public const string GroupPanel = "Panel";
        public const string GroupKeybinds = "Keybinds";

        public Setting(IMod mod) : base(mod)
        {
            SetDefaults();
        }

        // Unit settings are hidden from the Options page - they're already
        // changeable directly from the in-game panel's own dropdowns, so a
        // duplicate "Measurements" section here was redundant. The properties
        // themselves stay (still persisted, still read by MeasureUISystem/
        // MeasureToolSystem, still settable via the panel's trigger bindings) -
        // [SettingsUIHidden] just keeps them off the auto-generated Options UI.
        [SettingsUIHidden]
        public DistanceUnit UnitOfDistance { get; set; }

        [SettingsUIHidden]
        public SlopeUnit UnitOfSlope { get; set; }

        [SettingsUIHidden]
        public DirectionUnit UnitOfDirection { get; set; }

        [SettingsUIDropdown(typeof(Setting), nameof(GetMeasureColorItems))]
        [SettingsUISection(GroupPanel)]
        public MeasureColorOption MeasureColor { get; set; }

        // Lets clicks snap to actual network node positions (roads, pipes, tracks,
        // subway lines) rather than only the raycast hit surface. This is what makes
        // measuring to/through buried pipes, tunnels, and submerged crossings work:
        // a Game.Net.Node's position is its real designed 3D position regardless of
        // terrain occlusion or view mode, so clicking near the surface above a
        // buried pipe can still snap precisely to that pipe's actual depth.
        // Hidden from Options (same treatment as the unit settings above) since it's
        // now toggleable directly from the in-game panel instead.
        [SettingsUIHidden]
        public bool SnapToNodes { get; set; }

        // Where the readout window was last dragged to, remembered separately for the
        // in-game (city) screen and the editor, since the two screens lay out their
        // own UI differently. Stored as fractions (0..1) of the screen's width and
        // height rather than pixels, so a saved position survives a change of
        // resolution or UI scale. The "Placed" flags tell "never moved" (use the
        // default, centered spot) apart from a real position - 0,0 is a legitimate
        // spot - and a bool also defaults to false for anyone whose saved settings
        // predate these properties. Hidden from Options: the window itself is the UI
        // for these (drag it); Options -> Reset Panel Position clears them.
        [SettingsUIHidden]
        public bool CityPanelPlaced { get; set; }

        [SettingsUIHidden]
        public float CityPanelX { get; set; }

        [SettingsUIHidden]
        public float CityPanelY { get; set; }

        [SettingsUIHidden]
        public bool EditorPanelPlaced { get; set; }

        [SettingsUIHidden]
        public float EditorPanelX { get; set; }

        [SettingsUIHidden]
        public float EditorPanelY { get; set; }

        [SettingsUIButton]
        [SettingsUISection(GroupPanel)]
        public bool ResetPanelPosition
        {
            set
            {
                MeasureUISystem.RequestResetPanelPosition();
            }
        }

        [SettingsUIKeyboardBinding(BindingKeyboard.M, ctrl: true)]
        [SettingsUISection(GroupKeybinds)]
        public ProxyBinding ToggleToolBinding { get; set; }

        [SettingsUIKeyboardBinding(BindingKeyboard.Z, ctrl: true)]
        [SettingsUISection(GroupKeybinds)]
        public ProxyBinding AddPointBinding { get; set; }

        [SettingsUIKeyboardBinding(BindingKeyboard.X, ctrl: true)]
        [SettingsUISection(GroupKeybinds)]
        public ProxyBinding UndoLastPointBinding { get; set; }

        public override void SetDefaults()
        {
            UnitOfDistance = DistanceUnit.Meters;
            UnitOfSlope = SlopeUnit.Percentage;
            UnitOfDirection = DirectionUnit.Point;
            MeasureColor = MeasureColorOption.Yellow;
            SnapToNodes = true;
            CityPanelPlaced = false;
            CityPanelX = 0f;
            CityPanelY = 0f;
            EditorPanelPlaced = false;
            EditorPanelX = 0f;
            EditorPanelY = 0f;
        }

        public DropdownItem<int>[] GetMeasureColorItems()
        {
            return new[]
            {
                new DropdownItem<int> { value = (int)MeasureColorOption.Yellow, displayName = LocalizedString.Value("Yellow") },
                new DropdownItem<int> { value = (int)MeasureColorOption.Magenta, displayName = LocalizedString.Value("Magenta") },
                new DropdownItem<int> { value = (int)MeasureColorOption.Red, displayName = LocalizedString.Value("Red") },
                new DropdownItem<int> { value = (int)MeasureColorOption.Green, displayName = LocalizedString.Value("Green") },
                new DropdownItem<int> { value = (int)MeasureColorOption.Cyan, displayName = LocalizedString.Value("Cyan") },
                new DropdownItem<int> { value = (int)MeasureColorOption.White, displayName = LocalizedString.Value("White") },
                new DropdownItem<int> { value = (int)MeasureColorOption.Orange, displayName = LocalizedString.Value("Orange") },
            };
        }
    }

    /// <summary>Distance units. Feet/Meters map 1:1 to the CS1 options; Kilometers and
    /// Miles are the new ones requested for CS2.</summary>
    public enum DistanceUnit
    {
        Meters = 0,
        Kilometers = 1,
        Feet = 2,
        Miles = 3,
    }

    public enum SlopeUnit
    {
        Degree = 0,
        Percentage = 1,
    }

    public enum DirectionUnit
    {
        Degree = 0,
        Point = 1,
    }

    /// <summary>Preset colors for the placed points/lines. No native color-picker
    /// widget exists in this Settings framework (checked - no SettingsUIColor
    /// attribute or similar in Game.dll), so this is a curated preset list using the
    /// same dropdown pattern as the unit settings above.</summary>
    public enum MeasureColorOption
    {
        Yellow = 0,
        Magenta = 1,
        Red = 2,
        Green = 3,
        Cyan = 4,
        White = 5,
        Orange = 6,
    }
}