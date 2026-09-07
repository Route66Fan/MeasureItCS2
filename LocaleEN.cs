using System.Collections.Generic;
using Colossal;

namespace MeasureItCS2
{
    /// <summary>
    /// Supplies the English display strings for the Options UI. Without this, every
    /// label falls back to its raw locale key (e.g. the mod's own title showed as
    /// "Options.SECTION[MeasureItCS2.MeasureItCS2.Mod]" - that ".Mod" suffix comes from
    /// ModSetting's constructor keying off the IMod implementation's own type name,
    /// confirmed from Game.dll's IL: it calls mod.GetType() rather than this.GetType(),
    /// and our IMod class is literally named "Mod").
    ///
    /// Registered via GameManager.instance.localizationManager.AddSource("en-US", ...)
    /// in Mod.OnLoad. Key-building helper methods (GetSettingsLocaleID,
    /// GetOptionGroupLocaleID, GetOptionLabelLocaleID, GetOptionDescLocaleID,
    /// GetBindingKeyLocaleID) are inherited from ModSetting and produce the exact same
    /// keys the framework looks up at render time, so we never hardcode the key
    /// strings here.
    /// </summary>
    public class LocaleEN : IDictionarySource
    {
        private readonly Setting m_Setting;

        public LocaleEN(Setting setting)
        {
            m_Setting = setting;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "Measure It! for CS2" },

                // Group headers (SettingsUISection groups shown on the options page).
                { m_Setting.GetOptionGroupLocaleID(Setting.GroupPanel), "Panel" },
                { m_Setting.GetOptionGroupLocaleID(Setting.GroupKeybinds), "Keybinds" },

                // Individual option labels.
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ResetPanelPosition)), "Reset Panel Position" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ToggleToolBinding)), "Toggle Measure Tool" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.AddPointBinding)), "Add Point" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.UndoLastPointBinding)), "Undo Last Point" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.MeasureColor)), "Point/Line Color" },

                // Option descriptions (tooltips).
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ResetPanelPosition)), "Moves the measurement panel back to its default position." },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.MeasureColor)), "Color of the placed points and connecting lines on the map." },

                // Keybind rebind dialog titles ("Reassign <this>") - confirmed via
                // ModSetting's decompiled GetBindingKeyLocaleID(actionName): it builds
                // "Options.OPTION[{id}/{actionName}/Binding]", which is exactly the raw
                // key that was showing up unlocalized in the rebind popup.
                { m_Setting.GetBindingKeyLocaleID(nameof(Setting.ToggleToolBinding)), "Toggle Measure Tool" },
                { m_Setting.GetBindingKeyLocaleID(nameof(Setting.AddPointBinding)), "Add Point" },
                { m_Setting.GetBindingKeyLocaleID(nameof(Setting.UndoLastPointBinding)), "Undo Last Point" },
            };
        }

        public void Unload()
        {
        }
    }
}
