using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Makes a hobby aircraft decide whether to refuel before beginning its
/// normal departure taxi route.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
public class AircraftHobbyFuelDepartureController : MonoBehaviour
{
    private enum FuelDepartureState
    {
        WaitingForVisit,
        Deciding,
        TaxiingToFuelStation,
        AtFuelStation,
        WaitingForFuelStation,
        Finished
    }

    [Header("Fuel Decision")]
    [Range(0f, 1f)]
    [SerializeField] private float mandatoryRefuellingBelowFuelPercentage = 0.4f;

    [Tooltip("At $1/L the aircraft always refuels; at $1.50/L it has a 50% chance; at $2/L it never chooses to refuel.")]
    [SerializeField] private bool logFuelDecision = true;

    [Header("Taxi Movement")]
    [Min(1f)]
    [SerializeField] private float taxiSpeedKnots = 8f;

    [Min(1f)]
    [SerializeField] private float rotationSpeedDegreesPerSecond = 35f;

    [Min(0.05f)]
    [SerializeField] private float arrivalDistance = 0.2f;

    [Min(1)]
    [SerializeField] private int startSearchRadiusCells = 12;

    [Min(1)]
    [SerializeField] private int stopSearchRadiusCells = 6;

    [Range(1, 20)]
    [SerializeField] private int maximumStartCandidates = 10;

    [Range(1, 20)]
    [SerializeField] private int maximumStopCandidates = 10;

    [Header("References")]
    [SerializeField] private GridManager gridManager;
    [SerializeField] private TaxiwayNetwork taxiwayNetwork;
    [SerializeField] private TaxiwayPathfinder taxiwayPathfinder;

    [Header("Runtime")]
    [SerializeField] private FuelDepartureState state = FuelDepartureState.WaitingForVisit;
    [SerializeField] private AircraftFuelStation selectedStation;
    [SerializeField] private string debugStatus = "Waiting for hobby aircraft visit";

    private AircraftHobbyVisitController visitController;
    private AircraftHobbyDepartureTaxiController departureController;
    private AircraftFuelTank fuelTank;
    private AircraftStatus aircraftStatus;
    private readonly List<Vector2Int> plannedRoute = new();
    private readonly List<Vector3> movementPoints = new();
    private int movementIndex;
    private float decisionRetryTimer;
    private bool decisionMade;

    public bool IsAtFuelStation => state == FuelDepartureState.AtFuelStation;

    private void Awake()
    {
        visitController = GetComponent<AircraftHobbyVisitController>();
        departureController = GetComponent<AircraftHobbyDepartureTaxiController>();
        fuelTank = GetComponent<AircraftFuelTank>();
        aircraftStatus = GetComponent<AircraftStatus>();

        ResolveReferences();
    }

    private void Update()
    {
        if (visitController == null || !visitController.DepartureRequested)
            return;

        if (state == FuelDepartureState.WaitingForFuelStation)
        {
            decisionRetryTimer -= Time.deltaTime;
            if (decisionRetryTimer <= 0f)
            {
                decisionMade = false;
                state = FuelDepartureState.Deciding;
            }
            return;
        }

        if (!decisionMade)
        {
            decisionMade = true;
            state = FuelDepartureState.Deciding;
            DecideWhetherToRefuel();
        }

        if (state == FuelDepartureState.TaxiingToFuelStation)
            UpdateFuelStationTaxi();
        else if (state == FuelDepartureState.AtFuelStation)
            UpdateFuelStationStop();
    }

    private void ResolveReferences()
    {
        if (gridManager == null)
            gridManager = FindFirstObjectByType<GridManager>();

        if (taxiwayNetwork == null)
            taxiwayNetwork = FindFirstObjectByType<TaxiwayNetwork>();

        if (taxiwayPathfinder == null)
            taxiwayPathfinder = FindFirstObjectByType<TaxiwayPathfinder>();
    }

