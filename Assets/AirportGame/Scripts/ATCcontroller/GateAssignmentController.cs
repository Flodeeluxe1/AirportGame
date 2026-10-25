
using System.Collections.Generic;
using UnityEngine;

public class GateAssignmentController : MonoBehaviour
{
    public static GateAssignmentController Instance { get; private set; }

    [Header("References")]
    [SerializeField] private AircraftGateRegistry gateRegistry;
    [SerializeField] private GridManager gridManager;
    [SerializeField] private TaxiwayNetwork taxiwayNetwork;
    [SerializeField] private TaxiwayPathfinder taxiwayPathfinder;

    [Header("Taxiway Reachability")]
    [Min(1)]
    [SerializeField] private int connectionSearchRadiusCells = 12;

    [Range(1, 10)]
    [SerializeField] private int maximumConnectionCandidates = 4;

    [Min(1)]
    [SerializeField] private int maximumRouteAttemptsPerGate = 16;

    private readonly Dictionary<string, AircraftFlightIdentity>
        gateReservations =
            new Dictionary<string, AircraftFlightIdentity>();

    private struct TaxiwayCandidate
    {
        public Vector2Int Cell;
        public float DistanceSquared;

        public TaxiwayCandidate(
            Vector2Int cell,
            float distanceSquared)
        {
            Cell = cell;
            DistanceSquared = distanceSquared;
        }
    }

    private struct RouteCandidatePair
    {
        public TaxiwayCandidate Start;
        public TaxiwayCandidate Target;
        public float CombinedDistanceSquared;

        public RouteCandidatePair(
            TaxiwayCandidate start,
            TaxiwayCandidate target)
        {
            Start = start;
            Target = target;

            CombinedDistanceSquared =
                start.DistanceSquared + target.DistanceSquared;
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError(
                "Multiple GateAssignmentController instances exist. " +
                "Only one should be present in the scene.",
                this
            );

            enabled = false;
            return;
        }

        Instance = this;

        if (gateRegistry == null)
            gateRegistry = FindFirstObjectByType<AircraftGateRegistry>();

        if (gridManager == null)
            gridManager = FindFirstObjectByType<GridManager>();

        if (taxiwayNetwork == null)
            taxiwayNetwork = FindFirstObjectByType<TaxiwayNetwork>();

        if (taxiwayPathfinder == null)
            taxiwayPathfinder = FindFirstObjectByType<TaxiwayPathfinder>();

        if (gateRegistry == null ||
            gridManager == null ||
            taxiwayNetwork == null ||
            taxiwayPathfinder == null)
        {
            Debug.LogError(
                "GateAssignmentController: Missing required references. " +
                "Assign AircraftGateRegistry, GridManager, TaxiwayNetwork, " +
                "and TaxiwayPathfinder in the Inspector.",
                this
            );

            enabled = false;
        }
    }

