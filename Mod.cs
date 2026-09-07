using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using MeasureItCS2.Systems;

namespace MeasureItCS2
{
    /// <summary>
    /// Entry point for "Measure It for CS2". Mirrors the role that ModInfo/Loading
    /// played in the CS1 version: registers settings, wires up the ECS systems that
    /// replace the old MonoBehaviour tool + info panel, and cleans up on unload.
    /// </summary>
    public class Mod : IMod
    {
        public static readonly string Id = "MeasureItCS2";

        public static ILog Log = LogManager.GetLogger(Id).SetShowsErrorsInUI(false);

        public static Setting Settings { get; private set; }

        public void OnLoad(UpdateSystem updateSystem)
        {
            Log.Info($"[{Id}] OnLoad");

            // Settings (equivalent of ModConfig.cs / the OnSettingsUI() options page in CS1).
            Settings = new Setting(this);
            Settings.RegisterInOptionsUI();
            Settings.RegisterKeyBindings();

            AssetDatabase.global.LoadSettings(Id, Settings, new Setting(this));

            // Registers the display strings the Options UI looks up (mod title, group
            // headers, option labels/descriptions). Without this, every label falls
            // back to showing its raw locale key instead of readable text.
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(Settings));

            // Register the ECS systems. UpdateSystem.UpdateAt schedules them into the
            // game's phase loop the same way CS1 relied on Unity's Update()/OnToolGUI().
            updateSystem.UpdateAt<MeasureToolSystem>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateAt<MeasureUISystem>(SystemUpdatePhase.UIUpdate);
        }

        public void OnDispose()
        {
            Log.Info($"[{Id}] OnDispose");

            if (Settings != null)
            {
                Settings.UnregisterInOptionsUI();
                Settings = null;
            }
        }
    }
}
