using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace TweaksAndFixes
{
    internal static class GGShipgenSpeed
    {
        private const float SmallCraftGlobalSpeedMinReductionKnots = 2f;

        private static int _generateRandomShipState = -1;
        private static Il2CppSystem.Random? _rnd;
        private static readonly Dictionary<string, float> _originalSmallCraftSpeedMins = new();

        private static bool IsVanillaBaselineShipgen()
        {
            return Patch_Ship.UseVanillaShipgenBaseline() && _generateRandomShipState >= 0;
        }

        internal static void ApplyGlobalSmallCraftSpeedMinRelaxation(GameData gameData)
        {
            if (gameData?.shipTypes == null)
                return;

            foreach (string shipTypeName in new[] { "dd" })
            {
                if (!gameData.shipTypes.TryGetValue(shipTypeName, out ShipType shipType) || shipType == null)
                    continue;

                if (!_originalSmallCraftSpeedMins.TryGetValue(shipTypeName, out float originalSpeedMin))
                {
                    originalSpeedMin = shipType.speedMin;
                    _originalSmallCraftSpeedMins[shipTypeName] = originalSpeedMin;
                }

                float relaxedSpeedMin = Mathf.Max(0f, originalSpeedMin - SmallCraftGlobalSpeedMinReductionKnots);
                if (Mathf.Approximately(shipType.speedMin, relaxedSpeedMin))
                    continue;

                // Patch intent: treat DD minimum speed as a global rules tweak,
                // not just a generator workaround. Early destroyer hulls need
                // the extra displacement headroom in shipgen, and the designer
                // UI should show the same lower legal minimum. Do not apply this
                // to TBs: DIP data already lowers their class minimum heavily,
                // while vanilla's TB generator speed is driven by hull speedLimiter.
                shipType.speedMin = relaxedSpeedMin;
                Melon<TweaksAndFixes>.Logger.Msg($"GG global ship type speed minimum: {shipTypeName.ToUpperInvariant()} {originalSpeedMin:0.#}kn -> {shipType.speedMin:0.#}kn");
            }
        }

        private static float GetMaxSpeed(Ship ship)
        {
            return Mathf.Min(ship.shipType.speedMax, ship.hull.data.speedLimiter * GetMaxSpeedMult(ship)) * ShipM.KnotsToMS;
        }

        private static float GetMaxSpeedMult(Ship ship)
        {
            float year = ship.GetYear(ship);
            float mult = ship.shipType.name == "cl" || ship.shipType.name == "tb" || ship.shipType.name == "dd" ? 1f : 1.05f;
            mult *= Util.Remap(year, 1890f, 1940f, 1.05f, 1.0f, true);
            if (_rnd != null)
                mult *= Util.Range(0.95f, 1.05f, _rnd);

            return mult;
        }

        private static void ClampAndNormalizeSpeed(Ship ship, ref float speedMax)
        {
            if (ship == null || ship.hull?.data == null || ship.shipType == null || speedMax <= 0f)
                return;

            // Patch intent: only cap unrealistic high generated speeds. Early
            // DD/TB hulls can become overweight when forced up to a class
            // minimum speed, so keep vanilla's low-speed choices intact.
            float maxSpeed = GetMaxSpeed(ship);
            if (speedMax > maxSpeed)
                speedMax = maxSpeed;

            float speedKnots = speedMax / ShipM.KnotsToMS;
            float wholeKnots = Mathf.Floor(speedKnots);
            if (wholeKnots <= 0f)
                return;

            wholeKnots = Mathf.Min(wholeKnots, ship.shipType.speedMax);
            speedMax = wholeKnots * ShipM.KnotsToMS;
        }

        [HarmonyPatch(typeof(Ship))]
        internal class Patch_SetSpeedMax
        {
            [HarmonyPatch(nameof(Ship.SetSpeedMax))]
            [HarmonyPrefix]
            internal static void Prefix(Ship __instance, ref float speedMax)
            {
                // Patch intent: keep vanilla's speed selection/reduction flow,
                // but cap generated speeds to TAF's hull-aware maximum and
                // normalize to whole knots. Do not enforce a minimum speed;
                // weight-fragile early DD/TB hulls need the generator's lower
                // speed choices to remain available.
                if (!IsVanillaBaselineShipgen())
                    return;

                if (Patch_BattleManager_d115._ShipGenInfo.customSpeed > 0f)
                    return;

                ClampAndNormalizeSpeed(__instance, ref speedMax);
            }
        }

        [HarmonyPatch(typeof(Ship._GenerateRandomShip_d__573))]
        internal class Patch_GenerateRandomShipSpeedLifecycle
        {
            [HarmonyPatch(nameof(Ship._GenerateRandomShip_d__573.MoveNext))]
            [HarmonyPrefix]
            internal static void Prefix(Ship._GenerateRandomShip_d__573 __instance)
            {
                if (!Patch_Ship.UseVanillaShipgenBaseline())
                    return;

                // Patch intent: expose the active vanilla shipgen state and RNG to the speed
                // clamp while MoveNext is running. The copied TAF speed multipliers use the same
                // coroutine RNG for their small variation when it is available.
                _generateRandomShipState = __instance.__1__state;
                _rnd = __instance.__8__1?.rnd;
            }

            [HarmonyPatch(nameof(Ship._GenerateRandomShip_d__573.MoveNext))]
            [HarmonyPostfix]
            internal static void Postfix()
            {
                _generateRandomShipState = -1;
                _rnd = null;
            }

            [HarmonyPatch(nameof(Ship._GenerateRandomShip_d__573.MoveNext))]
            [HarmonyFinalizer]
            internal static void Finalizer()
            {
                _generateRandomShipState = -1;
                _rnd = null;
            }
        }
    }
}
