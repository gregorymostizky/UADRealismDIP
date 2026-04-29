using Il2Cpp;

namespace TweaksAndFixes
{
    internal static class GGShipgenTBGunClamp
    {
        private const float EarlyTbGunMaxBarrelInches = 14f;
        private const float ImprovedTbGunMaxBarrelInches = 18f;

        private static bool IsTinyEarlyTbHull(Ship ship)
        {
            string? hull = ship?.hull?.data?.name;
            string? model = ship?.hull?.data?.model;

            return model == "jap_tb_hull"
                || hull == "tb_lowbow"
                || hull == "tb_standard"
                || hull == "tb_highbow";
        }

        private static float GunBarrelInches(Ship ship, Part ignoredPart)
        {
            if (ship?.parts == null)
                return 0f;

            float total = 0f;
            IntPtr ignoredPointer = ignoredPart != null ? ignoredPart.Pointer : IntPtr.Zero;

            foreach (Part part in ship.parts)
            {
                if (part?.data == null || !part.data.isGun)
                    continue;

                if (ignoredPointer != IntPtr.Zero && part.Pointer == ignoredPointer)
                    continue;

                total += part.data.GetCaliberInch(ship) * part.data.barrels;
            }

            return total;
        }

        private static bool HasResearchedTb500TonTech(Ship ship)
        {
            if (ship?.player?.technologies == null)
                return false;

            foreach (Technology tech in ship.player.technologies)
            {
                if (tech?.data?.name == "hull_destroyer_2" && tech.isResearched)
                    return true;
            }

            return false;
        }

        private static bool HasResearchedTb700TonTech(Ship ship)
        {
            if (ship?.player?.technologies == null)
                return false;

            foreach (Technology tech in ship.player.technologies)
            {
                if (tech?.data?.name == "hull_destroyer_3" && tech.isResearched)
                    return true;
            }

            return false;
        }

        internal static bool ShouldBlockEarlyTbGunPlacement(Part part)
        {
            // Patch intent: the tiny early TB hull family remains weight-fragile
            // through the 500t torpedo-boat tech. Vanilla can stack several
            // small guns and then fail on weight/barbette checks. Reject only
            // the placement that would push the combined gun battery above a
            // compact total, then stop intervening once the 700t TB tech is
            // researched.
            Ship ship = part?.ship;
            if (!Patch_Ship.UseVanillaShipgenBaseline())
                return false;

            if (ship?.shipType?.name != "tb")
                return false;

            if (part?.data == null || !part.data.isGun)
                return false;

            if (!IsTinyEarlyTbHull(ship))
                return false;

            if (HasResearchedTb700TonTech(ship))
                return false;

            float candidate = part.data.GetCaliberInch(ship) * part.data.barrels;
            float cap = HasResearchedTb500TonTech(ship) ? ImprovedTbGunMaxBarrelInches : EarlyTbGunMaxBarrelInches;
            return GunBarrelInches(ship, part) + candidate > cap + 0.001f;
        }
    }
}
