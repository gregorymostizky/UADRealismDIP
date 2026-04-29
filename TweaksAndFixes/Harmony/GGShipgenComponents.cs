using Il2Cpp;

namespace TweaksAndFixes
{
    internal static class GGShipgenComponents
    {
        private static bool InstallFirstAvailableComponent(Ship ship, params string[] keys)
        {
            if (ship?.components == null)
                return false;

            foreach (string key in keys)
            {
                if (!G.GameData.components.TryGetValue(key, out ComponentData component))
                    continue;

                if (!ship.components.ContainsKey(component.typex))
                    continue;

                if (!ship.IsComponentAvailable(component))
                    continue;

                if (ship.components[component.typex] != component)
                    ship.InstallComponent(component);

                return true;
            }

            return false;
        }

        internal static void OptimizeGeneratedComponents(Ship ship)
        {
            // Patch intent: generated vanilla-baseline designs should pick the
            // same high-value modules we would usually choose manually when tech
            // allows it, without touching vanilla part placement or ship shape.
            InstallFirstAvailableComponent(ship, "armor_10", "armor_9", "armor_8", "armor_7", "armor_6", "armor_5", "armor_4", "armor_3", "armor_2", "armor_1", "armor_0");
            InstallFirstAvailableComponent(ship, "shell_ratio_main_2");
            InstallFirstAvailableComponent(ship, "shell_ratio_sec_2");
            InstallFirstAvailableComponent(ship, "shell_S.heavy", "shell_heavy", "shell_normal");
            InstallFirstAvailableComponent(ship, "ap_5", "ap_2", "ap_1", "ap_0", "ap_4", "ap_3");
            InstallFirstAvailableComponent(ship, "he_3", "he_2", "he_0", "he_1", "he_4", "he_5");
        }
    }
}
