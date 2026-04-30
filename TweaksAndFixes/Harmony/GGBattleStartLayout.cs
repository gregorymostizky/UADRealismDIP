using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace TweaksAndFixes
{
    internal static class GGBattleStartLayout
    {
        private sealed class ShipEntry
        {
            public Ship Ship = null!;
            public string Type = string.Empty;
            public int Priority;
            public float Tonnage;
        }

        private static readonly Dictionary<string, int> ClassPriority = new()
        {
            ["bb"] = 0,
            ["bc"] = 1,
            ["ca"] = 2,
            ["cl"] = 3,
            ["dd"] = 4,
            ["tb"] = 5,
        };

        private const int MaxSupportScoutDivisions = 6;
        private const float ThreatScreenFraction = 0.30f;
        private const float FallbackBehindDistance = 1800f;
        private const float ScoutDivisionSpacing = 450f;
        private const float ScoutShipSpacing = 260f;
        private const float ThreatAssignmentRefreshSeconds = 1f;

        private static readonly HashSet<IntPtr> PlayerMainDivisions = new();
        private static readonly Dictionary<IntPtr, ScoutDivisionInfo> PlayerScoutDivisions = new();
        private static readonly Dictionary<IntPtr, IntPtr> ScoutThreatAssignments = new();
        private static Player CurrentPlayer;
        private static float LastThreatAssignmentRefresh = -999f;

        private sealed class ScoutDivisionInfo
        {
            public Division Division = null!;
            public int Index;
            public int Count;
        }

        internal static void ApplyPlayerLayout()
        {
            try
            {
                ClearBattleLayoutState();
                if (!GGAdvancedBattleAIOption.Enabled)
                    return;

                Player player = ExtraGameData.MainPlayer();
                if (player == null || DivisionsManager.Instance?.CurrentDivisions == null)
                    return;

                List<Division> playerDivisions = CurrentPlayerDivisions(player);
                List<ShipEntry> entries = CombatShips(playerDivisions, player);
                if (entries.Count <= 1)
                    return;

                List<ShipEntry> transports = TransportShips(playerDivisions, player);
                SplitMainAndSupport(entries, out List<ShipEntry> mains, out List<ShipEntry> supports);
                if (mains.Count == 0)
                    return;

                ErasePlayerDivisions(playerDivisions);

                Vector3 enemyDirection = DirectionToEnemy(entries, player);

                Division mainDivision = CreateDivision("main battle line", mains, Division.Formation.Column, clearOrders: true);
                if (mainDivision == null)
                    return;

                CurrentPlayer = player;
                PlayerMainDivisions.Add(mainDivision.Pointer);

                int supportGroupCount = DesiredSupportGroupCount(player, supports.Count);
                List<Division> createdSupports = CreateSupportDivisions(mainDivision, supports, supportGroupCount);
                Division transportDivision = transports.Count > 0 ? CreateDivision("transport", transports, Division.Formation.Column, clearOrders: true) : null;
                RegisterScoutDivisions(createdSupports);
                UpdateMainPlayerDivisions(mainDivision, createdSupports, transportDivision);
                ArrangeBattleStart(mainDivision, createdSupports, transportDivision, enemyDirection);

                Melon<TweaksAndFixes>.Logger.Msg(
                    $"GG battle layout: player={player.Name(false)}, main={DescribeDivision(mainDivision)}, " +
                    $"supports={string.Join("; ", createdSupports.Select(DescribeDivision))}, transports={DescribeDivision(transportDivision)}");
            }
            catch (Exception ex)
            {
                Melon<TweaksAndFixes>.Logger.Warning($"GG battle layout skipped. {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void ClearBattleLayoutState()
        {
            PlayerMainDivisions.Clear();
            PlayerScoutDivisions.Clear();
            ScoutThreatAssignments.Clear();
            CurrentPlayer = null;
            LastThreatAssignmentRefresh = -999f;
        }

        private static List<Division> CurrentPlayerDivisions(Player player)
        {
            List<Division> divisions = new();
            foreach (Division division in DivisionsManager.Instance.CurrentDivisions)
            {
                if (division?.ships == null)
                    continue;

                foreach (Ship ship in division.ships)
                {
                    if (ship?.player == player)
                    {
                        divisions.Add(division);
                        break;
                    }
                }
            }

            return divisions;
        }

        private static List<ShipEntry> CombatShips(List<Division> divisions, Player player)
        {
            Dictionary<IntPtr, ShipEntry> ships = new();

            foreach (Division division in divisions)
            {
                if (division?.ships == null)
                    continue;

                foreach (Ship ship in division.ships)
                {
                    if (ship == null || ship.player != player || IsTransport(ship))
                        continue;

                    IntPtr key = ship.Pointer;
                    if (ships.ContainsKey(key))
                        continue;

                    string type = ShipTypeName(ship);
                    ships[key] = new ShipEntry
                    {
                        Ship = ship,
                        Type = type,
                        Priority = ClassPriority.TryGetValue(type, out int priority) ? priority : 99,
                        Tonnage = SafeTonnage(ship)
                    };
                }
            }

            return SortEntries(ships.Values).ToList();
        }

        private static List<ShipEntry> TransportShips(List<Division> divisions, Player player)
        {
            Dictionary<IntPtr, ShipEntry> ships = new();

            foreach (Division division in divisions)
            {
                if (division?.ships == null)
                    continue;

                foreach (Ship ship in division.ships)
                {
                    if (ship == null || ship.player != player || !IsTransport(ship))
                        continue;

                    IntPtr key = ship.Pointer;
                    if (ships.ContainsKey(key))
                        continue;

                    ships[key] = new ShipEntry
                    {
                        Ship = ship,
                        Type = ShipTypeName(ship),
                        Priority = 100,
                        Tonnage = SafeTonnage(ship)
                    };
                }
            }

            return SortEntries(ships.Values).ToList();
        }

        private static void SplitMainAndSupport(List<ShipEntry> entries, out List<ShipEntry> mains, out List<ShipEntry> supports)
        {
            bool hasHeavy = entries.Any(entry => entry.Priority <= ClassPriority["ca"]);
            if (hasHeavy)
            {
                mains = SortEntries(entries.Where(entry => entry.Priority <= ClassPriority["ca"])).ToList();
                supports = SortEntries(entries.Where(entry => entry.Priority > ClassPriority["ca"])).ToList();
                return;
            }

            List<int> knownPriorities = entries
                .Where(entry => entry.Priority < 99)
                .Select(entry => entry.Priority)
                .Distinct()
                .OrderBy(priority => priority)
                .ToList();

            if (knownPriorities.Count <= 1)
            {
                mains = SortEntries(entries).ToList();
                supports = new List<ShipEntry>();
                return;
            }

            int mainPriority = knownPriorities[0];
            mains = SortEntries(entries.Where(entry => entry.Priority == mainPriority)).ToList();
            supports = SortEntries(entries.Where(entry => entry.Priority != mainPriority)).ToList();
        }

        private static int DesiredSupportGroupCount(Player player, int supportShipCount)
        {
            if (supportShipCount <= 0)
                return 0;

            int torpedoThreatCount = EnemyTorpedoDivisions(player).Count;
            if (torpedoThreatCount <= 0)
                return 1;

            return Math.Max(1, Math.Min(Math.Min(MaxSupportScoutDivisions, supportShipCount), torpedoThreatCount));
        }

        private static List<Division> CreateSupportDivisions(Division mainDivision, List<ShipEntry> supports, int desiredGroups)
        {
            List<Division> created = new();
            if (supports.Count == 0)
                return created;

            List<List<ShipEntry>> groups = SplitSupportScoutGroups(supports, desiredGroups);
            for (int i = 0; i < groups.Count; i++)
            {
                Division support = CreateDivision($"scout support {i + 1}", groups[i], Division.Formation.Column, clearOrders: false);
                if (support == null)
                    continue;

                // Patch intent: vanilla scout orders already know how to fan
                // divisions around their target. Give the engine several small,
                // mostly same-class scout divisions instead of forcing our own
                // forward/left/right slot map.
                support.SetScoutDivision(mainDivision);
                created.Add(support);
            }

            return created;
        }

        private static List<List<ShipEntry>> SplitSupportScoutGroups(List<ShipEntry> supports, int desiredGroups)
        {
            List<List<ShipEntry>> groups = supports
                .GroupBy(entry => entry.Type)
                .OrderBy(group => ClassPriority.TryGetValue(group.Key, out int priority) ? priority : 99)
                .Select(group => SortEntries(group).ToList())
                .Where(group => group.Count > 0)
                .ToList();

            desiredGroups = Math.Max(1, Math.Min(Math.Min(MaxSupportScoutDivisions, supports.Count), desiredGroups));
            while (groups.Count > desiredGroups)
            {
                int mergeIndex = groups.Count - 2;
                groups[mergeIndex].AddRange(groups[mergeIndex + 1]);
                groups[mergeIndex] = SortEntries(groups[mergeIndex]).ToList();
                groups.RemoveAt(mergeIndex + 1);
            }

            while (groups.Count < desiredGroups)
            {
                int splitIndex = -1;
                int splitSize = 1;

                for (int i = 0; i < groups.Count; i++)
                {
                    if (groups[i].Count > splitSize)
                    {
                        splitIndex = i;
                        splitSize = groups[i].Count;
                    }
                }

                if (splitIndex < 0)
                    break;

                List<ShipEntry> group = groups[splitIndex];
                int firstCount = (int)Math.Ceiling(group.Count / 2d);
                groups[splitIndex] = group.Take(firstCount).ToList();
                groups.Insert(splitIndex + 1, group.Skip(firstCount).ToList());
            }

            return groups;
        }

        private static void RegisterScoutDivisions(List<Division> supports)
        {
            PlayerScoutDivisions.Clear();
            for (int i = 0; i < supports.Count; i++)
            {
                Division support = supports[i];
                if (support == null)
                    continue;

                PlayerScoutDivisions[support.Pointer] = new ScoutDivisionInfo
                {
                    Division = support,
                    Index = i,
                    Count = supports.Count
                };
            }
        }

        private static Division CreateDivision(string label, List<ShipEntry> entries, Division.Formation formation, bool clearOrders)
        {
            if (entries.Count == 0)
                return null;

            Il2CppSystem.Collections.Generic.List<Ship> ships = ToIl2CppList(entries.Select(entry => entry.Ship));
            Division division = DivisionsManager.Create(ships, null);
            if (division == null)
                return null;

            // Patch intent: player battle starts should be readable at a glance.
            // The main combatants are the battle line division, but vanilla's
            // Column formation is the sequential line-ahead behavior where each
            // ship follows the previous ship. Formation.Line fans ships around
            // the leader, which creates tree-like follow targets.
            division.formation = formation;
            division.spread = Division.Spread.Normal;
            division.Reorder(ships);
            NormalizeDivisionState(division, ships, clearOrders);

            Melon<TweaksAndFixes>.Logger.Msg($"GG battle layout division: {label}, {DescribeDivision(division)}");
            return division;
        }

        private static void NormalizeDivisionState(Division division, Il2CppSystem.Collections.Generic.List<Ship> ships, bool clearOrders)
        {
            // Patch intent: after rebuilding divisions post-PrepareBattle, make
            // the internal division state look like vanilla had created this
            // order in the first place. Reorder updates the list, but existing
            // ships may still carry stale formation indices from their old
            // division, which can produce tree-like follow relationships.
            for (int i = 0; i < ships.Count; i++)
            {
                Ship ship = ships[i];
                if (ship == null)
                    continue;

                ship.ColumnFormationPointIdx = i;
                ship.OldDivisionPos = i;
            }

            division.FollowDivision = null;
            division.FollowingDivision = null;
            division.ScoutDivision = null;
            division.ScreenDivision = null;
            division.WhoScoutMe = null;
            division.WhoScreenMe = null;
            if (clearOrders)
                division.ClearOrders();

            division.RecalculateInitialSpeed();
            division.AfterInit();
            NotifyDivisionOrderChanged(division);
        }

        private static Vector3 DirectionToEnemy(List<ShipEntry> playerShips, Player player)
        {
            Vector3 playerCenter = AveragePosition(playerShips.Select(entry => entry.Ship));
            List<Ship> enemies = new();

            foreach (Ship ship in Ship.AllShips)
            {
                if (ship == null || ship.player == null || ship.player == player || IsTransport(ship))
                    continue;

                enemies.Add(ship);
            }

            Vector3 enemyCenter = AveragePosition(enemies);
            Vector3 direction = enemyCenter - playerCenter;
            direction.y = 0f;

            if (direction.sqrMagnitude < 1f)
            {
                Ship first = playerShips.FirstOrDefault()?.Ship;
                direction = first != null ? first.transform.forward : Vector3.forward;
                direction.y = 0f;
            }

            return direction.sqrMagnitude > 1f ? direction.normalized : Vector3.forward;
        }

        private static Vector3 AveragePosition(IEnumerable<Ship> ships)
        {
            Vector3 total = Vector3.zero;
            int count = 0;

            foreach (Ship ship in ships)
            {
                if (ship == null)
                    continue;

                total += ship.transform.position;
                count++;
            }

            return count > 0 ? total / count : Vector3.zero;
        }

        private static Vector3 AveragePosition(Il2CppSystem.Collections.Generic.List<Ship> ships)
        {
            Vector3 total = Vector3.zero;
            int count = 0;

            if (ships == null)
                return total;

            foreach (Ship ship in ships)
            {
                if (ship == null)
                    continue;

                total += ship.transform.position;
                count++;
            }

            return count > 0 ? total / count : Vector3.zero;
        }

        private static List<Division> EnemyTorpedoDivisions(Player player)
        {
            List<Division> threats = new();
            if (DivisionsManager.Instance?.CurrentDivisions == null)
                return threats;

            foreach (Division division in DivisionsManager.Instance.CurrentDivisions)
            {
                if (division?.ships == null || !DivisionHasEnemyTorpedoes(division, player))
                    continue;

                threats.Add(division);
            }

            return threats
                .OrderBy(division => DistanceSquared(AveragePosition(division.ships), PlayerMainCenter()))
                .ToList();
        }

        private static bool DivisionHasEnemyTorpedoes(Division division, Player player)
        {
            if (division?.ships == null)
                return false;

            foreach (Ship ship in division.ships)
            {
                if (ship == null || !ship.isAlive || ship.player == null || ship.player == player)
                    continue;

                if (ship.haveTorpedoes || IsSmallEnemyCombatant(ship))
                    return true;
            }

            return false;
        }

        private static bool IsSmallEnemyCombatant(Ship ship)
        {
            string type = ShipTypeName(ship);
            return type == "cl" || type == "dd" || type == "tb";
        }

        private static Vector3 PlayerMainCenter()
        {
            foreach (IntPtr pointer in PlayerMainDivisions)
            {
                Division division = FindDivision(pointer);
                if (division?.ships != null)
                    return AveragePosition(division.ships);
            }

            return Vector3.zero;
        }

        private static void ArrangeBattleStart(Division mainDivision, List<Division> supports, Division transportDivision, Vector3 enemyDirection)
        {
            if (mainDivision?.ships == null || mainDivision.ships.Count == 0)
                return;

            Vector3 center = AveragePosition(mainDivision.ships);
            Vector3 forward = enemyDirection.sqrMagnitude > 1f ? enemyDirection.normalized : mainDivision.ships[0].transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 1f ? forward.normalized : Vector3.forward;
            Vector3 right = new(forward.z, 0f, -forward.x);

            // Patch intent: the rebuilt player divisions should start already
            // facing the enemy. This keeps the initial physical layout from
            // being overwritten by a stale vanilla movement/line-abreast order
            // as soon as the battle clock starts.
            PlaceDivisionLineAhead(mainDivision, center, forward, 450f);
            GiveInitialBattleLineOrder(mainDivision, forward);

            foreach (Division support in supports)
            {
                if (support?.ships == null || support.ships.Count == 0)
                    continue;

                PlaceDivisionLineAhead(support, AveragePosition(support.ships), forward, 330f);
            }

            if (transportDivision != null)
                PlaceDivisionLineAhead(transportDivision, center - forward * 2200f, forward, 450f);
        }

        private static void GiveInitialBattleLineOrder(Division mainDivision, Vector3 forward)
        {
            if (mainDivision?.ships == null || mainDivision.ships.Count == 0)
                return;

            // Patch intent: UIDivision only shows "Battle Line" for a clean
            // division when Division.IsMoving() is true. Physical placement and
            // velocity are not enough to set that vanilla movement state, so
            // issue a simple forward movement order after rebuilding the main
            // line. This mirrors the state created when the player clicks once
            // without changing the scout/support relationships.
            forward.y = 0f;
            if (forward.sqrMagnitude < 1f)
                forward = mainDivision.ships[0]?.transform.forward ?? Vector3.forward;

            forward.y = 0f;
            if (forward.sqrMagnitude < 1f)
                forward = Vector3.forward;

            mainDivision.MoveDir(forward.normalized, clearOrders: true);
            NotifyDivisionOrderChanged(mainDivision);
        }

        private static void PlaceDivisionLineAhead(Division division, Vector3 leadPosition, Vector3 forward, float spacing)
        {
            if (division?.ships == null)
                return;

            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
            for (int i = 0; i < division.ships.Count; i++)
            {
                Ship ship = division.ships[i];
                if (ship == null)
                    continue;

                ship.transform.position = leadPosition - forward * spacing * i;
                ship.transform.rotation = rotation;
                ship.ColumnFormationPointIdx = i;
                ship.OldDivisionPos = i;
                ship.RemoveCustomMoveTo();
            }

            division.RecalculateInitialSpeed();
            division.AfterInit();
            NormalizeDivisionSpeed(division);
            NotifyDivisionOrderChanged(division);
            division.UIElement?.RefreshUI();
        }

        private static void NormalizeDivisionSpeed(Division division)
        {
            if (division?.ships == null || division.ships.Count == 0)
                return;

            float slowest = float.PositiveInfinity;
            foreach (Ship ship in division.ships)
            {
                if (ship == null)
                    continue;

                float speed = 0f;
                try { speed = ship.SpeedMax(true, false, true); }
                catch { speed = ship.speedMax; }

                if (speed > 0f && speed < slowest)
                    slowest = speed;
            }

            if (float.IsInfinity(slowest) || slowest <= 0f)
                return;

            // Patch intent: vanilla battle spawn can leave individual ships
            // with different desired speeds. Normalize each rebuilt division
            // to its slowest ship so faster rear ships do not immediately run
            // into slower leaders when the battle clock starts.
            foreach (Ship ship in division.ships)
            {
                if (ship == null)
                    continue;

                ship.SetEngineCustomSpeed(slowest);
                ship.savedDesiredSpeed = slowest;
                ship.savedCurrentSpeed = Math.Min(ship.savedCurrentSpeed > 0f ? ship.savedCurrentSpeed : slowest, slowest);
                ship.InitialSetVelocity(ship.transform.forward * slowest);
            }
        }

        private static void NotifyDivisionOrderChanged(Division division)
        {
            try { division?.OnDivisionOrderChanged?.Invoke(); }
            catch { }

            try { division?.UIElement?.RefreshUI(); }
            catch { }

            try { UIDivision.Sort(true); }
            catch { }
        }

        private static void UpdateMainPlayerDivisions(Division mainDivision, List<Division> supports, Division transportDivision)
        {
            if (DivisionsManager.Instance == null)
                return;

            Il2CppSystem.Collections.Generic.List<Division> ordered = new();
            if (mainDivision != null)
                ordered.Add(mainDivision);

            foreach (Division support in supports)
            {
                if (support != null)
                    ordered.Add(support);
            }

            if (transportDivision != null)
                ordered.Add(transportDivision);

            // Patch intent: the battle UI uses MainPlayerDivisions for the
            // player-side division order/status. Make the rebuilt main line the
            // first player division so it starts as the visible Battle Line
            // instead of inheriting stale status from erased vanilla divisions.
            DivisionsManager.Instance.MainPlayerDivisions = ordered;
            DivisionsManager.Instance.RecalculateDivisionInitialSpeed();
        }

        private static bool TryGetThreatScreenPosition(Division targetDivision, Ship ship, bool isScout, out Vector3 position)
        {
            position = Vector3.zero;
            if (!GGAdvancedBattleAIOption.Enabled)
                return false;

            if (!isScout || targetDivision == null || ship == null || CurrentPlayer == null)
                return false;

            if (!PlayerMainDivisions.Contains(targetDivision.Pointer))
                return false;

            Division scoutDivision = ship.division;
            if (scoutDivision == null || !PlayerScoutDivisions.TryGetValue(scoutDivision.Pointer, out ScoutDivisionInfo scoutInfo))
                return false;

            if (ship.player != CurrentPlayer || scoutDivision.ScoutDivision != targetDivision)
                return false;

            RefreshThreatAssignments(targetDivision);

            Vector3 mainCenter = AveragePosition(targetDivision.ships);
            Vector3 enemyCenter = EnemyCenter(CurrentPlayer);
            Vector3 enemyDirection = FlatDirection(mainCenter, enemyCenter);
            if (enemyDirection.sqrMagnitude < 1f)
                enemyDirection = FlatForward(targetDivision);

            Vector3 basePosition;
            if (ScoutThreatAssignments.TryGetValue(scoutDivision.Pointer, out IntPtr threatPointer))
            {
                Division threat = FindDivision(threatPointer);
                if (threat != null && DivisionHasEnemyTorpedoes(threat, CurrentPlayer))
                {
                    Vector3 threatCenter = AveragePosition(threat.ships);
                    basePosition = Vector3.Lerp(mainCenter, threatCenter, ThreatScreenFraction);
                }
                else
                {
                    basePosition = mainCenter - enemyDirection * FallbackBehindDistance;
                }
            }
            else
            {
                basePosition = mainCenter - enemyDirection * FallbackBehindDistance;
            }

            Vector3 right = new(enemyDirection.z, 0f, -enemyDirection.x);
            float divisionOffset = scoutInfo.Index - ((scoutInfo.Count - 1) * 0.5f);
            int shipIndex = ShipIndexInDivision(scoutDivision, ship);
            float shipOffset = shipIndex - ((scoutDivision.ships.Count - 1) * 0.5f);

            // Patch intent: player scout divisions are defensive screens, not
            // generic perimeter scouts. Keep vanilla steering, but make the
            // desired point sit between our battle line and the nearest live
            // enemy torpedo threats. If no such threat exists, pull scouts back
            // behind the main line instead of feeding them forward.
            position = basePosition + right * ((divisionOffset * ScoutDivisionSpacing) + (shipOffset * ScoutShipSpacing));
            position.y = ship.transform.position.y;
            return true;
        }

        private static void RefreshThreatAssignments(Division mainDivision)
        {
            float now = Time.time;
            if (now - LastThreatAssignmentRefresh < ThreatAssignmentRefreshSeconds)
                return;

            LastThreatAssignmentRefresh = now;
            ScoutThreatAssignments.Clear();

            List<Division> threats = EnemyTorpedoDivisions(CurrentPlayer);
            if (threats.Count == 0 || PlayerScoutDivisions.Count == 0)
                return;

            Vector3 mainCenter = AveragePosition(mainDivision.ships);
            List<Division> orderedScouts = PlayerScoutDivisions.Values
                .Select(info => info.Division)
                .Where(division => division?.ships != null && division.ships.Count > 0)
                .OrderBy(division => DistanceSquared(AveragePosition(division.ships), mainCenter))
                .ToList();

            for (int i = 0; i < orderedScouts.Count; i++)
            {
                Division scout = orderedScouts[i];
                Division threat = threats[i % threats.Count];
                ScoutThreatAssignments[scout.Pointer] = threat.Pointer;
            }
        }

        private static Vector3 EnemyCenter(Player player)
        {
            List<Ship> enemies = new();
            foreach (Ship ship in Ship.AllShips)
            {
                if (ship == null || !ship.isAlive || ship.player == null || ship.player == player || IsTransport(ship))
                    continue;

                enemies.Add(ship);
            }

            return enemies.Count > 0 ? AveragePosition(enemies) : PlayerMainCenter() + Vector3.forward;
        }

        private static Division FindDivision(IntPtr pointer)
        {
            if (pointer == IntPtr.Zero || DivisionsManager.Instance?.CurrentDivisions == null)
                return null;

            foreach (Division division in DivisionsManager.Instance.CurrentDivisions)
            {
                if (division != null && division.Pointer == pointer)
                    return division;
            }

            return null;
        }

        private static int ShipIndexInDivision(Division division, Ship ship)
        {
            if (division?.ships == null || ship == null)
                return 0;

            for (int i = 0; i < division.ships.Count; i++)
            {
                Ship current = division.ships[i];
                if (current != null && current.Pointer == ship.Pointer)
                    return i;
            }

            return 0;
        }

        private static Vector3 FlatDirection(Vector3 from, Vector3 to)
        {
            Vector3 direction = to - from;
            direction.y = 0f;
            return direction.sqrMagnitude > 1f ? direction.normalized : Vector3.zero;
        }

        private static Vector3 FlatForward(Division division)
        {
            if (division?.ships != null && division.ships.Count > 0 && division.ships[0] != null)
            {
                Vector3 forward = division.ships[0].transform.forward;
                forward.y = 0f;
                return forward.sqrMagnitude > 1f ? forward.normalized : Vector3.forward;
            }

            return Vector3.forward;
        }

        private static float DistanceSquared(Vector3 a, Vector3 b)
        {
            Vector3 delta = a - b;
            delta.y = 0f;
            return delta.sqrMagnitude;
        }

        private static List<List<ShipEntry>> SplitEvenly(List<ShipEntry> entries, int groups)
        {
            List<List<ShipEntry>> result = new();
            int offset = 0;

            for (int i = 0; i < groups; i++)
            {
                int remainingEntries = entries.Count - offset;
                int remainingGroups = groups - i;
                int count = (int)Math.Ceiling(remainingEntries / (double)remainingGroups);
                result.Add(entries.Skip(offset).Take(count).ToList());
                offset += count;
            }

            return result;
        }

        private static IEnumerable<ShipEntry> SortEntries(IEnumerable<ShipEntry> entries)
        {
            return entries
                .OrderBy(entry => entry.Priority)
                .ThenByDescending(entry => entry.Tonnage)
                .ThenBy(entry => entry.Ship?.name ?? string.Empty);
        }

        private static void ErasePlayerDivisions(List<Division> divisions)
        {
            // Patch intent: DivisionsManager.Create can add the desired new
            // divisions without removing the vanilla all-ships division. Remove
            // the player's original battle-start divisions first, then recreate
            // combat groups from the captured ship lists to avoid UI duplicates.
            foreach (Division division in divisions)
            {
                if (division == null)
                    continue;

                DivisionsManager.Erase(division);
            }
        }

        private static Il2CppSystem.Collections.Generic.List<Ship> ToIl2CppList(IEnumerable<Ship> ships)
        {
            Il2CppSystem.Collections.Generic.List<Ship> list = new();
            foreach (Ship ship in ships)
            {
                if (ship != null)
                    list.Add(ship);
            }

            return list;
        }

        private static bool IsTransport(Ship ship)
            => ShipTypeName(ship) == "tr";

        private static string ShipTypeName(Ship ship)
            => ship?.shipType?.name?.ToLowerInvariant() ?? string.Empty;

        private static float SafeTonnage(Ship ship)
        {
            try { return ship?.Tonnage() ?? 0f; }
            catch { return 0f; }
        }

        private static string DescribeDivision(Division division)
        {
            if (division?.ships == null)
                return "none";

            List<string> ships = new();
            foreach (Ship ship in division.ships)
            {
                if (ship == null)
                    continue;

                ships.Add($"{ShipTypeName(ship).ToUpperInvariant()} {ship.name} {SafeTonnage(ship):0}t");
            }

            return $"{division.formation}/{division.spread} [{string.Join(", ", ships)}]";
        }

        [HarmonyPatch(typeof(BattleManager._PrepareBattle_d__67))]
        internal static class Patch_PrepareBattle
        {
            [HarmonyPatch(nameof(BattleManager._PrepareBattle_d__67.MoveNext))]
            [HarmonyPostfix]
            internal static void Postfix(ref bool __result)
            {
                if (__result)
                    return;

                ApplyPlayerLayout();
            }
        }

        [HarmonyPatch(typeof(Division), nameof(Division.GetDesiredScreenPosition))]
        internal static class Patch_Division_GetDesiredScreenPosition
        {
            [HarmonyPrefix]
            internal static bool Prefix(Division __instance, Ship ship, bool isScout, ref Vector3 __result)
            {
                try
                {
                    if (TryGetThreatScreenPosition(__instance, ship, isScout, out Vector3 position))
                    {
                        __result = position;
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Melon<TweaksAndFixes>.Logger.Warning($"GG battle layout screen fallback. {ex.GetType().Name}: {ex.Message}");
                }

                return true;
            }
        }
    }
}
