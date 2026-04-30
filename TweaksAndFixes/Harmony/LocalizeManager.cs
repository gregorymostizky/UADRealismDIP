using MelonLoader;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace TweaksAndFixes
{
    [HarmonyPatch(typeof(LocalizeManager))]
    internal class Patch_LocalizeManager
    {
        private static readonly HashSet<string> _SeenKeys = new HashSet<string>();

        private static bool _Initialized = false;

        private static int LoadLocFromFile(LocalizeManager.LanguagesData __result, FilePath file, bool clobber)
        {
            if (!file.Exists)
                return -1;

            var lines = File.ReadAllLines(file.path);

            for (int j = 0; j < lines.Length; ++j)
            {
                var line = lines[j];
                var split = line.Split(';');
                if (split.Length < 2)
                {
                    Melon<TweaksAndFixes>.Logger.Error($"Error loading language file {file.name}, line {j + 1} `{line}` lacks key or value");
                    continue;
                }

                string key = split[0];
                if (_SeenKeys.Contains(key))
                {
                    Melon<TweaksAndFixes>.Logger.Error($"Error loading language file {file.name}, line {j + 1} `{line}` is a duplicate key");
                    continue;
                }
                _SeenKeys.Add(key);
                if (!clobber && __result.Data.ContainsKey(key))
                    continue;

                string[] newArr = new string[split.Length - 1];
                for (int i = 1; i < split.Length; ++i)
                    newArr[i - 1] = LocalizeManager.__c.__9__24_0.Invoke(split[i]);

                __result.Data[key] = newArr;
            }
            int count = _SeenKeys.Count;
            _SeenKeys.Clear();
            return count;
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(LocalizeManager.LoadLanguage))]
        internal static void Postfix_LoadLanguage(LocalizeManager __instance, string currentLanguage, ref LocalizeManager.LanguagesData __result)
        {
            if (!_Initialized)
            {
                _Initialized = true;
            }
            else
            {
                return;
            }

                int overrideCount = LoadLocFromFile(__result, new FilePath(FilePath.DirType.ModsDir, currentLanguage + ".lng"), true);
            if(overrideCount >= 0)
                Melon<TweaksAndFixes>.Logger.Msg($"Overriding language {currentLanguage} with {overrideCount} lines");

            if (LoadLocFromFile(__result, Config._LocFile, false) < 0)
                Melon<TweaksAndFixes>.Logger.Error($"Unable to find base TAF loc file {Config._LocFile} in {Config._DataDir}");
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(LocalizeManager))]
        [HarmonyPatch("Localize", new Type[]
        {
            typeof(string),
            typeof(Il2CppReferenceArray<Il2CppSystem.Object>)
        })]
        internal static bool Prefix_LocalizeCampaignShipCounts(string tag, Il2CppReferenceArray<Il2CppSystem.Object> p, ref string __result)
        {
            if (p == null || GameManager.Instance == null || !GameManager.IsWorldMap)
                return true;

            int commissioning = CountMainPlayerCommissioningShips();
            if (commissioning < 0)
                return true;

            // Patch intent: expose commissioning ships in the campaign status summaries. Vanilla
            // advances commissioning separately from construction, but the right-side world-map
            // panel only shows active/building/repair/refit counts.
            if (tag == "$Ui_World_MapWindow_Building0shipsDP" && p.Length >= 1)
            {
                __result = $"Building {ArgString(p, 0)} ships, commissioning {commissioning}:";
                return false;
            }

            if (tag == "$Ui_World_Finances_ShipsActiveBuildingRepairingRefit" && p.Length >= 6)
            {
                string colorOpen = ArgString(p, 4);
                string colorClose = ArgString(p, 5);
                __result = $"{colorOpen}{ArgString(p, 0)}{colorClose} ships active ({colorOpen}{ArgString(p, 1)}{colorClose} building, {colorOpen}{commissioning}{colorClose} commissioning, {colorOpen}{ArgString(p, 2)}{colorClose} repairing, {colorOpen}{ArgString(p, 3)}{colorClose} refit)";
                return false;
            }

            return true;
        }

        private static int CountMainPlayerCommissioningShips()
        {
            try
            {
                Player player = ExtraGameData.MainPlayer();
                if (player == null)
                    return -1;

                int count = 0;
                foreach (Ship ship in player.GetFleetAll())
                {
                    if (ship != null && !ship.isDesign && ship.isCommissioning)
                        count++;
                }

                return count;
            }
            catch
            {
                return -1;
            }
        }

        private static string ArgString(Il2CppReferenceArray<Il2CppSystem.Object> p, int index)
        {
            try
            {
                if (p == null || index < 0 || index >= p.Length || p[index] == null)
                    return string.Empty;

                return p[index].ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(LocalizeManager))]
        [HarmonyPatch("Localize", new Type[]
        {
            typeof(string),
            typeof(Il2CppReferenceArray<Il2CppSystem.Object>)
        })]
        internal static void FixLocalize(ref string tag, Il2CppReferenceArray<Il2CppSystem.Object> p)
        {
            if (tag == "$tooltip_campaign_new_game_fleet_creation" &&
                LocalizeManager.Instance != null &&
                LocalizeManager.Instance.Language.Data.ContainsKey("$TAF_tooltip_campaign_new_game_fleet_creation"))
            {
                tag = "$TAF_tooltip_campaign_new_game_fleet_creation";
                return;
            }

            if (GameManager.Instance != null && GameManager.IsConstructor && 
                (p == null || ((Il2CppArrayBase<Il2CppSystem.Object>)(object)p).Length <= 0) && 
                !(tag != "$Ui_World_PopWindows_Port") && LocalizeManager.Instance.Language.Data.ContainsKey("$TAF_Ui_Constr_Port")
            )
            {
                tag = "$TAF_Ui_Constr_Port";
            }
        }
    }
}
