using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace TweaksAndFixes
{
    internal static class GGShipgenWeightRescue
    {
        private const float MinCoherentArmorLerp = -0.2f;
        private const float MaxCoherentArmorLerp = 1.0f;
        private const int ArmorLerpSearchIterations = 16;
        private const float MinArmorSpeedLimiterMultiplier = 0.8f;

        private static string ShipLabel(Ship ship)
        {
            string shipType = ship?.shipType?.name ?? "?";
            string hull = ship?.hull?.data?.name ?? ship?.hull?.name ?? "?";
            string model = ship?.hull?.data?.model ?? "?";
            return $"{shipType.ToUpperInvariant()} {hull}/{model}";
        }

        private static string Inches(float mm)
            => $"{mm / 25.4f:0.#}";

        private static string ArmorSummary(Ship ship)
        {
            if (ship?.armor == null)
                return "armor=?";

            float turretSide = 0f;
            float turretTop = 0f;
            float barbette = 0f;
            if (ship.shipTurretArmor != null)
            {
                foreach (var ta in ship.shipTurretArmor)
                {
                    turretSide = Mathf.Max(turretSide, ta.sideTurretArmor);
                    turretTop = Mathf.Max(turretTop, ta.topTurretArmor);
                    barbette = Mathf.Max(barbette, ta.barbetteArmor);
                }
            }

            return $"belt={Inches(ship.armor.ArmorValue(Ship.A.Belt))}/{Inches(ship.armor.ArmorValue(Ship.A.BeltBow))}/{Inches(ship.armor.ArmorValue(Ship.A.BeltStern))}in, " +
                $"deck={Inches(ship.armor.ArmorValue(Ship.A.Deck))}/{Inches(ship.armor.ArmorValue(Ship.A.DeckBow))}/{Inches(ship.armor.ArmorValue(Ship.A.DeckStern))}in, " +
                $"turret={Inches(turretSide)}/{Inches(turretTop)}/{Inches(barbette)}in";
        }

        private static void TrySpeedRescueAtMinimumArmor(Ship ship, float finalLerp, float tonnage, ref float afterWeight)
        {
            if (finalLerp > MinCoherentArmorLerp + 0.001f || afterWeight <= tonnage)
                return;

            if (ship?.hull?.data == null || ship.speedMax <= 0f)
                return;

            float currentSpeedKnots = ship.speedMax / ShipM.KnotsToMS;
            float floorSpeedKnots = Mathf.Floor(ship.hull.data.speedLimiter * MinArmorSpeedLimiterMultiplier);
            floorSpeedKnots = Mathf.Clamp(floorSpeedKnots, 1f, ship.shipType.speedMax);
            if (currentSpeedKnots <= floorSpeedKnots + 0.01f)
                return;

            float beforeSpeedKnots = currentSpeedKnots;
            float beforeWeight = afterWeight;

            // Patch intent: if coherent armor has already reached its allowed
            // minimum and the design is still overweight, speed is the next
            // least-invasive displacement lever. Use a hull-relative 80% floor
            // for all classes and leave armor at minimum instead of spending the
            // newly freed weight back into protection.
            for (float speedKnots = Mathf.Floor(currentSpeedKnots - 0.01f); speedKnots >= floorSpeedKnots; speedKnots -= 1f)
            {
                ship.SetSpeedMax(speedKnots * ShipM.KnotsToMS);
                afterWeight = ship.Weight();
                if (afterWeight <= tonnage)
                    break;
            }

            float afterSpeedKnots = ship.speedMax / ShipM.KnotsToMS;
            if (afterSpeedKnots < beforeSpeedKnots - 0.01f)
            {
                Melon<TweaksAndFixes>.Logger.Msg(
                    $"GG speed rescue: {ShipLabel(ship)}, speed={beforeSpeedKnots:0.#}kn->{afterSpeedKnots:0.#}kn, " +
                    $"floor={floorSpeedKnots:0.#}kn, weight={beforeWeight:0}t->{afterWeight:0}t/{tonnage:0}t");
            }
        }

        internal static void NormalizeArmorForShipgen(Ship ship, string source)
        {
            if (!GGShipgenContext.IsVanillaBaselineShipgen() || ship == null || ship.shipType == null || ship.IsShipWhitoutArmor())
                return;

            var info = GenArmorData.GetInfoFor(ship);
            if (info == null || ship.armor == null)
                return;

            float tonnage = ship.Tonnage();
            if (tonnage <= 0f)
                return;

            float startedAt = Time.realtimeSinceStartup;
            float beforeWeight = ship.Weight();
            float startLerp = Mathf.Clamp(info.EstimateLerp(ship), MinCoherentArmorLerp, MaxCoherentArmorLerp);

            // Patch intent: vanilla shaves or fills individual armor zones, which
            // can leave incoherent layouts. Project armor onto the copied TAF curve
            // and solve for the highest coherent lerp that still fits tonnage. The
            // negative lower bound lets very small/early hulls go below table minima
            // while preserving the same belt/deck/turret relationship.
            float lowLerp = MinCoherentArmorLerp;
            bool changed = info.SetArmor(ship, lowLerp);
            float lowWeight = ship.Weight();
            bool fits = lowWeight <= tonnage;
            float finalLerp = lowLerp;
            int iterations = 0;

            if (fits)
            {
                float highLerp = MaxCoherentArmorLerp;
                changed |= info.SetArmor(ship, highLerp);
                if (ship.Weight() <= tonnage)
                {
                    finalLerp = highLerp;
                }
                else
                {
                    for (int i = 0; i < ArmorLerpSearchIterations; i++)
                    {
                        iterations++;
                        float testLerp = (lowLerp + highLerp) * 0.5f;
                        changed |= info.SetArmor(ship, testLerp);
                        if (ship.Weight() <= tonnage)
                        {
                            lowLerp = testLerp;
                            finalLerp = testLerp;
                        }
                        else
                        {
                            highLerp = testLerp;
                        }
                    }
                }
            }

            changed |= info.SetArmor(ship, finalLerp);
            float afterWeight = ship.Weight();
            TrySpeedRescueAtMinimumArmor(ship, finalLerp, tonnage, ref afterWeight);
            bool finalFits = afterWeight <= tonnage;
            string result = finalFits ? (changed ? "applied" : "unchanged") : "overweight";

            float elapsedMs = (Time.realtimeSinceStartup - startedAt) * 1000f;
            Melon<TweaksAndFixes>.Logger.Msg(
                $"GG armor normalize: {ShipLabel(ship)}, source={source}, result={result}, " +
                $"lerp={startLerp:0.###}->{finalLerp:0.###}, iterations={iterations}, " +
                $"weight={beforeWeight:0}t->{afterWeight:0}t/{tonnage:0}t, elapsed={elapsedMs:0.0}ms, {ArmorSummary(ship)}");
        }

        [HarmonyPatch(typeof(Ship))]
        internal class Patch_ReduceWeightByReducingCharacteristics
        {
            [HarmonyPatch(nameof(Ship.ReduceWeightByReducingCharacteristics))]
            [HarmonyPostfix]
            internal static void Postfix(Ship __instance)
            {
                NormalizeArmorForShipgen(__instance, "reduce");
            }
        }

        [HarmonyPatch(typeof(Ship))]
        internal class Patch_AddedAdditionalTonnageUsage
        {
            [HarmonyPatch(nameof(Ship.AddedAdditionalTonnageUsage))]
            [HarmonyPostfix]
            internal static void Postfix(Ship __instance)
            {
                NormalizeArmorForShipgen(__instance, "fill");
            }
        }
    }
}