    private void DecideWhetherToRefuel()
    {
        if (fuelTank == null || fuelTank.IsFull)
        {
            FinishWithoutRefuelling("Fuel level does not require refuelling.");
            return;
        }

        float fuelPercentage = fuelTank.FuelPercentage;
        bool mandatory = fuelPercentage < mandatoryRefuellingBelowFuelPercentage;

        AircraftFuelStation[] stations =
            FindObjectsByType<AircraftFuelStation>(FindObjectsSortMode.None);

        if (stations.Length == 0)
        {
            if (mandatory)
                WaitForFuelStation("Fuel is below 40%; waiting for an aircraft fuel station.");
            else
                FinishWithoutRefuelling("No aircraft fuel station is available.");
            return;
        }

        AircraftFuelStation bestStation = null;
        List<Vector2Int> bestRoute = null;
        float bestDistance = float.MaxValue;

        foreach (AircraftFuelStation station in stations)
        {
            if (station == null ||
                station.CurrentFuelLitres <= 0f ||
                station.AircraftFuelStopPoint == null)
            {
                continue;
            }

            if (!TryFindRouteToStation(station, out List<Vector2Int> route,
                    out float distance))
            {
                continue;
            }

            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestStation = station;
            bestRoute = route;
        }

        if (bestStation == null || bestRoute == null)
        {
            if (mandatory)
                WaitForFuelStation("Fuel is below 40%; waiting for a stocked, reachable fuel station.");
            else
                FinishWithoutRefuelling("No stocked, reachable fuel station was found.");
            return;
        }

        float refuelChance = Mathf.Clamp01(2f - bestStation.SellingPricePerLitre);
        bool choosesRefuelling = mandatory || Random.value < refuelChance;

        if (logFuelDecision)
        {
            Debug.Log(
                $"{name}: fuel decision | Fuel: {fuelPercentage:P0} | " +
                $"Price: ${bestStation.SellingPricePerLitre:F2}/L | " +
                $"Mandatory: {mandatory} | Refuel: {choosesRefuelling}",
                this);
        }

        if (!choosesRefuelling)
        {
            FinishWithoutRefuelling("Aircraft declined refuelling based on price.");
            return;
        }

        selectedStation = bestStation;
        plannedRoute.Clear();
        plannedRoute.AddRange(bestRoute);
        BuildFuelStationMovementPoints(bestRoute, bestStation.AircraftFuelStopPoint);

        if (movementPoints.Count == 0)
        {
            FinishWithoutRefuelling("Could not build a route to the fuel stop.");
            return;
        }

        if (departureController != null)
            departureController.enabled = false;

        state = FuelDepartureState.TaxiingToFuelStation;
        movementIndex = 0;

        if (aircraftStatus != null)
            aircraftStatus.SetTaxiing(true);

        debugStatus = $"Taxiing to fuel station {selectedStation.name}";
    }

    private bool TryFindRouteToStation(
        AircraftFuelStation station,
        out List<Vector2Int> bestRoute,
        out float bestDistance)
    {
        bestRoute = null;
        bestDistance = float.MaxValue;

        ResolveReferences();

        if (gridManager == null || taxiwayNetwork == null ||
            taxiwayPathfinder == null || gridManager.CellSize <= 0f)
        {
            return false;
        }

        Vector2Int aircraftCell = gridManager.WorldToGrid(transform.position);
        Vector2Int stopCell = gridManager.WorldToGrid(
            station.AircraftFuelStopPoint.position);

        List<Vector2Int> starts = new();
        for (int x = -startSearchRadiusCells; x <= startSearchRadiusCells; x++)
        {
            for (int z = -startSearchRadiusCells; z <= startSearchRadiusCells; z++)
            {
                Vector2Int cell = aircraftCell + new Vector2Int(x, z);
                if (gridManager.IsInsideGrid(cell) && taxiwayNetwork.IsTaxiway(cell))
                    starts.Add(cell);
            }
        }

        starts.Sort((a, b) =>
            Vector2Int.Distance(a, aircraftCell).CompareTo(
                Vector2Int.Distance(b, aircraftCell)));

        if (starts.Count > maximumStartCandidates)
            starts.RemoveRange(maximumStartCandidates,
                starts.Count - maximumStartCandidates);

        List<Vector2Int> targets = new();
        for (int x = -stopSearchRadiusCells; x <= stopSearchRadiusCells; x++)
        {
            for (int z = -stopSearchRadiusCells; z <= stopSearchRadiusCells; z++)
            {
                Vector2Int cell = stopCell + new Vector2Int(x, z);
                if (gridManager.IsInsideGrid(cell) &&
                    taxiwayNetwork.IsTaxiway(cell) &&
                    taxiwayNetwork.IsCenterlineCell(cell))
                {
                    targets.Add(cell);
                }
            }
        }

        targets.Sort((a, b) =>
            Vector2Int.Distance(a, stopCell).CompareTo(
                Vector2Int.Distance(b, stopCell)));

        if (targets.Count > maximumStopCandidates)
            targets.RemoveRange(maximumStopCandidates,
                targets.Count - maximumStopCandidates);

        foreach (Vector2Int start in starts)
        {
            foreach (Vector2Int target in targets)
            {
                List<Vector2Int> route = taxiwayPathfinder.FindPath(start, target);
                if (route == null || route.Count == 0)
                    continue;

                float distance = Vector3.Distance(
                    transform.position,
                    gridManager.GridToWorldCenter(route[0]));

                for (int i = 1; i < route.Count; i++)
                {
                    distance += Vector3.Distance(
                        gridManager.GridToWorldCenter(route[i - 1]),
                        gridManager.GridToWorldCenter(route[i]));
                }

                distance += Vector3.Distance(
                    gridManager.GridToWorldCenter(route[route.Count - 1]),
                    station.AircraftFuelStopPoint.position);

                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                bestRoute = route;
            }
        }

        return bestRoute != null;
    }

