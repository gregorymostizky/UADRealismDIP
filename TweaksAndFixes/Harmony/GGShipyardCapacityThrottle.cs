using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace TweaksAndFixes
{
    internal static class GGShipyardCapacityThrottle
    {
        private enum WorkKind
        {
            Repair = 0,
            Build = 1,
            Refit = 2
        }

        private sealed class WorkItem
        {
            public Ship Ship = null!;
            public WorkKind Kind;
            public float Tonnage;
            public float MonthsRemaining;
        }

        internal sealed class Plan
        {
            public readonly List<PausedState> PausedShips = new();
        }

        internal readonly struct PausedState
        {
            public readonly Ship Ship;
            public readonly bool IsBuildingPaused;
            public readonly bool IsRepairingPaused;
            public readonly bool IsRefitPaused;

            public PausedState(Ship ship)
            {
                Ship = ship;
                IsBuildingPaused = ship.isBuildingPaused;
                IsRepairingPaused = ship.isRepairingPaused;
                IsRefitPaused = ship.isRefitPaused;
            }
        }

        internal static Plan? BeginAdvance(Player player, bool prewarm)
        {
            if (prewarm || player == null)
                return null;

            List<WorkItem> work = BuildWorkList(player);
            if (work.Count == 0)
                return null;

            float capacity = SafeCapacity(player);
            if (capacity <= 0f)
                return null;

            float activeTonnage = 0f;
            foreach (WorkItem item in work)
                activeTonnage += item.Tonnage;

            if (activeTonnage <= capacity + 0.5f)
                return null;

            HashSet<string> allowed = new();
            float remainingCapacity = capacity;

            foreach (WorkItem item in work
                         .OrderBy(item => item.Kind == WorkKind.Repair ? 0 : 1)
                         .ThenBy(item => item.MonthsRemaining)
                         .ThenByDescending(item => item.Tonnage))
            {
                // Patch intent: continue scanning after an oversized ship so smaller jobs can use
                // leftover shipyard capacity instead of being blocked by list order.
                if (item.Tonnage <= remainingCapacity + 0.5f)
                {
                    allowed.Add(ShipKey(item.Ship));
                    remainingCapacity -= item.Tonnage;
                }
            }

            Plan plan = new();

            foreach (WorkItem item in work)
            {
                if (allowed.Contains(ShipKey(item.Ship)))
                    continue;

                plan.PausedShips.Add(new PausedState(item.Ship));

                // Patch intent: use vanilla's own paused-work filters as the capacity throttle.
                // This prevents progress from being written, so no completion/status side effects
                // need to be rolled back after the fact.
                switch (item.Kind)
                {
                    case WorkKind.Repair:
                        item.Ship.isRepairingPaused = true;
                        break;
                    case WorkKind.Refit:
                        item.Ship.isRefitPaused = true;
                        break;
                    default:
                        item.Ship.isBuildingPaused = true;
                        break;
                }
            }

            if (plan.PausedShips.Count == 0)
                return null;

            if (!player.isAi)
            {
                Melon<TweaksAndFixes>.Logger.Msg(
                    $"Shipyard throttle: {player.Name(false)}, capacity={capacity:0}t, active={activeTonnage:0}t, delayed={plan.PausedShips.Count}");
            }

            return plan;
        }

        internal static void EndAdvance(Plan? plan)
        {
            if (plan == null)
                return;

            foreach (PausedState state in plan.PausedShips)
            {
                if (state.Ship == null)
                    continue;

                state.Ship.isBuildingPaused = state.IsBuildingPaused;
                state.Ship.isRepairingPaused = state.IsRepairingPaused;
                state.Ship.isRefitPaused = state.IsRefitPaused;
            }
        }

        private static List<WorkItem> BuildWorkList(Player player)
        {
            List<WorkItem> work = new();

            foreach (Ship ship in player.GetFleetAll())
            {
                if (ship == null || ship.isDesign || ship.isSunk || ship.isScrapped || ship.isErased)
                    continue;

                WorkItem? item = TryCreateWorkItem(ship);
                if (item != null)
                    work.Add(item);
            }

            return work;
        }

        private static WorkItem? TryCreateWorkItem(Ship ship)
        {
            try
            {
                if (ship.isRepairing && !ship.isRepairingPaused)
                {
                    return new WorkItem
                    {
                        Ship = ship,
                        Kind = WorkKind.Repair,
                        Tonnage = SafeTonnage(ship),
                        MonthsRemaining = RemainingMonths(ship.repairingProgress, ship.RepairingTime(false))
                    };
                }

                if (ship.isBuilding && !ship.isBuildingPaused)
                {
                    return new WorkItem
                    {
                        Ship = ship,
                        Kind = WorkKind.Build,
                        Tonnage = SafeTonnage(ship),
                        MonthsRemaining = RemainingMonths(ship.buildingProgress, ship.BuildingTime(false))
                    };
                }

                if (ship.isRefit && !ship.isRefitPaused)
                {
                    return new WorkItem
                    {
                        Ship = ship,
                        Kind = WorkKind.Refit,
                        Tonnage = SafeTonnage(ship),
                        MonthsRemaining = RemainingMonths(ship.refitProgress, ship.DesignRefitTime(false))
                    };
                }
            }
            catch (Exception ex)
            {
                Melon<TweaksAndFixes>.Logger.Warning($"Shipyard throttle skipped ship work item. {ex.GetType().Name}: {ex.Message}");
            }

            return null;
        }

        private static float RemainingMonths(float progress, float totalMonths)
        {
            if (totalMonths <= 0f)
                return float.MaxValue;

            float remainingProgress = Math.Max(0f, 100f - progress);
            return remainingProgress * totalMonths / 100f;
        }

        private static float SafeCapacity(Player player)
        {
            try { return player.ShipbuildingCapacityLimit(); }
            catch { return 0f; }
        }

        private static float SafeTonnage(Ship ship)
        {
            try { return Math.Max(0f, ship.Tonnage()); }
            catch { return 0f; }
        }

        private static string ShipKey(Ship ship)
        {
            try { return ship.id.ToString(); }
            catch { return ship.GetHashCode().ToString(); }
        }
    }

    [HarmonyPatch(typeof(CampaignController))]
    internal static class Patch_GGShipyardCapacityThrottle_CampaignController
    {
        [HarmonyPatch(nameof(CampaignController.AdvanceShips))]
        [HarmonyPrefix]
        internal static void Prefix_AdvanceShips(Player player, bool prewarm, out GGShipyardCapacityThrottle.Plan? __state)
        {
            __state = GGShipyardCapacityThrottle.BeginAdvance(player, prewarm);
        }

        [HarmonyPatch(nameof(CampaignController.AdvanceShips))]
        [HarmonyPostfix]
        internal static void Postfix_AdvanceShips(GGShipyardCapacityThrottle.Plan? __state)
        {
            GGShipyardCapacityThrottle.EndAdvance(__state);
        }
    }

    [HarmonyPatch(typeof(Player))]
    internal static class Patch_GGShipyardCapacityThrottle_Player
    {
        [HarmonyPatch(nameof(Player.TimePenalty))]
        [HarmonyPrefix]
        internal static bool Prefix_TimePenalty(ref float __result)
        {
            // Patch intent: remove the global over-capacity cost/time multiplier. The replacement
            // behavior is per-shipyard-work throttling in GGShipyardCapacityThrottle.
            __result = 1f;
            return false;
        }
    }
}
