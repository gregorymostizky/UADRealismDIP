using HarmonyLib;
using Il2Cpp;

namespace TweaksAndFixes
{
    internal static class GGShipgenLifecycle
    {
        private const int MaxShipgenAttempts = 3;

        private static void ApplyAttemptCap(Ship._GenerateRandomShip_d__573 routine)
        {
            // Patch intent: vanilla-baseline shipgen bypasses the old TAF
            // MoveNext mutation path, so enforce the retry budget from the
            // active GG lifecycle hook instead.
            if (routine == null || routine._triesTotal_5__4 <= 0)
                return;

            routine._triesTotal_5__4 = Math.Min(routine._triesTotal_5__4, MaxShipgenAttempts);
        }

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

                ApplyAttemptCap(__instance);

                // Patch intent: expose vanilla shipgen activity to small GG modules
                // without re-enabling the disabled TAF GenerateRandomShip override path.
                GGShipgenContext.EnterGenerateRandomShipState(__instance.__1__state);

                // Patch intent: vanilla can mutate individual armor zones after
                // our data-driven GenerateArmor pass without necessarily calling
                // the reduce/fill methods we also hook. Normalize once more right
                // before final validation so both accepted and rejected attempts
                // are evaluated and logged with coherent armor.
                if (__instance.__1__state == 11)
                    GGShipgenWeightRescue.NormalizeArmorForShipgen(__instance.__4__this, "validate");

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
