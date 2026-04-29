using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using System.Reflection;

namespace TweaksAndFixes
{
    [HarmonyPatch(typeof(PlayerController))]
    internal class Patch_PlayerController
    {
        [HarmonyPatch(nameof(PlayerController.CloneShipRaw))]
        [HarmonyPostfix]
        internal static void Postfix_CloneShipRaw(Ship from, bool willBeDesign, ref Ship __result)
        {
            if (from != null && __result != null)
                __result.TAFData().OnClonePost(from.TAFData());

            // Patch intent: refits created from an erased base design can inherit
            // the Erased status through CloneShipRaw's store roundtrip. New design
            // clones should be live designs, otherwise the design list immediately
            // hides them as deleted designs with zero ships.
            if (willBeDesign && from != null && __result != null && from.isErased && __result.isErased)
            {
                __result.SetStatus(VesselEntity.Status.Normal);
                Melon<TweaksAndFixes>.Logger.Msg($"Design clone status normalized from erased source: {from.Name(false, false, false, false, true)} -> {__result.Name(false, false, false, false, true)}");
            }
        }
    }

    [HarmonyPatch]
    internal class Patch_PlayerController_CanBuildShipsFromDesign
    {
        internal static IEnumerable<MethodBase> TargetMethods()
        {
            Type reasonType = typeof(string).MakeByRefType();

            MethodInfo method = AccessTools.Method(
                typeof(PlayerController),
                nameof(PlayerController.CanBuildShipsFromDesign),
                new[] { typeof(Ship), reasonType });
            if (method != null)
                yield return method;

            method = AccessTools.Method(
                typeof(PlayerController),
                nameof(PlayerController.CanBuildShipsFromDesign),
                new[] { typeof(Ship), typeof(int), reasonType });
            if (method != null)
                yield return method;
        }

        [HarmonyPrefix]
        internal static bool Prefix(Ship design, ref bool __result)
        {
            if (design == null || !design.isRefitDesign)
                return true;

            __result = true;
            return false;
        }
    }
}
