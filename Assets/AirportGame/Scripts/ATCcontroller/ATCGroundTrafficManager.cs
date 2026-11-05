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

    [Header("Scene View Debugging")]
    [SerializeField] private bool showSceneDebug = true;
    [SerializeField] private bool showControlPoints = true;
    [SerializeField] private bool showReservations = true;
    [SerializeField] private bool showWaitingQueues = true;
    [SerializeField] private bool showDebugLabels = true;
    [Min(0.1f)]
    [SerializeField] private float debugLabelHeight = 2f;
    [Min(0.1f)]
    [SerializeField] private float directionArrowLength = 1.5f;

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

    private int cachedNetworkVersion = int.MinValue;
    private int cachedManualPointVersion = int.MinValue;
    private float nextCleanupTime;

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
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        activeReservations.Clear();
        waitingRequests.Clear();
        pointStates.Clear();
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

        ResolveReferences();

        if (gridManager == null || taxiwayNetwork == null)
        {
            decision = "ATC references not ready";
            return true;
        }

        EnsureControlPoints();

        if (!IsNearTaxiwayCenterline(aircraft.transform.position))
        {
            ReleaseWaitingRequest(aircraft);
            decision = "Outside taxiway centerline";
            return true;
        }

        if (UpdateExistingReservation(aircraft))
        {
            decision = "Cleared through reserved section";
            return true;
        }

        if (!TryFindNextControlPoint(
                aircraft.transform,
                out ControlPoint nextPoint))
        {
            ReleaseWaitingRequest(aircraft);
            decision = "No conflict point ahead";
            return true;
        }

        bool granted = RequestControlPoint(
            aircraft,
            nextPoint,
            aircraft.transform.forward,
            out decision);

        return granted;
    }

    /// <summary>
    /// Releases any current or queued clearance when an aircraft stops
    /// taxiing or its traffic-following component is disabled.
    /// </summary>
    public void ReleaseAircraft(
        AircraftTaxiTrafficFollowing aircraft)
    {
        if (aircraft == null)
            return;

        ReleaseActiveReservation(aircraft);
        ReleaseWaitingRequest(aircraft);
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
        if (!Application.isPlaying || !showSceneDebug)
            return;

        foreach (ControlPointState state in pointStates.Values)
        {
            if (state == null || state.Point == null)
                continue;

            DrawControlPointDebug(state);
        }
    }

    private void DrawControlPointDebug(ControlPointState state)
    {
        ControlPoint point = state.Point;
        Vector3 labelPosition =
            point.Position + Vector3.up * debugLabelHeight;

        Color pointColor;

        if (state.Owners.Count > 1)
            pointColor = new Color(0.1f, 0.55f, 1f, 1f);
        else if (state.Waiters.Count > 0)
            pointColor = new Color(1f, 0.2f, 0.15f, 1f);
        else if (state.Owners.Count > 0)
            pointColor = new Color(1f, 0.7f, 0.1f, 1f);
        else
            pointColor = new Color(0.2f, 0.85f, 0.35f, 1f);

#if UNITY_EDITOR
        UnityEngine.Rendering.CompareFunction previousZTest =
            UnityEditor.Handles.zTest;
        UnityEditor.Handles.zTest =
            UnityEngine.Rendering.CompareFunction.Always;

        if (showControlPoints)
        {
            UnityEditor.Handles.color = pointColor;
            Vector3 discPosition = point.Position + Vector3.up * 0.35f;
            UnityEditor.Handles.DrawSolidDisc(discPosition, Vector3.up, 0.32f);
            UnityEditor.Handles.DrawWireDisc(
                discPosition, Vector3.up, Mathf.Max(0.35f, point.EntryRadius));
            UnityEditor.Handles.color =
                new Color(pointColor.r, pointColor.g, pointColor.b, 0.65f);
            UnityEditor.Handles.DrawWireDisc(
                discPosition, Vector3.up,
                Mathf.Max(point.EntryRadius + 0.1f, point.ClearRadius));
        }
#endif

        if (showControlPoints)
        {
            Gizmos.color = pointColor;
            Gizmos.DrawSphere(
                point.Position + Vector3.up * 0.12f,
                0.22f);
            Gizmos.DrawWireSphere(
                point.Position,
                Mathf.Max(0.25f, point.EntryRadius));
            Gizmos.DrawWireSphere(
                point.Position,
                Mathf.Max(point.EntryRadius + 0.1f, point.ClearRadius));
        }

        System.Text.StringBuilder label =
            new System.Text.StringBuilder();

        label.Append(point.Label);

        if (state.Owners.Count > 0)
            label.Append("\nGranted: ").Append(state.Owners.Count);

        if (state.Waiters.Count > 0)
            label.Append("\nWaiting: ").Append(state.Waiters.Count);

        if (showReservations)
        {
            foreach (AircraftReservation owner in state.Owners)
            {
                if (owner.Aircraft == null)
                    continue;

                Vector3 aircraftPosition =
                    owner.Aircraft.transform.position + Vector3.up * 0.3f;

                DrawDebugConnection(
                    aircraftPosition, point.Position,
                    owner.TravelDirection, pointColor);

                label.Append("\nGRANTED: ")
                    .Append(owner.Aircraft.name)
                    .Append(owner.HasEntered ? " (inside)" : " (approaching)");
            }
        }

        if (showWaitingQueues)
        {
            // Waiters are appended in request order, so the list is
            // already ordered by priority. Avoid allocations in gizmo drawing.
            for (int i = 0; i < state.Waiters.Count; i++)
            {
                WaitRequest waiter = state.Waiters[i];

                if (waiter.Aircraft == null)
                    continue;

                Vector3 aircraftPosition =
                    waiter.Aircraft.transform.position + Vector3.up * 0.3f;

                Color waitingColor = new Color(1f, 0.2f, 0.15f, 1f);
                DrawDebugConnection(
                    aircraftPosition, point.Position,
                    waiter.TravelDirection, waitingColor);

                float waitSeconds =
                    Mathf.Max(0f, Time.unscaledTime - waiter.RequestedAt);

                label.Append("\n")
                    .Append(i + 1)
                    .Append(". WAIT: ")
                    .Append(waiter.Aircraft.name)
                    .Append(" (")
                    .Append(waitSeconds.ToString("0.0"))
                    .Append("s)");
            }
        }

#if UNITY_EDITOR
        if (showDebugLabels)
        {
            UnityEditor.Handles.color = pointColor;
            UnityEditor.Handles.Label(labelPosition, label.ToString());
        }

        UnityEditor.Handles.zTest = previousZTest;
#endif
    }

    private void DrawDebugConnection(
        Vector3 aircraftPosition,
        Vector3 controlPointPosition,
        Vector3 direction,
        Color color)
    {
        Gizmos.color = color;
        Gizmos.DrawLine(aircraftPosition, controlPointPosition);
        DrawDirectionArrow(aircraftPosition, direction, color);

#if UNITY_EDITOR
        UnityEditor.Handles.color = color;
        UnityEditor.Handles.DrawAAPolyLine(4f, aircraftPosition, controlPointPosition);

        direction.y = 0f;
        if (direction.sqrMagnitude > 0.0001f)
        {
            direction.Normalize();
            Vector3 end = aircraftPosition + direction * directionArrowLength;
            Vector3 side = Vector3.Cross(Vector3.up, direction).normalized;
            float headLength = directionArrowLength * 0.25f;
            float headWidth = headLength * 0.55f;
            UnityEditor.Handles.DrawAAPolyLine(
                4f, end,
                end - direction * headLength + side * headWidth);
            UnityEditor.Handles.DrawAAPolyLine(
                4f, end,
                end - direction * headLength - side * headWidth);
        }
#endif
    }

    private void DrawDirectionArrow(
        Vector3 start,
        Vector3 direction,
        Color color)
    {
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.0001f)
            return;

        direction.Normalize();

        Vector3 end = start + direction * directionArrowLength;
        Vector3 side = Vector3.Cross(Vector3.up, direction).normalized;
        float arrowHeadLength = directionArrowLength * 0.25f;
        float arrowHeadWidth = arrowHeadLength * 0.55f;

        Gizmos.color = color;
        Gizmos.DrawLine(start, end);
        Gizmos.DrawLine(
            end,
            end - direction * arrowHeadLength + side * arrowHeadWidth);
        Gizmos.DrawLine(
            end,
            end - direction * arrowHeadLength - side * arrowHeadWidth);
    }
}