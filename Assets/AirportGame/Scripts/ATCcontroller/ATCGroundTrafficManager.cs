using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class ATCGroundTrafficManager : MonoBehaviour
{
    private struct ControlPointKey : IEquatable<ControlPointKey>
    {
        public bool IsManual;
        public Vector2Int Cell;
        public EntityId EntityId;

        public bool Equals(ControlPointKey other)
        {
            return IsManual == other.IsManual &&
                   Cell == other.Cell &&
                   EntityId == other.EntityId;
        }

        public override bool Equals(object obj)
        {
            return obj is ControlPointKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Cell.GetHashCode();
                hash = (hash * 397) ^ EntityId.GetHashCode();
                hash = (hash * 397) ^ IsManual.GetHashCode();
                return hash;
            }
        }
    }


    private struct TaxiwaySectionKey : IEquatable<TaxiwaySectionKey>
    {
        public Vector2Int Start;
        public Vector2Int End;

        public TaxiwaySectionKey(Vector2Int first, Vector2Int second)
        {
            bool firstComesFirst =
                first.x < second.x ||
                (first.x == second.x && first.y <= second.y);

            Start = firstComesFirst ? first : second;
            End = firstComesFirst ? second : first;
        }

        public bool Equals(TaxiwaySectionKey other)
        {
            return Start == other.Start && End == other.End;
        }

        public override bool Equals(object obj)
        {
            return obj is TaxiwaySectionKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Start.GetHashCode() * 397) ^ End.GetHashCode();
            }
        }
    }

    private struct TaxiwayEdgeKey : IEquatable<TaxiwayEdgeKey>
    {
        public Vector2Int First;
        public Vector2Int Second;

        public TaxiwayEdgeKey(Vector2Int first, Vector2Int second)
        {
            bool firstComesFirst =
                first.x < second.x ||
                (first.x == second.x && first.y <= second.y);

            First = firstComesFirst ? first : second;
            Second = firstComesFirst ? second : first;
        }

        public bool Equals(TaxiwayEdgeKey other)
        {
            return First == other.First && Second == other.Second;
        }

        public override bool Equals(object obj)
        {
            return obj is TaxiwayEdgeKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (First.GetHashCode() * 397) ^ Second.GetHashCode();
            }
        }
    }

    private sealed class TaxiwaySection
    {
        public TaxiwaySectionKey Key;
        public readonly List<Vector2Int> Cells = new List<Vector2Int>();
    }

    private sealed class TaxiwaySectionState
    {
        public TaxiwaySection Section;
        public readonly List<AircraftReservation> Owners =
            new List<AircraftReservation>();
        public readonly List<WaitRequest> Waiters =
            new List<WaitRequest>();
    }

    private static readonly Vector2Int[] TaxiwayDirections =
    {
        Vector2Int.up,
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.left
    };

    private sealed class ControlPoint
    {
        public ControlPointKey Key;
        public Vector3 Position;
        public string Label;
        public bool AllowSameDirectionTraffic;
        public float EntryRadius;
        public float ClearRadius;
    }

    private sealed class AircraftReservation
    {
        public AircraftTaxiTrafficFollowing Aircraft;
        public Vector3 TravelDirection;
        public bool HasEntered;
        public float LastSeenTime;
    }

    private sealed class WaitRequest
    {
        public AircraftTaxiTrafficFollowing Aircraft;
        public Vector3 TravelDirection;
        public float RequestedAt;
        public float LastSeenTime;
    }

    private sealed class ControlPointState
    {
        public ControlPoint Point;
        public readonly List<AircraftReservation> Owners =
            new List<AircraftReservation>();
        public readonly List<WaitRequest> Waiters =
            new List<WaitRequest>();
    }

    public static ATCGroundTrafficManager Instance { get; private set; }

    [Header("References")]
    [SerializeField] private GridManager gridManager;
    [SerializeField] private TaxiwayNetwork taxiwayNetwork;

    [Header("Automatic Junction Detection")]
    [Min(1f)]
    [SerializeField] private float approachDistanceCells = 6f;
    [Min(0.5f)]
    [SerializeField] private float centerlineSearchRadiusCells = 3f;
    [Min(0.25f)]
    [SerializeField] private float intersectionEntryRadiusCells = 1.5f;
    [Min(0.5f)]
    [SerializeField] private float intersectionClearRadiusCells = 2.5f;

    [Header("ATC Diagnostics")]
    [SerializeField] private bool logTaxiClearanceChanges;

    [Header("Reservation Cleanup")]
    [Min(0.25f)]
    [SerializeField] private float staleReservationTimeout = 3f;
    [Min(0.1f)]
    [SerializeField] private float staleWaitRequestTimeout = 1.5f;
    [Min(0.1f)]
    [SerializeField] private float cleanupInterval = 0.5f;

    private readonly List<ControlPoint> controlPoints =
        new List<ControlPoint>();

    private readonly Dictionary<ControlPointKey, ControlPointState> pointStates =
        new Dictionary<ControlPointKey, ControlPointState>();

    private readonly Dictionary<AircraftTaxiTrafficFollowing, ControlPointKey>
        activeReservations =
            new Dictionary<AircraftTaxiTrafficFollowing, ControlPointKey>();

    private readonly Dictionary<AircraftTaxiTrafficFollowing, ControlPointKey>
        waitingRequests =
            new Dictionary<AircraftTaxiTrafficFollowing, ControlPointKey>();


    private readonly Dictionary<TaxiwaySectionKey, TaxiwaySectionState>
        taxiwaySectionStates =
            new Dictionary<TaxiwaySectionKey, TaxiwaySectionState>();

    private readonly Dictionary<Vector2Int, List<TaxiwaySectionKey>>
        taxiwaySectionsByCell =
            new Dictionary<Vector2Int, List<TaxiwaySectionKey>>();

    private readonly Dictionary<AircraftTaxiTrafficFollowing, List<TaxiwaySectionKey>>
        activeSectionReservations =
            new Dictionary<AircraftTaxiTrafficFollowing, List<TaxiwaySectionKey>>();

    private readonly Dictionary<AircraftTaxiTrafficFollowing, TaxiwaySectionKey>
        waitingSectionRequests =
            new Dictionary<AircraftTaxiTrafficFollowing, TaxiwaySectionKey>();

    private int cachedNetworkVersion = int.MinValue;
    private int cachedManualPointVersion = int.MinValue;
    private float nextCleanupTime;

    private readonly Dictionary<AircraftTaxiTrafficFollowing, string> lastLoggedTaxiDecisions =
        new Dictionary<AircraftTaxiTrafficFollowing, string>();

    private readonly Dictionary<AircraftTaxiTrafficFollowing, int>
        routeClearanceTargets =
            new Dictionary<AircraftTaxiTrafficFollowing, int>();

    [Header("Registered Route Diagnostics")]
    [SerializeField] private bool logRegisteredRouteConflicts;

    private readonly Dictionary<string, string> lastRegisteredAircraftStates =
        new Dictionary<string, string>();

    private readonly Dictionary<string, string> lastLoggedRoutePairStates =
        new Dictionary<string, string>();

    public int DetectedIntersectionCount { get; private set; }
    public int ActiveReservationCount => activeReservations.Count;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        Instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null)
            return;

        ATCGroundTrafficManager existing =
            FindFirstObjectByType<ATCGroundTrafficManager>();

        if (existing != null)
        {
            Instance = existing;
            return;
        }

        GameObject managerObject =
            new GameObject("ATCGroundTrafficManager");

        managerObject.AddComponent<ATCGroundTrafficManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        ResolveReferences();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextCleanupTime)
            return;

        nextCleanupTime =
            Time.unscaledTime + Mathf.Max(0.1f, cleanupInterval);

        CleanupStaleReservations();
        CleanupStaleSectionReservations();

        if (logRegisteredRouteConflicts)
            RunRegisteredRouteDiagnostics();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        activeReservations.Clear();
        waitingRequests.Clear();
        pointStates.Clear();
        activeSectionReservations.Clear();
        waitingSectionRequests.Clear();
        taxiwaySectionStates.Clear();
        taxiwaySectionsByCell.Clear();
    }

    private void ResolveReferences()
    {
        if (gridManager == null)
            gridManager = FindFirstObjectByType<GridManager>();

        if (taxiwayNetwork == null)
            taxiwayNetwork = FindFirstObjectByType<TaxiwayNetwork>();
    }

    /// <summary>
    /// Returns false when this aircraft must hold before the next
    /// automatically detected intersection or manual holding point.
    /// </summary>
    public bool HasTaxiClearance(
        AircraftTaxiTrafficFollowing aircraft,
        out string decision)
    {
        decision = "ATC unavailable";

        if (aircraft == null)
            return true;

        AircraftTaxiTrafficFollowing.RegisterActiveAircraft(aircraft);
        ResolveReferences();

        if (gridManager == null || taxiwayNetwork == null)
        {
            decision = "ATC references not ready";
            LogTaxiClearanceDecision(aircraft, true, decision);
            return true;
        }

        EnsureControlPoints();

        float holdBrakingBuffer =
            Mathf.Max(1f, approachDistanceCells) * gridManager.CellSize;

        IReadOnlyList<Vector2Int> route = aircraft.PlannedTaxiRoute;
        if (route != null && route.Count >= 2 &&
            TryFindNearestRouteIndex(
                route,
                aircraft.transform.position,
                out int currentIndex,
                out _))
        {
            // Recheck before reaching the clearance boundary. Taxi speed is
            // reduced over time, so waiting until the boundary is reached
            // can make the aircraft physically unable to stop there.
            if (routeClearanceTargets.TryGetValue(
                    aircraft,
                    out int targetIndex))
            {
                Vector3 targetPosition =
                    gridManager.GridToWorldCenter(
                        route[Mathf.Clamp(targetIndex, 0, route.Count - 1)]);

                float distanceToBoundary =
                    HorizontalDistance(
                        aircraft.transform.position,
                        targetPosition);

                bool insideBrakingBuffer =
                    distanceToBoundary <= holdBrakingBuffer;

                bool passedBoundary = currentIndex > targetIndex;

                if (!insideBrakingBuffer && !passedBoundary)
                {
                    decision = "Cleared to " + route[targetIndex];
                    LogTaxiClearanceDecision(aircraft, true, decision);
                    return true;
                }

                routeClearanceTargets.Remove(aircraft);
            }
        }
        else
        {
            routeClearanceTargets.Remove(aircraft);
        }

        // This scans the complete remaining route, not just the next section.
        if (!HasRouteConflictClearance(
                aircraft,
                out decision,
                out int conflictHoldingIndex))
        {
            LogTaxiClearanceDecision(aircraft, false, decision);
            return false;
        }

        if (route != null && route.Count >= 2 &&
            TryFindNearestRouteIndex(
                route,
                aircraft.transform.position,
                out currentIndex,
                out _))
        {
            if (conflictHoldingIndex >= 0)
            {
                Vector3 conflictHoldPosition =
                    gridManager.GridToWorldCenter(
                        route[Mathf.Clamp(
                            conflictHoldingIndex,
                            0,
                            route.Count - 1)]);

                float distanceToConflictHold =
                    HorizontalDistance(
                        aircraft.transform.position,
                        conflictHoldPosition);

                // Start braking before the actual holding point. Keep the
                // aircraft stopped here while the route conflict remains.
                if (distanceToConflictHold <= holdBrakingBuffer)
                {
                    decision =
                        "HOLD before conflict holding point " +
                        route[conflictHoldingIndex] + ": " + decision;

                    LogTaxiClearanceDecision(aircraft, false, decision);
                    return false;
                }
            }

            int nextBoundary = conflictHoldingIndex >= 0
                ? conflictHoldingIndex
                : FindNextRouteBoundary(route, currentIndex);

            nextBoundary = Mathf.Clamp(
                nextBoundary,
                currentIndex,
                route.Count - 1);

            routeClearanceTargets[aircraft] = nextBoundary;

            decision = conflictHoldingIndex >= 0
                ? "Cleared only toward conflict holding point " + route[nextBoundary]
                : "Cleared to next holding point " + route[nextBoundary];
        }
        else
        {
            decision = "Route clear; no route boundary available";
        }

        LogTaxiClearanceDecision(aircraft, true, decision);
        return true;
    }

    private void RunRegisteredRouteDiagnostics()
    {
        IReadOnlyList<AircraftTaxiTrafficFollowing> registered =
            AircraftTaxiTrafficFollowing.ActiveAircraft;

        Debug.Log(
            "[ATC REGISTRY] Active registered aircraft: " + registered.Count);

        HashSet<string> currentAircraftIds = new HashSet<string>();
        HashSet<string> currentPairIds = new HashSet<string>();

        for (int i = 0; i < registered.Count; i++)
        {
            AircraftTaxiTrafficFollowing aircraft = registered[i];
            if (aircraft == null || !aircraft.isActiveAndEnabled)
                continue;

            string id = aircraft.GetEntityId().ToString();
            currentAircraftIds.Add(id);

            AircraftStatus status = aircraft.GetComponent<AircraftStatus>();
            string state = status == null
                ? "AircraftStatus missing"
                : status.IsParked
                    ? "PARKED"
                    : status.IsTaxiing
                        ? "TAXIING"
                        : "NOT TAXIING";

            string routeState = aircraft.PlannedTaxiRoute == null ||
                                aircraft.PlannedTaxiRoute.Count == 0
                ? "NO ROUTE"
                : "ROUTE CELLS " + aircraft.PlannedTaxiRoute.Count;

            string signature = state + " | " + routeState;
            if (!lastRegisteredAircraftStates.TryGetValue(id, out string previous) ||
                previous != signature)
            {
                lastRegisteredAircraftStates[id] = signature;

                Debug.Log(
                    "[ATC REGISTERED AIRCRAFT] " + aircraft.name +
                    " | ID: " + id +
                    " | " + signature +
                    " | Position: " + aircraft.transform.position +
                    " | Forward: " + aircraft.transform.forward,
                    aircraft);
            }
        }

        List<string> removedAircraftIds = null;
        foreach (string previousId in lastRegisteredAircraftStates.Keys)
        {
            if (currentAircraftIds.Contains(previousId))
                continue;

            if (removedAircraftIds == null)
                removedAircraftIds = new List<string>();

            removedAircraftIds.Add(previousId);
        }

        if (removedAircraftIds != null)
        {
            foreach (string id in removedAircraftIds)
            {
                Debug.Log("[ATC REGISTERED AIRCRAFT] UNREGISTERED | ID: " + id);
                lastRegisteredAircraftStates.Remove(id);
            }
        }

        if (gridManager == null)
            return;

        for (int i = 0; i < registered.Count; i++)
        {
            AircraftTaxiTrafficFollowing first = registered[i];
            if (!IsDiagnosticTaxiingAircraft(first))
                continue;

            for (int j = i + 1; j < registered.Count; j++)
            {
                AircraftTaxiTrafficFollowing second = registered[j];
                if (!IsDiagnosticTaxiingAircraft(second))
                    continue;

                LogRegisteredRoutePair(first, second, currentPairIds);
            }
        }

        List<string> clearedPairIds = null;
        foreach (string previousPairId in lastLoggedRoutePairStates.Keys)
        {
            if (currentPairIds.Contains(previousPairId))
                continue;

            if (clearedPairIds == null)
                clearedPairIds = new List<string>();

            clearedPairIds.Add(previousPairId);
        }

        if (clearedPairIds != null)
        {
            foreach (string pairId in clearedPairIds)
            {
                Debug.Log("[ATC ROUTE PAIR] NO LONGER REGISTERED | Pair: " + pairId);
                lastLoggedRoutePairStates.Remove(pairId);
            }
        }
    }

    private bool IsDiagnosticTaxiingAircraft(
        AircraftTaxiTrafficFollowing aircraft)
    {
        if (aircraft == null ||
            !aircraft.isActiveAndEnabled ||
            aircraft.PlannedTaxiRoute == null ||
            aircraft.PlannedTaxiRoute.Count < 2)
        {
            return false;
        }

        AircraftStatus status = aircraft.GetComponent<AircraftStatus>();
        return status != null && !status.IsParked && status.IsTaxiing;
    }

    private void LogRegisteredRoutePair(
        AircraftTaxiTrafficFollowing first,
        AircraftTaxiTrafficFollowing second,
        HashSet<string> currentPairIds)
    {
        string firstId = first.GetEntityId().ToString();
        string secondId = second.GetEntityId().ToString();

        bool firstComesFirst = string.CompareOrdinal(firstId, secondId) <= 0;
        string pairId = firstComesFirst
            ? firstId + "|" + secondId
            : secondId + "|" + firstId;

        currentPairIds.Add(pairId);

        AircraftTaxiTrafficFollowing a = firstComesFirst ? first : second;
        AircraftTaxiTrafficFollowing b = firstComesFirst ? second : first;

        IReadOnlyList<Vector2Int> routeA = a.PlannedTaxiRoute;
        IReadOnlyList<Vector2Int> routeB = b.PlannedTaxiRoute;

        TryFindNearestRouteIndex(
            routeA, a.transform.position, out int startA, out float distanceA);
        TryFindNearestRouteIndex(
            routeB, b.transform.position, out int startB, out float distanceB);

        HashSet<string> sharedCellReports = new HashSet<string>();
        int sharedCellCount = 0;
        int conflictingCellCount = 0;
        int sameDirectionCellCount = 0;

        for (int ai = 0; ai < routeA.Count; ai++)
        {
            for (int bi = 0; bi < routeB.Count; bi++)
            {
                if (routeA[ai] != routeB[bi])
                    continue;

                Vector3 directionA = GetRouteDirection(routeA, ai);
                Vector3 directionB = GetRouteDirection(routeB, bi);
                float directionDot = Vector3.Dot(directionA, directionB);

                string classification;
                if (directionDot > 0.75f)
                {
                    classification = "SAME DIRECTION";
                    sameDirectionCellCount++;
                }
                else
                {
                    classification = directionDot < -0.75f
                        ? "OPPOSING"
                        : "CROSSING/MERGING";
                    conflictingCellCount++;
                }

                sharedCellCount++;

                bool aheadA = startA >= 0 && ai >= startA;
                bool aheadB = startB >= 0 && bi >= startB;

                sharedCellReports.Add(
                    routeA[ai] +
                    " [" + classification +
                    ", dot=" + directionDot.ToString("F2") +
                    ", A index=" + ai + (aheadA ? " AHEAD" : " PASSED") +
                    ", B index=" + bi + (aheadB ? " AHEAD" : " PASSED") +
                    "]");
            }
        }

        string pairState = conflictingCellCount > 0
            ? "CONFLICT"
            : sharedCellCount > 0
                ? "SHARED ROUTE - SAME DIRECTION"
                : "NO SHARED ROUTE CELLS";

        List<string> orderedReports = new List<string>(sharedCellReports);
        orderedReports.Sort(StringComparer.Ordinal);

        string signature = pairState + " | " +
                           string.Join("; ", orderedReports);

        if (lastLoggedRoutePairStates.TryGetValue(pairId, out string previous) &&
            previous == signature)
        {
            return;
        }

        lastLoggedRoutePairStates[pairId] = signature;

        AircraftStatus statusA = a.GetComponent<AircraftStatus>();
        AircraftStatus statusB = b.GetComponent<AircraftStatus>();

        string report =
            "[ATC ROUTE PAIR] " + pairState +
            " | " + a.name + " (ID " + firstId + ")" +
            " <-> " + b.name + " (ID " + secondId + ")" +
            "\n  A position: " + a.transform.position +
            " | forward: " + a.transform.forward +
            " | speed: " + (statusA != null ? statusA.CurrentSpeedKnots.ToString("F2") : "n/a") +
            " | nearest route index: " + startA +
            " | distance to route: " + distanceA.ToString("F2") +
            "\n  B position: " + b.transform.position +
            " | forward: " + b.transform.forward +
            " | speed: " + (statusB != null ? statusB.CurrentSpeedKnots.ToString("F2") : "n/a") +
            " | nearest route index: " + startB +
            " | distance to route: " + distanceB.ToString("F2") +
            "\n  Shared cells: " +
            (orderedReports.Count > 0
                ? string.Join("; ", orderedReports)
                : "none") +
            "\n  Totals: shared=" + sharedCellCount +
            ", conflicting=" + conflictingCellCount +
            ", same-direction=" + sameDirectionCellCount;

        Debug.Log(report, a);
    }

    private void LogTaxiClearanceDecision(
        AircraftTaxiTrafficFollowing aircraft,
        bool cleared,
        string decision)
    {
        if (!logTaxiClearanceChanges || aircraft == null)
            return;

        string result = cleared ? "CLEAR" : "HOLD";
        string signature = result + ": " + decision;

        if (lastLoggedTaxiDecisions.TryGetValue(aircraft, out string previous) &&
            previous == signature)
            return;

        lastLoggedTaxiDecisions[aircraft] = signature;

        string message = "[ATC ROUTE DEBUG] " + aircraft.name +
            " | " + result +
            " | " + decision +
            " | Position: " + aircraft.transform.position +
            " | Route: " + FormatTaxiRoute(aircraft.PlannedTaxiRoute);

        IReadOnlyList<AircraftTaxiTrafficFollowing> allAircraft =
            AircraftTaxiTrafficFollowing.ActiveAircraft;

        for (int i = 0; i < allAircraft.Count; i++)
        {
            AircraftTaxiTrafficFollowing other = allAircraft[i];
            if (other == null || other == aircraft)
                continue;

            AircraftStatus status = other.GetComponent<AircraftStatus>();
            if (status == null || status.IsParked || !status.IsTaxiing)
                continue;

            message += "\n  Traffic: " + other.name +
                " | Position: " + other.transform.position +
                " | Route: " + FormatTaxiRoute(other.PlannedTaxiRoute);
        }

        Debug.Log(message, aircraft);
    }

    private static string FormatTaxiRoute(IReadOnlyList<Vector2Int> route)
    {
        if (route == null || route.Count == 0)
            return "<no planned route>";

        return string.Join(" -> ", route);
    }

    public void ReleaseAircraft(
        AircraftTaxiTrafficFollowing aircraft)
    {
        if (aircraft == null)
            return;

        ReleaseActiveReservation(aircraft);
        ReleaseWaitingRequest(aircraft);
        ReleaseAllSectionReservations(aircraft);
        lastLoggedTaxiDecisions.Remove(aircraft);
        routeClearanceTargets.Remove(aircraft);
        aircraft.ClearPlannedTaxiRoute();
    }

    private void EnsureControlPoints()
    {
        ResolveReferences();

        if (taxiwayNetwork == null || gridManager == null)
            return;

        int networkVersion = taxiwayNetwork.TopologyVersion;
        int manualVersion = ATCManualHoldingPoint.RegistryVersion;

        if (networkVersion == cachedNetworkVersion &&
            manualVersion == cachedManualPointVersion)
        {
            return;
        }

        RebuildControlPoints(networkVersion, manualVersion);
    }

    private void RebuildControlPoints(
        int networkVersion,
        int manualVersion)
    {
        controlPoints.Clear();

        HashSet<ControlPointKey> validKeys =
            new HashSet<ControlPointKey>();

        HashSet<Vector2Int> centerlineCells =
            new HashSet<Vector2Int>(
                taxiwayNetwork.TaxiwayCenterlineCells);

        int intersectionCount = 0;

        foreach (Vector2Int cell in centerlineCells)
        {
            int connections = 0;

            if (centerlineCells.Contains(cell + Vector2Int.up))
                connections++;

            if (centerlineCells.Contains(cell + Vector2Int.right))
                connections++;

            if (centerlineCells.Contains(cell + Vector2Int.down))
                connections++;

            if (centerlineCells.Contains(cell + Vector2Int.left))
                connections++;

            // A centerline cell with three or four connected neighbours
            // is an automatically detected T-junction or crossing.
            if (connections < 3)
                continue;

            ControlPointKey key = new ControlPointKey
            {
                IsManual = false,
                Cell = cell,
                EntityId = default
            };

            ControlPoint point = new ControlPoint
            {
                Key = key,
                Position = gridManager.GridToWorldCenter(cell),
                Label = "Taxiway intersection " + cell,
                AllowSameDirectionTraffic = false,
                EntryRadius =
                    intersectionEntryRadiusCells * gridManager.CellSize,
                ClearRadius =
                    intersectionClearRadiusCells * gridManager.CellSize
            };

            AddOrUpdateControlPoint(point, validKeys);
            intersectionCount++;
        }

        foreach (ATCManualHoldingPoint holdingPoint
                 in ATCManualHoldingPoint.RegisteredPoints)
        {
            if (holdingPoint == null)
                continue;

            if (!IsNearTaxiwayCenterline(holdingPoint.transform.position))
                continue;

            Vector2Int cell =
                gridManager.WorldToGrid(holdingPoint.transform.position);

            ControlPointKey key = new ControlPointKey
            {
                IsManual = true,
                Cell = cell,
                EntityId = holdingPoint.GetEntityId()
            };

            float entryRadius =
                holdingPoint.ControlRadiusCells * gridManager.CellSize;

            ControlPoint point = new ControlPoint
            {
                Key = key,
                Position = holdingPoint.transform.position,
                Label = holdingPoint.name,
                AllowSameDirectionTraffic =
                    holdingPoint.AllowSameDirectionTraffic,
                EntryRadius = entryRadius,
                ClearRadius = entryRadius +
                              gridManager.CellSize
            };

            AddOrUpdateControlPoint(point, validKeys);
        }

        List<ControlPointKey> removedKeys = null;

        foreach (KeyValuePair<ControlPointKey, ControlPointState> pair
                 in pointStates)
        {
            if (validKeys.Contains(pair.Key))
                continue;

            if (removedKeys == null)
                removedKeys = new List<ControlPointKey>();

            removedKeys.Add(pair.Key);
        }

        if (removedKeys != null)
        {
            foreach (ControlPointKey key in removedKeys)
            {
                ControlPointState state = pointStates[key];

                for (int i = state.Owners.Count - 1; i >= 0; i--)
                {
                    AircraftTaxiTrafficFollowing owner =
                        state.Owners[i].Aircraft;

                    if (owner != null)
                        activeReservations.Remove(owner);
                }

                for (int i = state.Waiters.Count - 1; i >= 0; i--)
                {
                    AircraftTaxiTrafficFollowing waiter =
                        state.Waiters[i].Aircraft;

                    if (waiter != null)
                        waitingRequests.Remove(waiter);
                }

                pointStates.Remove(key);
            }
        }

        RebuildTaxiwaySections(centerlineCells);

        DetectedIntersectionCount = intersectionCount;
        cachedNetworkVersion = networkVersion;
        cachedManualPointVersion = manualVersion;
    }

    private void AddOrUpdateControlPoint(
        ControlPoint point,
        HashSet<ControlPointKey> validKeys)
    {
        validKeys.Add(point.Key);
        controlPoints.Add(point);

        if (pointStates.TryGetValue(
                point.Key,
                out ControlPointState existing))
        {
            existing.Point = point;
            return;
        }

        pointStates.Add(
            point.Key,
            new ControlPointState
            {
                Point = point
            });
    }


    private void RebuildTaxiwaySections(HashSet<Vector2Int> centerlineCells)
    {
        Dictionary<Vector2Int, int> connectionCounts =
            new Dictionary<Vector2Int, int>();

        foreach (Vector2Int cell in centerlineCells)
        {
            int count = 0;

            foreach (Vector2Int direction in TaxiwayDirections)
            {
                if (centerlineCells.Contains(cell + direction))
                    count++;
            }

            connectionCounts[cell] = count;
        }

        HashSet<TaxiwayEdgeKey> visitedEdges =
            new HashSet<TaxiwayEdgeKey>();

        HashSet<TaxiwaySectionKey> validKeys =
            new HashSet<TaxiwaySectionKey>();

        taxiwaySectionsByCell.Clear();

        foreach (Vector2Int startCell in centerlineCells)
        {
            // Junctions and dead ends divide the network into sections.
            // Cells with exactly two connections are traversed as part
            // of the section between those nodes.
            if (connectionCounts[startCell] == 2)
                continue;

            foreach (Vector2Int direction in TaxiwayDirections)
            {
                Vector2Int nextCell = startCell + direction;

                if (!centerlineCells.Contains(nextCell))
                    continue;

                TaxiwayEdgeKey firstEdge =
                    new TaxiwayEdgeKey(startCell, nextCell);

                if (!visitedEdges.Add(firstEdge))
                    continue;

                TaxiwaySection section = new TaxiwaySection();
                section.Cells.Add(startCell);

                Vector2Int previousCell = startCell;
                Vector2Int currentCell = nextCell;
                section.Cells.Add(currentCell);

                int safety = centerlineCells.Count + 1;

                while (safety-- > 0 &&
                       connectionCounts.TryGetValue(
                           currentCell,
                           out int connections) &&
                       connections == 2)
                {
                    Vector2Int followingCell = currentCell;
                    bool foundNext = false;

                    foreach (Vector2Int nextDirection in TaxiwayDirections)
                    {
                        Vector2Int candidate =
                            currentCell + nextDirection;

                        if (candidate == previousCell ||
                            !centerlineCells.Contains(candidate))
                        {
                            continue;
                        }

                        followingCell = candidate;
                        foundNext = true;
                        break;
                    }

                    if (!foundNext)
                        break;

                    visitedEdges.Add(
                        new TaxiwayEdgeKey(currentCell, followingCell));

                    previousCell = currentCell;
                    currentCell = followingCell;
                    section.Cells.Add(currentCell);
                }

                if (section.Cells.Count < 2)
                    continue;

                section.Key = new TaxiwaySectionKey(
                    section.Cells[0],
                    section.Cells[section.Cells.Count - 1]);

                // Keep section ordering stable so the direction token
                // remains consistent across rebuilds.
                if (section.Cells[0] != section.Key.Start)
                    section.Cells.Reverse();

                if (!validKeys.Add(section.Key))
                    continue;

                if (!taxiwaySectionStates.TryGetValue(
                        section.Key,
                        out TaxiwaySectionState state))
                {
                    state = new TaxiwaySectionState();
                    taxiwaySectionStates.Add(section.Key, state);
                }

                state.Section = section;

                foreach (Vector2Int cell in section.Cells)
                {
                    if (!taxiwaySectionsByCell.TryGetValue(
                            cell,
                            out List<TaxiwaySectionKey> keys))
                    {
                        keys = new List<TaxiwaySectionKey>();
                        taxiwaySectionsByCell.Add(cell, keys);
                    }

                    if (!keys.Contains(section.Key))
                        keys.Add(section.Key);
                }
            }
        }

        List<TaxiwaySectionKey> removedKeys = null;

        foreach (TaxiwaySectionKey key in taxiwaySectionStates.Keys)
        {
            if (validKeys.Contains(key))
                continue;

            if (removedKeys == null)
                removedKeys = new List<TaxiwaySectionKey>();

            removedKeys.Add(key);
        }

        if (removedKeys == null)
            return;

        foreach (TaxiwaySectionKey key in removedKeys)
        {
            TaxiwaySectionState state = taxiwaySectionStates[key];

            foreach (AircraftReservation owner in state.Owners)
            {
                if (owner.Aircraft != null)
                    RemoveActiveSectionKey(owner.Aircraft, key);
            }

            foreach (WaitRequest waiter in state.Waiters)
            {
                if (waiter.Aircraft != null)
                    waitingSectionRequests.Remove(waiter.Aircraft);
            }

            taxiwaySectionStates.Remove(key);
        }
    }

    private bool TryFindNextTaxiwaySection(
        Transform aircraft,
        out TaxiwaySectionState selectedState,
        out Vector3 travelDirection)
    {
        selectedState = null;
        travelDirection = Vector3.zero;

        Vector3 forward = aircraft.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude < 0.0001f)
            return false;

        forward.Normalize();

        float cellSize = gridManager.CellSize;
        float approachDistance = approachDistanceCells * cellSize;
        float lateralTolerance = centerlineSearchRadiusCells * cellSize;
        float bestScore = float.MaxValue;

        foreach (TaxiwaySectionState state in taxiwaySectionStates.Values)
        {
            TaxiwaySection section = state.Section;

            if (section == null || section.Cells.Count < 2)
                continue;

            for (int i = 0; i < section.Cells.Count; i++)
            {
                Vector3 cellPosition =
                    gridManager.GridToWorldCenter(section.Cells[i]);

                Vector3 toCell = cellPosition - aircraft.position;
                toCell.y = 0f;

                float distance = toCell.magnitude;

                if (distance > approachDistance)
                    continue;

                float forwardDistance = Vector3.Dot(toCell, forward);

                if (forwardDistance < -cellSize * 0.25f)
                    continue;

                float lateralDistance =
                    Mathf.Sqrt(Mathf.Max(
                        0f,
                        toCell.sqrMagnitude -
                        forwardDistance * forwardDistance));

                if (lateralDistance > lateralTolerance)
                    continue;

                int previousIndex = Mathf.Max(0, i - 1);
                int nextIndex = Mathf.Min(section.Cells.Count - 1, i + 1);

                Vector3 tangent =
                    gridManager.GridToWorldCenter(
                        section.Cells[nextIndex]) -
                    gridManager.GridToWorldCenter(
                        section.Cells[previousIndex]);

                tangent.y = 0f;

                if (tangent.sqrMagnitude < 0.0001f)
                    continue;

                tangent.Normalize();

                float alignment = Vector3.Dot(tangent, forward);
                float absoluteAlignment = Mathf.Abs(alignment);

                if (absoluteAlignment < 0.5f)
                    continue;

                float score =
                    distance +
                    (1f - absoluteAlignment) * cellSize * 2f;

                if (score >= bestScore)
                    continue;

                bestScore = score;
                selectedState = state;
                // Section direction is represented along its stable
                // cell ordering, not by the local tangent. This allows
                // same-direction aircraft to share curved sections.
                travelDirection =
                    alignment >= 0f ? Vector3.forward : Vector3.back;
            }
        }

        return selectedState != null;
    }

    private bool RequestTaxiwaySection(
        AircraftTaxiTrafficFollowing aircraft,
        TaxiwaySectionState state,
        Vector3 travelDirection,
        out string decision)
    {
        decision = string.Empty;

        travelDirection.y = 0f;

        if (travelDirection.sqrMagnitude < 0.0001f)
        {
            decision = "Taxiway section direction unavailable";
            return false;
        }

        travelDirection.Normalize();
        TaxiwaySectionKey key = state.Section.Key;

        if (waitingSectionRequests.TryGetValue(
                aircraft,
                out TaxiwaySectionKey previousKey) &&
            !previousKey.Equals(key))
        {
            ReleaseWaitingSectionRequest(aircraft);
        }

        AircraftReservation existingOwner =
            FindSectionOwner(state, aircraft);

        if (existingOwner != null)
        {
            existingOwner.LastSeenTime = Time.unscaledTime;
            decision = "Cleared on taxiway section";
            return true;
        }

        WaitRequest request = FindSectionWaiter(state, aircraft);

        if (request == null)
        {
            request = new WaitRequest
            {
                Aircraft = aircraft,
                TravelDirection = travelDirection,
                RequestedAt = Time.unscaledTime,
                LastSeenTime = Time.unscaledTime
            };

            state.Waiters.Add(request);
            waitingSectionRequests[aircraft] = key;
        }
        else
        {
            request.TravelDirection = travelDirection;
            request.LastSeenTime = Time.unscaledTime;
        }

        if (HasSameDirectionSectionOwner(state, travelDirection) &&
            !HasEarlierOpposingSectionWaiter(state, request))
        {
            GrantSectionReservation(state, request);
            decision = "Same-direction traffic cleared on taxiway section";
            return true;
        }

        if (state.Owners.Count == 0 &&
            IsOldestActiveSectionWaiter(state, request))
        {
            GrantSectionReservation(state, request);
            decision = "Cleared onto taxiway section";
            return true;
        }

        AircraftReservation blockingOwner =
            state.Owners.Count > 0 ? state.Owners[0] : null;

        string ownerName =
            blockingOwner != null && blockingOwner.Aircraft != null
                ? blockingOwner.Aircraft.name
                : "opposing traffic";

        decision = "Holding for opposing traffic on taxiway section (" +
                   ownerName + ")";
        return false;
    }

    private void UpdateExistingSectionReservations(
        AircraftTaxiTrafficFollowing aircraft)
    {
        if (!activeSectionReservations.TryGetValue(
                aircraft,
                out List<TaxiwaySectionKey> keys))
        {
            return;
        }

        List<TaxiwaySectionKey> completed = null;
        float clearDistance =
            Mathf.Max(intersectionClearRadiusCells, 1f) *
            gridManager.CellSize;
        float entryDistance =
            Mathf.Max(intersectionEntryRadiusCells, 0.5f) *
            gridManager.CellSize;

        foreach (TaxiwaySectionKey key in keys)
        {
            if (!taxiwaySectionStates.TryGetValue(
                    key,
                    out TaxiwaySectionState state))
            {
                if (completed == null)
                    completed = new List<TaxiwaySectionKey>();

                completed.Add(key);
                continue;
            }

            AircraftReservation reservation =
                FindSectionOwner(state, aircraft);

            if (reservation == null)
            {
                if (completed == null)
                    completed = new List<TaxiwaySectionKey>();

                completed.Add(key);
                continue;
            }

            reservation.LastSeenTime = Time.unscaledTime;

            float nearestDistance = float.MaxValue;

            foreach (Vector2Int cell in state.Section.Cells)
            {
                Vector3 cellPosition =
                    gridManager.GridToWorldCenter(cell);

                nearestDistance = Mathf.Min(
                    nearestDistance,
                    HorizontalDistance(
                        aircraft.transform.position,
                        cellPosition));
            }

            if (nearestDistance <= entryDistance)
                reservation.HasEntered = true;

            bool clearedSection =
                reservation.HasEntered &&
                nearestDistance > clearDistance;

            bool movedAwayBeforeEntry =
                !reservation.HasEntered &&
                nearestDistance >
                    approachDistanceCells * gridManager.CellSize +
                    gridManager.CellSize;

            if (clearedSection || movedAwayBeforeEntry)
            {
                if (completed == null)
                    completed = new List<TaxiwaySectionKey>();

                completed.Add(key);
            }
        }

        if (completed == null)
            return;

        foreach (TaxiwaySectionKey key in completed)
            ReleaseActiveSectionReservation(aircraft, key);
    }

    private bool HasSameDirectionSectionOwner(
        TaxiwaySectionState state,
        Vector3 direction)
    {
        if (state.Owners.Count == 0)
            return false;

        foreach (AircraftReservation owner in state.Owners)
        {
            if (owner.Aircraft == null)
                continue;

            if (Vector3.Dot(owner.TravelDirection, direction) < 0.85f)
                return false;
        }

        return true;
    }

    private bool HasEarlierOpposingSectionWaiter(
        TaxiwaySectionState state,
        WaitRequest requestingWaiter)
    {
        foreach (WaitRequest waiter in state.Waiters)
        {
            if (waiter == requestingWaiter ||
                waiter.Aircraft == null ||
                waiter.RequestedAt >= requestingWaiter.RequestedAt)
            {
                continue;
            }

            if (Vector3.Dot(
                    waiter.TravelDirection,
                    requestingWaiter.TravelDirection) < 0.85f)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsOldestActiveSectionWaiter(
        TaxiwaySectionState state,
        WaitRequest requestingWaiter)
    {
        WaitRequest oldest = null;

        foreach (WaitRequest waiter in state.Waiters)
        {
            if (waiter.Aircraft == null ||
                !waiter.Aircraft.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (oldest == null ||
                waiter.RequestedAt < oldest.RequestedAt)
            {
                oldest = waiter;
            }
        }

        return oldest == requestingWaiter;
    }

    private void GrantSectionReservation(
        TaxiwaySectionState state,
        WaitRequest request)
    {
        AircraftTaxiTrafficFollowing aircraft = request.Aircraft;

        if (aircraft == null)
            return;

        state.Waiters.Remove(request);
        waitingSectionRequests.Remove(aircraft);

        state.Owners.Add(new AircraftReservation
        {
            Aircraft = aircraft,
            TravelDirection = request.TravelDirection,
            LastSeenTime = Time.unscaledTime
        });

        if (!activeSectionReservations.TryGetValue(
                aircraft,
                out List<TaxiwaySectionKey> keys))
        {
            keys = new List<TaxiwaySectionKey>();
            activeSectionReservations.Add(aircraft, keys);
        }

        if (!keys.Contains(state.Section.Key))
            keys.Add(state.Section.Key);
    }

    private AircraftReservation FindSectionOwner(
        TaxiwaySectionState state,
        AircraftTaxiTrafficFollowing aircraft)
    {
        foreach (AircraftReservation owner in state.Owners)
        {
            if (owner.Aircraft == aircraft)
                return owner;
        }

        return null;
    }

    private WaitRequest FindSectionWaiter(
        TaxiwaySectionState state,
        AircraftTaxiTrafficFollowing aircraft)
    {
        foreach (WaitRequest waiter in state.Waiters)
        {
            if (waiter.Aircraft == aircraft)
                return waiter;
        }

        return null;
    }

    private void RemoveActiveSectionKey(
        AircraftTaxiTrafficFollowing aircraft,
        TaxiwaySectionKey key)
    {
        if (!activeSectionReservations.TryGetValue(
                aircraft,
                out List<TaxiwaySectionKey> keys))
        {
            return;
        }

        keys.Remove(key);

        if (keys.Count == 0)
            activeSectionReservations.Remove(aircraft);
    }

    private void ReleaseActiveSectionReservation(
        AircraftTaxiTrafficFollowing aircraft,
        TaxiwaySectionKey key)
    {
        RemoveActiveSectionKey(aircraft, key);

        if (!taxiwaySectionStates.TryGetValue(
                key,
                out TaxiwaySectionState state))
        {
            return;
        }

        for (int i = state.Owners.Count - 1; i >= 0; i--)
        {
            if (state.Owners[i].Aircraft == aircraft)
                state.Owners.RemoveAt(i);
        }
    }

    private void ReleaseWaitingSectionRequest(
        AircraftTaxiTrafficFollowing aircraft)
    {
        if (!waitingSectionRequests.TryGetValue(
                aircraft,
                out TaxiwaySectionKey key))
        {
            return;
        }

        waitingSectionRequests.Remove(aircraft);

        if (!taxiwaySectionStates.TryGetValue(
                key,
                out TaxiwaySectionState state))
        {
            return;
        }

        for (int i = state.Waiters.Count - 1; i >= 0; i--)
        {
            if (state.Waiters[i].Aircraft == aircraft)
                state.Waiters.RemoveAt(i);
        }
    }

    private void ReleaseAllSectionReservations(
        AircraftTaxiTrafficFollowing aircraft)
    {
        if (activeSectionReservations.TryGetValue(
                aircraft,
                out List<TaxiwaySectionKey> keys))
        {
            List<TaxiwaySectionKey> copy =
                new List<TaxiwaySectionKey>(keys);

            foreach (TaxiwaySectionKey key in copy)
                ReleaseActiveSectionReservation(aircraft, key);
        }

        ReleaseWaitingSectionRequest(aircraft);
    }

    private void CleanupStaleSectionReservations()
    {
        float now = Time.unscaledTime;
        List<AircraftTaxiTrafficFollowing> staleAircraft = null;

        foreach (KeyValuePair<AircraftTaxiTrafficFollowing, List<TaxiwaySectionKey>> pair
                 in activeSectionReservations)
        {
            bool stale =
                pair.Key == null ||
                !pair.Key.gameObject.activeInHierarchy;

            if (!stale)
            {
                foreach (TaxiwaySectionKey key in pair.Value)
                {
                    if (!taxiwaySectionStates.TryGetValue(
                            key,
                            out TaxiwaySectionState state))
                    {
                        stale = true;
                        break;
                    }

                    AircraftReservation reservation =
                        FindSectionOwner(state, pair.Key);

                    if (reservation == null ||
                        now - reservation.LastSeenTime > staleReservationTimeout)
                    {
                        stale = true;
                        break;
                    }
                }
            }

            if (!stale)
                continue;

            if (staleAircraft == null)
                staleAircraft = new List<AircraftTaxiTrafficFollowing>();

            staleAircraft.Add(pair.Key);
        }

        if (staleAircraft != null)
        {
            foreach (AircraftTaxiTrafficFollowing aircraft in staleAircraft)
            {
                if (aircraft != null)
                    ReleaseAllSectionReservations(aircraft);
                else
                    RemoveNullSectionReservations();
            }
        }

        foreach (TaxiwaySectionState state in taxiwaySectionStates.Values)
        {
            for (int i = state.Waiters.Count - 1; i >= 0; i--)
            {
                WaitRequest waiter = state.Waiters[i];

                if (waiter.Aircraft == null ||
                    !waiter.Aircraft.gameObject.activeInHierarchy ||
                    now - waiter.LastSeenTime > staleWaitRequestTimeout)
                {
                    if (waiter.Aircraft != null)
                        waitingSectionRequests.Remove(waiter.Aircraft);

                    state.Waiters.RemoveAt(i);
                }
            }
        }
    }

    private void RemoveNullSectionReservations()
    {
        foreach (TaxiwaySectionState state in taxiwaySectionStates.Values)
        {
            for (int i = state.Owners.Count - 1; i >= 0; i--)
            {
                if (state.Owners[i].Aircraft == null)
                    state.Owners.RemoveAt(i);
            }
        }

        List<AircraftTaxiTrafficFollowing> nullEntries = null;

        foreach (AircraftTaxiTrafficFollowing aircraft
                 in activeSectionReservations.Keys)
        {
            if (aircraft != null)
                continue;

            if (nullEntries == null)
                nullEntries = new List<AircraftTaxiTrafficFollowing>();

            nullEntries.Add(aircraft);
        }

        if (nullEntries != null)
        {
            foreach (AircraftTaxiTrafficFollowing aircraft in nullEntries)
                activeSectionReservations.Remove(aircraft);
        }
    }


    private bool HasEnteredReservedSection(
        AircraftTaxiTrafficFollowing aircraft)
    {
        if (!activeSectionReservations.TryGetValue(
                aircraft,
                out List<TaxiwaySectionKey> keys))
        {
            return false;
        }

        foreach (TaxiwaySectionKey key in keys)
        {
            if (!taxiwaySectionStates.TryGetValue(
                    key,
                    out TaxiwaySectionState state))
            {
                continue;
            }

            AircraftReservation reservation =
                FindSectionOwner(state, aircraft);

            if (reservation != null && reservation.HasEntered)
                return true;
        }

        return false;
    }

    private bool HasRouteConflictClearance(
        AircraftTaxiTrafficFollowing aircraft,
        out string decision,
        out int clearanceBoundaryIndex)
    {
        decision = "Route clear to next holding point";
        clearanceBoundaryIndex = -1;

        if (aircraft == null ||
            aircraft.PlannedTaxiRoute == null ||
            aircraft.PlannedTaxiRoute.Count < 2 ||
            gridManager == null ||
            taxiwayNetwork == null)
        {
            return true;
        }

        IReadOnlyList<Vector2Int> ownRoute = aircraft.PlannedTaxiRoute;

        if (!TryFindNearestRouteIndex(
                ownRoute,
                aircraft.transform.position,
                out int ownStartIndex,
                out _))
        {
            decision = "Aircraft is not close enough to its planned route";
            return true;
        }

        float clearRadius =
            Mathf.Max(intersectionClearRadiusCells, 1.5f) *
            gridManager.CellSize;

        // Evaluate every active aircraft. A single aircraft with priority
        // at a shared route cell is enough to make this aircraft yield.
        int yieldingConflictOwnIndex = -1;
        float nearestYieldingConflictDistance = float.MaxValue;
        AircraftTaxiTrafficFollowing yieldingAircraft = null;

        IReadOnlyList<AircraftTaxiTrafficFollowing> allAircraft =
            AircraftTaxiTrafficFollowing.ActiveAircraft;

        for (int a = 0; a < allAircraft.Count; a++)
        {
            AircraftTaxiTrafficFollowing other = allAircraft[a];

            if (other == null ||
                other == aircraft ||
                !other.isActiveAndEnabled ||
                other.PlannedTaxiRoute == null ||
                other.PlannedTaxiRoute.Count < 2)
            {
                continue;
            }

            AircraftStatus otherStatus =
                other.GetComponent<AircraftStatus>();

            if (otherStatus == null ||
                otherStatus.IsParked ||
                !otherStatus.IsTaxiing)
            {
                continue;
            }

            IReadOnlyList<Vector2Int> otherRoute = other.PlannedTaxiRoute;

            if (!TryFindNearestRouteIndex(
                    otherRoute,
                    other.transform.position,
                    out int otherStartIndex,
                    out _))
            {
                continue;
            }

            for (int ownIndex = ownStartIndex;
                 ownIndex < ownRoute.Count;
                 ownIndex++)
            {
                Vector2Int ownCell = ownRoute[ownIndex];

                for (int otherIndex = 0;
                     otherIndex < otherRoute.Count;
                     otherIndex++)
                {
                    if (otherRoute[otherIndex] != ownCell)
                        continue;

                    Vector3 ownDirection =
                        GetRouteDirection(ownRoute, ownIndex);
                    Vector3 otherDirection =
                        GetRouteDirection(otherRoute, otherIndex);

                    if (ownDirection.sqrMagnitude < 0.0001f ||
                        otherDirection.sqrMagnitude < 0.0001f)
                    {
                        continue;
                    }

                    // Same-direction traffic is handled by local following.
                    if (Vector3.Dot(ownDirection, otherDirection) > 0.75f)
                        continue;

                    Vector3 conflictPosition =
                        gridManager.GridToWorldCenter(ownCell);

                    bool otherHasPassedConflict =
                        otherIndex < otherStartIndex;

                    bool otherStillOccupiesConflict =
                        otherHasPassedConflict &&
                        HorizontalDistance(
                            other.transform.position,
                            conflictPosition) <= clearRadius;

                    if (otherHasPassedConflict &&
                        !otherStillOccupiesConflict)
                    {
                        continue;
                    }

                    float ownDistance = GetRouteDistance(
                        ownRoute,
                        ownStartIndex,
                        ownIndex);

                    float otherDistance = otherHasPassedConflict
                        ? 0f
                        : GetRouteDistance(
                            otherRoute,
                            otherStartIndex,
                            otherIndex);

                    bool ownHasPriority;

                    if (otherStillOccupiesConflict)
                    {
                        ownHasPriority = false;
                    }
                    else if (Mathf.Abs(ownDistance - otherDistance) >
                             gridManager.CellSize * 0.25f)
                    {
                        ownHasPriority = ownDistance < otherDistance;
                    }
                    else
                    {
                        ownHasPriority =
                            aircraft.GetEntityId() <
                            other.GetEntityId();
                    }

                    // Do not stop at a conflict this aircraft wins. Continue
                    // checking: another aircraft may have priority at a later
                    // conflict along the same remaining route.
                    if (ownHasPriority)
                        continue;

                    // Keep the nearest conflict that this aircraft must yield
                    // to, regardless of which other aircraft was enumerated first.
                    if (ownDistance >= nearestYieldingConflictDistance)
                        continue;

                    nearestYieldingConflictDistance = ownDistance;
                    yieldingConflictOwnIndex = ownIndex;
                    yieldingAircraft = other;
                }
            }
        }

        if (yieldingAircraft == null || yieldingConflictOwnIndex < 0)
        {
            decision = "Route clear to next holding point";
            return true;
        }

        int holdingIndex;
        Vector3 holdingPosition;
        string holdingLabel;

        FindNextRouteHoldingPoint(
            ownRoute,
            ownStartIndex,
            yieldingConflictOwnIndex,
            out holdingIndex,
            out holdingPosition,
            out holdingLabel);

        bool reachedHoldingPoint =
            ownStartIndex >= holdingIndex ||
            HorizontalDistance(
                aircraft.transform.position,
                holdingPosition) <=
            Mathf.Max(0.75f, gridManager.CellSize * 0.75f);

        if (!reachedHoldingPoint)
        {
            // Carry this exact holding index back to HasTaxiClearance so the
            // granted clearance cannot extend past the conflict hold.
            clearanceBoundaryIndex = holdingIndex;
            decision =
                "Cleared to " + holdingLabel +
                "; waiting for route conflict with " +
                yieldingAircraft.name +
                " at " + ownRoute[yieldingConflictOwnIndex];
            return true;
        }

        decision =
            "HOLD at " + holdingLabel +
            " for " + yieldingAircraft.name +
            "; conflicting route cell " + ownRoute[yieldingConflictOwnIndex];

        return false;
    }

    private int FindNextRouteBoundary(
        IReadOnlyList<Vector2Int> route,
        int currentIndex)
    {
        int startIndex = Mathf.Clamp(currentIndex + 1, 0, route.Count - 1);

        for (int i = startIndex; i < route.Count; i++)
        {
            Vector2Int cell = route[i];

            if (GetCenterlineConnectionCount(cell) >= 3)
                return Mathf.Max(currentIndex, i - 15);

            Vector3 cellPosition = gridManager.GridToWorldCenter(cell);

            for (int p = 0; p < controlPoints.Count; p++)
            {
                ControlPoint point = controlPoints[p];
                if (point == null || !point.Key.IsManual)
                    continue;

                if (HorizontalDistance(point.Position, cellPosition) <=
                    Mathf.Max(point.EntryRadius, gridManager.CellSize * 0.75f))
                {
                    return i;
                }
            }
        }

        return route.Count - 1;
    }

    private void FindNextRouteHoldingPoint(
        IReadOnlyList<Vector2Int> route,
        int currentIndex,
        int conflictIndex,
        out int holdingIndex,
        out Vector3 holdingPosition,
        out string holdingLabel)
    {
        holdingIndex = Mathf.Max(currentIndex, conflictIndex - 1);
        holdingPosition =
            gridManager.GridToWorldCenter(route[holdingIndex]);
        holdingLabel = "taxiway cell " + route[holdingIndex];

        // The conflict cell can itself be the junction (for example,
        // (80,66)). Include it when searching backwards so the holding
        // target is placed 15 route cells before that junction.
        for (int i = conflictIndex; i >= 1 && i < route.Count - 1; i--)
        {
            Vector2Int cell = route[i];

            Vector2Int incoming = cell - route[i - 1];
            Vector2Int outgoing = route[i + 1] - cell;

            bool routeChangesDirection =
                incoming != outgoing;

            bool isJunction =
                GetCenterlineConnectionCount(cell) >= 3;

            if (!isJunction && !routeChangesDirection)
                continue;

            holdingIndex = Mathf.Max(0, i - 15);
            holdingPosition =
                gridManager.GridToWorldCenter(route[holdingIndex]);
            holdingLabel = "15 cells before junction " + cell;
            return;
        }

        // If there is no automatic junction before the conflict, retain
        // the existing manual holding-point behavior.
        for (int i = currentIndex; i < conflictIndex; i++)
        {
            Vector2Int cell = route[i];
            Vector3 cellPosition = gridManager.GridToWorldCenter(cell);

            for (int p = 0; p < controlPoints.Count; p++)
            {
                ControlPoint point = controlPoints[p];
                if (point == null || !point.Key.IsManual)
                    continue;

                if (HorizontalDistance(point.Position, cellPosition) >
                    Mathf.Max(point.EntryRadius, gridManager.CellSize * 0.75f))
                {
                    continue;
                }

                holdingIndex = i;
                holdingPosition = point.Position;
                holdingLabel = "manual holding point " + point.Label;
                return;
            }
        }
    }

    private bool HasReachedRouteHoldingPoint(
        IReadOnlyList<Vector2Int> route,
        int currentIndex,
        int conflictIndex)
    {
        int holdingIndex = Mathf.Max(0, conflictIndex - 1);

        for (int i = conflictIndex - 1; i >= currentIndex; i--)
        {
            if (GetCenterlineConnectionCount(route[i]) >= 3)
            {
                holdingIndex = i;
                break;
            }
        }

        return currentIndex >= holdingIndex;
    }

    private int GetCenterlineConnectionCount(Vector2Int cell)
    {
        int count = 0;

        if (taxiwayNetwork.IsTaxiwayCenterlineCell(
                cell + Vector2Int.up))
            count++;

        if (taxiwayNetwork.IsTaxiwayCenterlineCell(
                cell + Vector2Int.right))
            count++;

        if (taxiwayNetwork.IsTaxiwayCenterlineCell(
                cell + Vector2Int.down))
            count++;

        if (taxiwayNetwork.IsTaxiwayCenterlineCell(
                cell + Vector2Int.left))
            count++;

        return count;
    }

    private bool TryFindNearestRouteIndex(
        IReadOnlyList<Vector2Int> route,
        Vector3 worldPosition,
        out int nearestIndex,
        out float nearestDistance)
    {
        nearestIndex = -1;
        nearestDistance = float.MaxValue;

        for (int i = 0; i < route.Count; i++)
        {
            Vector3 cellPosition =
                gridManager.GridToWorldCenter(route[i]);

            float distance =
                HorizontalDistance(worldPosition, cellPosition);

            if (distance >= nearestDistance)
                continue;

            nearestDistance = distance;
            nearestIndex = i;
        }

        float maximumDistance =
            (centerlineSearchRadiusCells + 1f) *
            gridManager.CellSize;

        return nearestIndex >= 0 &&
               nearestDistance <= maximumDistance;
    }

    private Vector3 GetRouteDirection(
        IReadOnlyList<Vector2Int> route,
        int index)
    {
        // Use the outgoing route segment at this cell. Averaging the incoming
        // and outgoing segments misclassifies ordinary 90-degree turns.
        int nextIndex = Mathf.Min(route.Count - 1, index + 1);
        int previousIndex = Mathf.Max(0, index - 1);

        Vector3 direction =
            gridManager.GridToWorldCenter(route[nextIndex]) -
            gridManager.GridToWorldCenter(route[index]);

        if (direction.sqrMagnitude < 0.0001f)
        {
            direction =
                gridManager.GridToWorldCenter(route[index]) -
                gridManager.GridToWorldCenter(route[previousIndex]);
        }

        direction.y = 0f;

        if (direction.sqrMagnitude < 0.0001f)
            return Vector3.zero;

        return direction.normalized;
    }

    private float GetRouteDistance(
        IReadOnlyList<Vector2Int> route,
        int startIndex,
        int endIndex)
    {
        float distance = 0f;

        for (int i = startIndex + 1; i <= endIndex; i++)
        {
            Vector3 previous =
                gridManager.GridToWorldCenter(route[i - 1]);
            Vector3 current =
                gridManager.GridToWorldCenter(route[i]);

            distance += HorizontalDistance(previous, current);
        }

        return distance;
    }

    private bool IsNearTaxiwayCenterline(Vector3 worldPosition)
    {
        Vector2Int centerCell =
            gridManager.WorldToGrid(worldPosition);

        int searchRadius =
            Mathf.CeilToInt(centerlineSearchRadiusCells);

        float maxDistance =
            centerlineSearchRadiusCells * gridManager.CellSize;

        float maxDistanceSquared =
            maxDistance * maxDistance;

        for (int x = -searchRadius; x <= searchRadius; x++)
        {
            for (int z = -searchRadius; z <= searchRadius; z++)
            {
                Vector2Int candidate =
                    centerCell + new Vector2Int(x, z);

                if (!taxiwayNetwork.IsTaxiwayCenterlineCell(candidate))
                    continue;

                Vector3 center =
                    gridManager.GridToWorldCenter(candidate);

                if (HorizontalDistanceSquared(worldPosition, center) <=
                    maxDistanceSquared)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool TryFindNextControlPoint(
        Transform aircraft,
        out ControlPoint selectedPoint)
    {
        selectedPoint = null;

        Vector3 forward = aircraft.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude < 0.0001f)
            return false;

        forward.Normalize();

        float cellSize = gridManager.CellSize;
        float approachDistance =
            approachDistanceCells * cellSize;
        float lateralTolerance =
            centerlineSearchRadiusCells * cellSize;

        float bestDistance = float.MaxValue;

        foreach (ControlPoint point in controlPoints)
        {
            Vector3 toPoint = point.Position - aircraft.position;
            toPoint.y = 0f;

            float distance = toPoint.magnitude;

            if (distance > approachDistance)
                continue;

            float forwardDistance = Vector3.Dot(toPoint, forward);

            if (forwardDistance < -cellSize * 0.25f)
                continue;

            float lateralDistance =
                Mathf.Sqrt(Mathf.Max(
                    0f,
                    toPoint.sqrMagnitude -
                    forwardDistance * forwardDistance));

            if (lateralDistance > lateralTolerance)
                continue;

            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            selectedPoint = point;
        }

        return selectedPoint != null;
    }

    private bool UpdateExistingReservation(
        AircraftTaxiTrafficFollowing aircraft)
    {
        if (!activeReservations.TryGetValue(
                aircraft,
                out ControlPointKey key))
        {
            return false;
        }

        if (!pointStates.TryGetValue(
                key,
                out ControlPointState state))
        {
            activeReservations.Remove(aircraft);
            return false;
        }

        AircraftReservation reservation =
            FindOwnerReservation(state, aircraft);

        if (reservation == null)
        {
            activeReservations.Remove(aircraft);
            return false;
        }

        reservation.LastSeenTime = Time.unscaledTime;

        float distance = HorizontalDistance(
            aircraft.transform.position,
            state.Point.Position);

        if (distance <= state.Point.EntryRadius)
            reservation.HasEntered = true;

        bool clearedPoint =
            reservation.HasEntered &&
            distance >= state.Point.ClearRadius;

        bool movedAwayBeforeEntry =
            !reservation.HasEntered &&
            distance >
                approachDistanceCells * gridManager.CellSize +
                gridManager.CellSize;

        if (clearedPoint || movedAwayBeforeEntry)
        {
            ReleaseActiveReservation(aircraft);
            return false;
        }

        return true;
    }

    private bool RequestControlPoint(
        AircraftTaxiTrafficFollowing aircraft,
        ControlPoint point,
        Vector3 travelDirection,
        out string decision)
    {
        decision = string.Empty;

        travelDirection.y = 0f;

        if (travelDirection.sqrMagnitude < 0.0001f)
            travelDirection = aircraft.transform.forward;

        travelDirection.y = 0f;
        travelDirection.Normalize();

        if (waitingRequests.TryGetValue(
                aircraft,
                out ControlPointKey previousKey) &&
            !previousKey.Equals(point.Key))
        {
            ReleaseWaitingRequest(aircraft);
        }

        if (!pointStates.TryGetValue(
                point.Key,
                out ControlPointState state))
        {
            decision = "Control point is being rebuilt";
            return false;
        }

        WaitRequest request = FindWaitRequest(state, aircraft);

        if (request == null)
        {
            request = new WaitRequest
            {
                Aircraft = aircraft,
                TravelDirection = travelDirection,
                RequestedAt = Time.unscaledTime,
                LastSeenTime = Time.unscaledTime
            };

            state.Waiters.Add(request);
            waitingRequests[aircraft] = point.Key;
        }
        else
        {
            request.TravelDirection = travelDirection;
            request.LastSeenTime = Time.unscaledTime;
        }

        // A manual holding point may pass a same-direction convoy together.
        // The local traffic follower still controls following distance.
        if (state.Point.AllowSameDirectionTraffic &&
            HasSameDirectionOwner(state, travelDirection) &&
            !HasEarlierOpposingWaiter(state, request))
        {
            GrantReservation(state, request);
            decision = "Same-direction traffic cleared at " +
                       state.Point.Label;
            return true;
        }

        if (state.Owners.Count == 0 &&
            IsOldestActiveWaiter(state, request))
        {
            GrantReservation(state, request);
            decision = "Cleared at " + state.Point.Label;
            return true;
        }

        AircraftReservation blockingOwner =
            state.Owners.Count > 0 ? state.Owners[0] : null;

        string ownerName =
            blockingOwner != null && blockingOwner.Aircraft != null
                ? blockingOwner.Aircraft.name
                : "traffic ahead";

        decision = "Holding for " + state.Point.Label +
                   " (" + ownerName + ")";
        return false;
    }

    private bool HasEarlierOpposingWaiter(
        ControlPointState state,
        WaitRequest requestingWaiter)
    {
        foreach (WaitRequest waiter in state.Waiters)
        {
            if (waiter == requestingWaiter ||
                waiter.Aircraft == null ||
                waiter.RequestedAt >= requestingWaiter.RequestedAt)
            {
                continue;
            }

            if (Vector3.Dot(
                    waiter.TravelDirection,
                    requestingWaiter.TravelDirection) < 0.85f)
            {
                return true;
            }
        }

        return false;
    }

    private bool HasSameDirectionOwner(
        ControlPointState state,
        Vector3 direction)
    {
        if (state.Owners.Count == 0)
            return false;

        foreach (AircraftReservation owner in state.Owners)
        {
            if (owner.Aircraft == null)
                continue;

            if (Vector3.Dot(
                    owner.TravelDirection,
                    direction) < 0.85f)
            {
                return false;
            }
        }

        return true;
    }

    private bool IsOldestActiveWaiter(
        ControlPointState state,
        WaitRequest requestingWaiter)
    {
        WaitRequest oldest = null;

        foreach (WaitRequest waiter in state.Waiters)
        {
            if (waiter.Aircraft == null ||
                !waiter.Aircraft.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (oldest == null ||
                waiter.RequestedAt < oldest.RequestedAt)
            {
                oldest = waiter;
            }
        }

        return oldest == requestingWaiter;
    }

    private void GrantReservation(
        ControlPointState state,
        WaitRequest request)
    {
        AircraftTaxiTrafficFollowing aircraft = request.Aircraft;

        if (aircraft == null)
            return;

        state.Waiters.Remove(request);
        waitingRequests.Remove(aircraft);

        AircraftReservation reservation =
            new AircraftReservation
            {
                Aircraft = aircraft,
                TravelDirection = request.TravelDirection,
                LastSeenTime = Time.unscaledTime
            };

        state.Owners.Add(reservation);
        activeReservations[aircraft] = state.Point.Key;
    }

    private AircraftReservation FindOwnerReservation(
        ControlPointState state,
        AircraftTaxiTrafficFollowing aircraft)
    {
        foreach (AircraftReservation reservation in state.Owners)
        {
            if (reservation.Aircraft == aircraft)
                return reservation;
        }

        return null;
    }

    private WaitRequest FindWaitRequest(
        ControlPointState state,
        AircraftTaxiTrafficFollowing aircraft)
    {
        foreach (WaitRequest request in state.Waiters)
        {
            if (request.Aircraft == aircraft)
                return request;
        }

        return null;
    }

    private void ReleaseActiveReservation(
        AircraftTaxiTrafficFollowing aircraft)
    {
        if (!activeReservations.TryGetValue(
                aircraft,
                out ControlPointKey key))
        {
            return;
        }

        activeReservations.Remove(aircraft);

        if (!pointStates.TryGetValue(
                key,
                out ControlPointState state))
        {
            return;
        }

        for (int i = state.Owners.Count - 1; i >= 0; i--)
        {
            if (state.Owners[i].Aircraft == aircraft)
                state.Owners.RemoveAt(i);
        }
    }

    private void ReleaseWaitingRequest(
        AircraftTaxiTrafficFollowing aircraft)
    {
        if (!waitingRequests.TryGetValue(
                aircraft,
                out ControlPointKey key))
        {
            return;
        }

        waitingRequests.Remove(aircraft);

        if (!pointStates.TryGetValue(
                key,
                out ControlPointState state))
        {
            return;
        }

        for (int i = state.Waiters.Count - 1; i >= 0; i--)
        {
            if (state.Waiters[i].Aircraft == aircraft)
                state.Waiters.RemoveAt(i);
        }
    }

    private void CleanupStaleReservations()
    {
        float now = Time.unscaledTime;
        List<AircraftTaxiTrafficFollowing> staleAircraft = null;

        foreach (KeyValuePair<AircraftTaxiTrafficFollowing, ControlPointKey> pair
                 in activeReservations)
        {
            if (pair.Key == null ||
                !pair.Key.gameObject.activeInHierarchy)
            {
                if (staleAircraft == null)
                    staleAircraft = new List<AircraftTaxiTrafficFollowing>();

                staleAircraft.Add(pair.Key);
                continue;
            }

            if (!pointStates.TryGetValue(
                    pair.Value,
                    out ControlPointState state))
            {
                if (staleAircraft == null)
                    staleAircraft = new List<AircraftTaxiTrafficFollowing>();

                staleAircraft.Add(pair.Key);
                continue;
            }

            AircraftReservation reservation =
                FindOwnerReservation(state, pair.Key);

            if (reservation == null ||
                now - reservation.LastSeenTime > staleReservationTimeout)
            {
                if (staleAircraft == null)
                    staleAircraft = new List<AircraftTaxiTrafficFollowing>();

                staleAircraft.Add(pair.Key);
            }
        }

        if (staleAircraft != null)
        {
            foreach (AircraftTaxiTrafficFollowing aircraft in staleAircraft)
            {
                if (aircraft != null)
                    ReleaseActiveReservation(aircraft);
                else
                    RemoveNullActiveReservation();
            }
        }

        foreach (ControlPointState state in pointStates.Values)
        {
            for (int i = state.Waiters.Count - 1; i >= 0; i--)
            {
                WaitRequest waiter = state.Waiters[i];

                if (waiter.Aircraft == null ||
                    !waiter.Aircraft.gameObject.activeInHierarchy ||
                    now - waiter.LastSeenTime > staleWaitRequestTimeout)
                {
                    if (waiter.Aircraft != null)
                        waitingRequests.Remove(waiter.Aircraft);

                    state.Waiters.RemoveAt(i);
                }
            }
        }
    }

    private void RemoveNullActiveReservation()
    {
        List<AircraftTaxiTrafficFollowing> nullEntries = null;

        foreach (AircraftTaxiTrafficFollowing aircraft
                 in activeReservations.Keys)
        {
            if (aircraft != null)
                continue;

            if (nullEntries == null)
                nullEntries = new List<AircraftTaxiTrafficFollowing>();

            nullEntries.Add(aircraft);
        }

        if (nullEntries == null)
            return;

        foreach (AircraftTaxiTrafficFollowing aircraft in nullEntries)
            activeReservations.Remove(aircraft);
    }

    private static float HorizontalDistance(
        Vector3 first,
        Vector3 second)
    {
        first.y = 0f;
        second.y = 0f;
        return Vector3.Distance(first, second);
    }

    private static float HorizontalDistanceSquared(
        Vector3 first,
        Vector3 second)
    {
        first.y = 0f;
        second.y = 0f;
        return (first - second).sqrMagnitude;
    }

    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying)
            return;

        foreach (ControlPoint point in controlPoints)
        {
            Gizmos.color = point.AllowSameDirectionTraffic
                ? Color.yellow
                : Color.cyan;

            Gizmos.DrawWireSphere(
                point.Position,
                Mathf.Max(0.25f, point.EntryRadius));
        }
    }
}