    public bool TryAssignGate(
        AircraftFlightIdentity aircraft,
        out AircraftGate assignedGate,
        out string reason)
    {
        assignedGate = null;
        reason = string.Empty;

        if (aircraft == null || !aircraft.IsInitialized)
        {
            reason =
                "Cannot assign a gate: aircraft flight identity " +
                "is missing or not initialized.";

            return false;
        }

        if (gateRegistry == null ||
            gridManager == null ||
            taxiwayNetwork == null ||
            taxiwayPathfinder == null)
        {
            reason =
                "Cannot assign a gate: required gate or taxiway references " +
                "are missing.";

            return false;
        }

        gateRegistry.RefreshGates();
        RemoveDestroyedAircraftReservations();

        // Preserve a reservation previously made by this controller.
        if (aircraft.HasActualGate &&
            gateReservations.TryGetValue(
                aircraft.ActualGateStableId,
                out AircraftFlightIdentity currentOwner) &&
            currentOwner == aircraft &&
            gateRegistry.TryGetGate(
                aircraft.ActualGateStableId,
                out AircraftGate existingGate))
        {
            assignedGate = existingGate;

            reason =
                $"{aircraft.FlightNumber} already has " +
                $"{existingGate.DisplayName} reserved.";

            return true;
        }

        int minimumTaxiwayWidth =
            GetMinimumTaxiwayWidth(
                aircraft.Definition.RequiredGateSize
            );

        List<TaxiwayCandidate> startCandidates =
            FindNearbyCenterlineCells(
                aircraft.transform.position,
                minimumTaxiwayWidth
            );

        if (startCandidates.Count == 0)
        {
            reason =
                $"No suitable taxiway centerline was found near " +
                $"{aircraft.FlightNumber}. No gate was reserved.";

            Debug.LogWarning(reason, aircraft);
            return false;
        }

        // First choice: the planned gate, but only if a usable route exists.
        if (gateRegistry.TryGetGate(
                aircraft.PlannedGateStableId,
                out AircraftGate plannedGate))
        {
            if (!plannedGate.CanAccommodate(
                    aircraft.Definition.RequiredGateSize))
            {
                reason =
                    $"Planned gate {plannedGate.DisplayName} is too small.";
            }
            else if (!IsGateAvailable(plannedGate, aircraft))
            {
                reason =
                    $"Planned gate {plannedGate.DisplayName} is already reserved.";
            }
            else if (TryFindTaxiRoute(
                         startCandidates,
                         plannedGate,
                         minimumTaxiwayWidth,
                         out int plannedRouteLength))
            {
                ReserveGate(aircraft, plannedGate);

                assignedGate = plannedGate;

                reason =
                    $"{aircraft.FlightNumber} assigned to planned gate " +
                    $"{plannedGate.DisplayName}. " +
                    $"A* route length: {plannedRouteLength} cells.";

                Debug.Log(
                    $"GATE ASSIGNED | Flight: {aircraft.FlightNumber} | " +
                    $"Planned: {aircraft.PlannedGateStableId} | " +
                    $"Actual: {plannedGate.StableId} | " +
                    $"Reachable: Yes | " +
                    $"Route cells: {plannedRouteLength}",
                    aircraft
                );

                return true;
            }
            else
            {
                reason =
                    $"No taxiway route connects the aircraft to planned gate " +
                    $"{plannedGate.DisplayName}.";
            }
        }
        else
        {
            reason =
                $"Planned gate '{aircraft.PlannedGateStableId}' was not found.";
        }

        // Fallback: select only a compatible, unreserved, reachable gate.
        IReadOnlyList<AircraftGate> candidates =
            gateRegistry.GetCompatibleGates(
                aircraft.Definition.RequiredGateSize
            );

        int bestRouteLength = int.MaxValue;
        float bestDistanceSquared = float.MaxValue;

        foreach (AircraftGate candidate in candidates)
        {
            if (candidate == null ||
                candidate.StableId == aircraft.PlannedGateStableId ||
                !IsGateAvailable(candidate, aircraft))
            {
                continue;
            }

            if (!candidate.CanAccommodate(
                    aircraft.Definition.RequiredGateSize))
            {
                continue;
            }

            if (!TryFindTaxiRoute(
                    startCandidates,
                    candidate,
                    minimumTaxiwayWidth,
                    out int routeLength))
            {
                continue;
            }

            float distanceSquared =
                HorizontalDistanceSquared(
                    aircraft.transform.position,
                    candidate.transform.position
                );

            // Prefer the shortest viable A* path. Use proximity as a
            // tie-breaker between routes with the same number of cells.
            bool isBetter =
                routeLength < bestRouteLength ||
                (routeLength == bestRouteLength &&
                 distanceSquared < bestDistanceSquared);

            if (!isBetter)
                continue;

            bestRouteLength = routeLength;
            bestDistanceSquared = distanceSquared;
            assignedGate = candidate;
        }

        if (assignedGate == null)
        {
            reason =
                $"{aircraft.FlightNumber}: planned gate unavailable or " +
                "unreachable, and no compatible, unreserved alternative " +
                "with a valid A* route was found.";

            Debug.LogWarning(reason, aircraft);
            return false;
        }

        ReserveGate(aircraft, assignedGate);

        reason =
            $"{aircraft.FlightNumber}: planned gate " +
            $"{aircraft.PlannedGateStableId} could not be used. " +
            $"Reachable alternative {assignedGate.DisplayName} assigned.";

        Debug.LogWarning(
            $"GATE REASSIGNED | Flight: {aircraft.FlightNumber} | " +
            $"Planned: {aircraft.PlannedGateStableId} | " +
            $"Actual: {assignedGate.StableId} | " +
            $"Reachable: Yes | Route cells: {bestRouteLength}",
            aircraft
        );

        return true;
    }


