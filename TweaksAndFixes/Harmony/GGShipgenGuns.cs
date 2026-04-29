using HarmonyLib;
using Il2Cpp;
using UnityEngine;

namespace TweaksAndFixes
{
    internal static class GGShipgenGuns
    {
        private static float FloorGunDiameterModifier(PartData data, float diameterModifier)
        {
            if (data == null || !data.isGun)
                return diameterModifier;

            float caliberMm = data.caliber + diameterModifier;
            float caliberInches = caliberMm * (1f / 25.4f);
            float flooredInches = Mathf.Max(1f, Mathf.Floor(caliberInches + 0.001f));
            return flooredInches * 25.4f - data.caliber;
        }

        internal static void FloorGeneratedGunCalibers(Ship ship)
        {
            if (ship == null || ship.shipGunCaliber == null)
                return;

            foreach (Ship.TurretCaliber caliber in ship.shipGunCaliber)
            {
                if (caliber == null || caliber.turretPartData == null)
                    continue;

                float flooredDiameter = FloorGunDiameterModifier(caliber.turretPartData, caliber.diameter);
                if (Mathf.Abs(flooredDiameter - caliber.diameter) > 0.001f)
                    ship.SetCaliberDiameter(caliber, flooredDiameter);
            }
        }

        [HarmonyPatch(typeof(Ship))]
        internal class Patch_SetCaliberModifiers
        {
            [HarmonyPatch(nameof(Ship.SetCaliberDiameter))]
            [HarmonyPrefix]
            internal static void Prefix_SetCaliberDiameter(Ship.TurretCaliber __0, ref float __1)
            {
                // Patch intent: keep vanilla's gun choice but floor generated diameter
                // modifiers to the nearest lower whole-inch caliber. This avoids rejecting
                // otherwise useful gun candidates and leaves manual designer edits alone.
                if (GGShipgenContext.IsVanillaBaselineShipgen() && __0?.turretPartData != null)
                    __1 = FloorGunDiameterModifier(__0.turretPartData, __1);
            }
        }
    }
}
