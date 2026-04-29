namespace TweaksAndFixes
{
    internal static class GGShipgenContext
    {
        private static int _generateRandomShipState = -1;

        internal static bool IsVanillaBaselineShipgen()
        {
            return Patch_Ship.UseVanillaShipgenBaseline()
                && (_generateRandomShipState >= 0 || Patch_Ship.IsVanillaShipgenBaselineActive());
        }

        internal static void EnterGenerateRandomShipState(int state)
        {
            _generateRandomShipState = state;
        }

        internal static void ExitGenerateRandomShipState()
        {
            _generateRandomShipState = -1;
        }
    }
}