    private bool TryFindTaxiRoute(
        List<TaxiwayCandidate> startCandidates,
        AircraftGate gate,
        int minimumTaxiwayWidth,
        out int bestRouteLength)
    {
        bestRouteLength = int.MaxValue;

        if (gate == null || startCandidates.Count == 0)
        {
            Debug.LogWarning(
                $"GATE ROUTE CHECK FAILED | " +
                $"Gate: {(gate != null ? gate.DisplayName : "null")} | " +
                $"Start candidates: " +
                $"{(startCandidates != null ? startCandidates.Count : 0)}"
            );

            return false;
        }

        List<TaxiwayCandidate> targetCandidates =
            FindNearbyCenterlineCells(
                gate.transform.position,
                minimumTaxiwayWidth
            );

        if (targetCandidates.Count == 0)
        {
            Vector2Int gateCell =
                gridManager.WorldToGrid(gate.transform.position);

            Debug.LogWarning(
                $"GATE ROUTE CHECK FAILED | Gate: {gate.DisplayName} | " +
                $"Grid cell: {gateCell} | " +
                $"Gate size: {gate.Size} | " +
                $"Required taxiway width: {minimumTaxiwayWidth} | " +
                $"No suitable centerline candidates within " +
                $"{connectionSearchRadiusCells} cells.",
                gate
            );

            return false;
        }

        List<RouteCandidatePair> pairs =
            BuildCandidatePairs(startCandidates, targetCandidates);

        int attempts = 0;
        int successfulRoutes = 0;

        foreach (RouteCandidatePair pair in pairs)
        {
            if (attempts >= maximumRouteAttemptsPerGate)
                break;

            attempts++;

            List<Vector2Int> path =
                taxiwayPathfinder.FindPath(
                    pair.Start.Cell,
                    pair.Target.Cell,
                    minimumTaxiwayWidth
                );

            if (path == null || path.Count == 0)
            {
                Debug.Log(
                    $"A* ROUTE FAILED | Gate: {gate.DisplayName} | " +
                    $"Start: {pair.Start.Cell} | " +
                    $"Target: {pair.Target.Cell} | " +
                    $"Minimum width: {minimumTaxiwayWidth}",
                    gate
                );

                continue;
            }

            successfulRoutes++;

            if (path.Count < bestRouteLength)
                bestRouteLength = path.Count;
        }

        bool foundRoute = bestRouteLength != int.MaxValue;

        Debug.Log(
            $"GATE ROUTE RESULT | Gate: {gate.DisplayName} | " +
            $"Size: {gate.Size} | " +
            $"Required width: {minimumTaxiwayWidth} | " +
            $"Start candidates: {startCandidates.Count} | " +
            $"Gate candidates: {targetCandidates.Count} | " +
            $"Pairs tested: {attempts} | " +
            $"Successful routes: {successfulRoutes} | " +
            $"Result: {(foundRoute ? "ROUTE FOUND" : "NO ROUTE")}" +
            (foundRoute
                ? $" | Best path: {bestRouteLength} cells"
                : ""),
            gate
        );

        return foundRoute;
    }


    private List<TaxiwayCandidate> FindNearbyCenterlineCells(
        Vector3 worldPosition,
        int minimumTaxiwayWidth)
    {
        List<TaxiwayCandidate> candidates =
            new List<TaxiwayCandidate>();

        Vector2Int center =
            gridManager.WorldToGrid(worldPosition);

        int radius = connectionSearchRadiusCells;

        for (int xOffset = -radius; xOffset <= radius; xOffset++)
        {
            for (int zOffset = -radius; zOffset <= radius; zOffset++)
            {
                Vector2Int cell = new Vector2Int(
                    center.x + xOffset,
                    center.y + zOffset
                );

                if (!gridManager.IsInsideGrid(cell))
                    continue;

                if (!taxiwayNetwork.IsTaxiway(cell))
                    continue;

                if (!taxiwayNetwork.IsCenterlineCell(cell))
                    continue;

                if (taxiwayNetwork.GetTaxiwayWidth(cell) <
                    minimumTaxiwayWidth)
                {
                    continue;
                }

                Vector3 cellWorldPosition =
                    gridManager.GridToWorldCenter(cell);

                candidates.Add(
                    new TaxiwayCandidate(
                        cell,
                        HorizontalDistanceSquared(
                            worldPosition,
                            cellWorldPosition
                        )
                    )
                );
            }
        }

        candidates.Sort(
            (a, b) => a.DistanceSquared.CompareTo(b.DistanceSquared)
        );

        if (candidates.Count > maximumConnectionCandidates)
        {
            candidates.RemoveRange(
                maximumConnectionCandidates,
                candidates.Count - maximumConnectionCandidates
            );
        }

        return candidates;
    }

