using HarmonyLib;
using Il2Cpp;

namespace TweaksAndFixes
{
    internal static class GGShipgenLifecycle
    {
        [HarmonyPatch(typeof(Ship._GenerateRandomShip_d__573))]
        internal class Patch_GenerateRandomShipLifecycle
        {
            [HarmonyPatch(nameof(Ship._GenerateRandomShip_d__573.MoveNext))]
            [HarmonyPrefix]
            internal static void Prefix(Ship._GenerateRandomShip_d__573 __instance, out int __state)
            {
                __state = __instance._tryN_5__5;

                if (!Patch_Ship.UseVanillaShipgenBaseline())
                    return;

                // Patch intent: expose vanilla shipgen activity to small GG modules
                // without re-enabling the disabled TAF GenerateRandomShip override path.
                GGShipgenContext.EnterGenerateRandomShipState(__instance.__1__state);
                GGShipgenLogging.LogShipgenStart(__instance);
            }

            [HarmonyPatch(nameof(Ship._GenerateRandomShip_d__573.MoveNext))]
            [HarmonyPostfix]
            internal static void Postfix(Ship._GenerateRandomShip_d__573 __instance, int __state, ref bool __result)
            {
                if (Patch_Ship.UseVanillaShipgenBaseline())
                {
                    GGShipgenComponents.OptimizeGeneratedComponents(__instance.__4__this);
                    GGShipgenGuns.FloorGeneratedGunCalibers(__instance.__4__this);

                    if (__state != __instance._tryN_5__5)
                        GGShipgenLogging.LogAttemptRejected(__instance, __state);

                    if (!__result || __instance.__1__state < 0)
                        GGShipgenLogging.LogShipgenEnd(__instance);
                }

                GGShipgenContext.ExitGenerateRandomShipState();
            }
        }
    }
}