    private void BuildFuelStationMovementPoints(
        List<Vector2Int> route,
        Transform stopPoint)
    {
        movementPoints.Clear();

        foreach (Vector2Int cell in route)
        {
            Vector3 point = gridManager.GridToWorldCenter(cell);
            point.y = transform.position.y;

            if (movementPoints.Count == 0 ||
                Vector3.Distance(movementPoints[movementPoints.Count - 1], point) > 0.05f)
            {
                movementPoints.Add(point);
            }
        }

        Vector3 stopPosition = stopPoint.position;
        stopPosition.y = transform.position.y;

        if (movementPoints.Count == 0 ||
            Vector3.Distance(movementPoints[movementPoints.Count - 1], stopPosition) > 0.05f)
        {
            movementPoints.Add(stopPosition);
        }
    }

    private void UpdateFuelStationTaxi()
    {
        if (selectedStation == null ||
            selectedStation.AircraftFuelStopPoint == null ||
            movementIndex >= movementPoints.Count)
        {
            AbortFuelTaxi("Fuel station or route became unavailable.");
            return;
        }

        AircraftTaxiTrafficFollowing trafficFollowing =
            GetComponent<AircraftTaxiTrafficFollowing>();

        float speedKnots = taxiSpeedKnots;

        if (trafficFollowing != null)
        {
            trafficFollowing.SetPlannedTaxiRoute(plannedRoute);
            speedKnots = trafficFollowing.GetTrafficFollowingSpeed(taxiSpeedKnots);
        }
        else if (aircraftStatus != null)
        {
            aircraftStatus.SetCurrentSpeedKnots(speedKnots);
        }

        Vector3 target = movementPoints[movementIndex];
        target.y = transform.position.y;

        Vector3 direction = target - transform.position;
        direction.y = 0f;

        if (direction.magnitude <= arrivalDistance)
        {
            movementIndex++;
            if (movementIndex >= movementPoints.Count)
                ArriveAtFuelStation();
            return;
        }

        if (direction.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRotation =
                Quaternion.LookRotation(direction.normalized, Vector3.up);

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                rotationSpeedDegreesPerSecond * Time.deltaTime);
        }

        float speed = AircraftStatus.KnotsToWorldSpeed(speedKnots);
        if (speed > 0.001f)
        {
            transform.position = Vector3.MoveTowards(
                transform.position,
                target,
                speed * Time.deltaTime);
        }
    }

    private void ArriveAtFuelStation()
    {
        Transform stopPoint = selectedStation.AircraftFuelStopPoint;
        transform.position = new Vector3(
            stopPoint.position.x,
            transform.position.y,
            stopPoint.position.z);

        Vector3 forward = stopPoint.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);

        if (aircraftStatus != null)
            aircraftStatus.SetParked(true);

        state = FuelDepartureState.AtFuelStation;
        debugStatus = $"Refuelling at {selectedStation.name}";
    }

    private void UpdateFuelStationStop()
    {
        if (selectedStation == null || fuelTank == null)
        {
            FinishFueling();
            return;
        }

        if (fuelTank.IsFull)
        {
            FinishFueling();
            return;
        }

        if (selectedStation.CurrentFuelLitres <= 0f)
        {
            debugStatus = "Waiting for fuel delivery at fuel station";
            return;
        }

        debugStatus =
            $"Refuelling: {fuelTank.CurrentFuelLitres:F1}/" +
            $"{fuelTank.MaximumFuelLitres:F1} L";
    }

    private void FinishFueling()
    {
        state = FuelDepartureState.Finished;
        debugStatus = "Refuelling complete; continuing to runway";

        if (aircraftStatus != null)
            aircraftStatus.SetParked(false);

        if (departureController != null)
            departureController.enabled = true;
    }

    private void WaitForFuelStation(string reason)
    {
        state = FuelDepartureState.WaitingForFuelStation;
        decisionRetryTimer = 5f;
        debugStatus = reason;

        if (logFuelDecision)
            Debug.Log($"{name}: {reason}", this);
    }

    private void FinishWithoutRefuelling(string reason)
    {
        state = FuelDepartureState.Finished;
        debugStatus = reason;

        if (logFuelDecision)
            Debug.Log($"{name}: {reason}", this);
    }

    private void AbortFuelTaxi(string reason)
    {
        debugStatus = reason;
        FinishFueling();
    }
}