    private List<RouteCandidatePair> BuildCandidatePairs(
        List<TaxiwayCandidate> startCandidates,
        List<TaxiwayCandidate> targetCandidates)
    {
        List<RouteCandidatePair> pairs =
            new List<RouteCandidatePair>();

        foreach (TaxiwayCandidate start in startCandidates)
        {
            foreach (TaxiwayCandidate target in targetCandidates)
            {
                pairs.Add(
                    new RouteCandidatePair(start, target)
                );
            }
        }

        pairs.Sort(
            (a, b) =>
                a.CombinedDistanceSquared.CompareTo(
                    b.CombinedDistanceSquared
                )
        );

        return pairs;
    }

    private int GetMinimumTaxiwayWidth(
        AircraftGateSize requiredGateSize)
    {
        switch (requiredGateSize)
        {
            case AircraftGateSize.Grass:
                return 3;

            case AircraftGateSize.Small:
                return 3;

            case AircraftGateSize.Middle:
                return 5;

            case AircraftGateSize.Big:
                return 7;

            default:
                return 3;
        }
    }

    public void ReleaseGate(AircraftFlightIdentity aircraft)
    {
        if (aircraft == null)
            return;

        List<string> gatesToRelease = new List<string>();

        foreach (KeyValuePair<string, AircraftFlightIdentity> reservation
                 in gateReservations)
        {
            if (reservation.Value == aircraft)
                gatesToRelease.Add(reservation.Key);
        }

        foreach (string gateId in gatesToRelease)
            gateReservations.Remove(gateId);

        aircraft.ClearActualGate();
    }

    public bool IsGateAvailable(
        AircraftGate gate,
        AircraftFlightIdentity requestingAircraft = null)
    {
        if (gate == null || string.IsNullOrEmpty(gate.StableId))
            return false;

        if (!gateReservations.TryGetValue(
                gate.StableId,
                out AircraftFlightIdentity owner))
        {
            return true;
        }

        if (owner == null)
        {
            gateReservations.Remove(gate.StableId);
            return true;
        }

        return owner == requestingAircraft;
    }

    private void ReserveGate(
        AircraftFlightIdentity aircraft,
        AircraftGate gate)
    {
        if (aircraft.HasActualGate &&
            aircraft.ActualGateStableId != gate.StableId)
        {
            if (gateReservations.TryGetValue(
                    aircraft.ActualGateStableId,
                    out AircraftFlightIdentity previousOwner) &&
                previousOwner == aircraft)
            {
                gateReservations.Remove(aircraft.ActualGateStableId);
            }
        }

        gateReservations[gate.StableId] = aircraft;
        aircraft.SetActualGate(gate.StableId);
    }

    private void RemoveDestroyedAircraftReservations()
    {
        List<string> destroyedReservations = new List<string>();

        foreach (KeyValuePair<string, AircraftFlightIdentity> reservation
                 in gateReservations)
        {
            if (reservation.Value == null)
                destroyedReservations.Add(reservation.Key);
        }

        foreach (string gateId in destroyedReservations)
            gateReservations.Remove(gateId);
    }

    private float HorizontalDistanceSquared(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;

        return dx * dx + dz * dz;
    }


    [ContextMenu("Debug Gate Availability")]
    private void DebugGateAvailability()
    {
        if (gateRegistry == null)
            gateRegistry = FindFirstObjectByType<AircraftGateRegistry>();

        if (gateRegistry == null)
        {
            Debug.LogError(
                "GateAssignmentController: AircraftGateRegistry is missing.",
                this
            );
            return;
        }

        gateRegistry.RefreshGates();
        RemoveDestroyedAircraftReservations();

        int availableCount = 0;
        int reservedCount = 0;

        Debug.Log(
            $"========== GATE AVAILABILITY ({gateRegistry.Gates.Count} gates) ==========",
            this
        );

        foreach (AircraftGate gate in gateRegistry.Gates)
        {
            if (gate == null)
                continue;

            bool isReserved = gateReservations.TryGetValue(
                gate.StableId,
                out AircraftFlightIdentity owner
            ) && owner != null;

            if (isReserved)
            {
                reservedCount++;

                Debug.Log(
                    $"RESERVED | {gate.DisplayName} | " +
                    $"Size: {gate.Size} | " +
                    $"ID: {gate.StableId} | " +
                    $"Owner: {owner.FlightNumber}",
                    gate
                );
            }
            else
            {
                availableCount++;

                Debug.Log(
                    $"AVAILABLE | {gate.DisplayName} | " +
                    $"Size: {gate.Size} | " +
                    $"ID: {gate.StableId}",
                    gate
                );
            }
        }

        Debug.Log(
            $"GATE AVAILABILITY SUMMARY | " +
            $"Available: {availableCount} | " +
            $"Reserved: {reservedCount} | " +
            $"Total: {availableCount + reservedCount}",
            this
        );
    }


    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}