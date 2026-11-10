using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dispatches one airport fuel truck to parked aircraft, transfers aircraft
/// fuel, refills its own tank at the airport refill point, and returns home.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AirportGroundVehicleMovement))]
public class AirportFuelTruckController : MonoBehaviour
{
    private enum TruckState
    {
        Idle,
        DrivingToAircraft,
        RefuellingAircraft,
        WaitingForRefillPoint,
        DrivingToRefillStation,
        RefillingTruck,
        DrivingHome
    }

    private sealed class RefuellingJob
    {
        public AircraftTurnaroundController Aircraft;
        public AircraftFuelTank FuelTank;
        public AircraftGate Gate;
        public Transform StopPoint;
        public float FuelLitresDelivered;
        public bool PaymentProcessed;
    }

    private static readonly List<AirportFuelTruckController> activeTrucks =
        new List<AirportFuelTruckController>();

    private static readonly HashSet<AircraftTurnaroundController> globallyRequestedAircraft =
        new HashSet<AircraftTurnaroundController>();

    [Header("Movement")]
    [SerializeField] private AirportGroundVehicleMovement groundVehicleMovement;

    [Header("Truck Fuel")]
    [Min(1f)]
    [SerializeField] private float truckFuelCapacityLitres = 800f;
    [Min(0f)]
    [SerializeField] private float currentTruckFuelLitres = 800f;
    [Tooltip("Fuel transferred to an aircraft per in-game minute.")]
    [Min(0.1f)]
    [SerializeField] private float aircraftRefuellingRateLitresPerMinute = 100f;
    [Tooltip("Fuel added to this truck per in-game minute at the refill station.")]
    [Min(0.1f)]
    [SerializeField] private float truckRefillRateLitresPerMinute = 300f;

    [Header("Airport Refill Station")]
    [Tooltip("Name of the child transform marking where the truck parks to refill.")]
    [SerializeField] private string refillStopPointName = "FuelTruckRefillStopPoint";

    [Header("Home Return")]
    [SerializeField] private Transform homeSpawnPoint;
    [SerializeField] private Vector3 homeSpawnOffset;
    [SerializeField] private float homeSpawnHeight = 0.23f;

    [Header("Time")]
    [SerializeField] private GameTimeManager gameTimeManager;

    [Header("Debug")]
    [SerializeField] private bool showDebugMessages = true;

    [Header("Runtime Status")]
    [SerializeField] private string currentJobStatus = "Idle";
    [SerializeField] private int queuedJobCount;

    private readonly Queue<RefuellingJob> requestQueue =
        new Queue<RefuellingJob>();


    private TruckState state = TruckState.Idle;
    private RefuellingJob activeJob;
    private bool resumeAircraftAfterTruckRefill;
    private Transform currentRefillStopPoint;
    private FuelConnector refillFuelConnector;
    private PipelineNetworkManager pipelineNetworkManager;
    private bool warnedMissingRefillStopPoint;
    private bool warnedMissingRefillNetwork;
    private bool warnedEmptyRefillNetwork;

    public static int ActiveTruckCount => activeTrucks.Count;

    public static int CurrentlyRefuellingAircraftCount
    {
        get
        {
            int count = 0;
            foreach (AirportFuelTruckController truck in activeTrucks)
            {
                if (truck != null &&
                    truck.state == TruckState.RefuellingAircraft &&
                    truck.activeJob != null &&
                    truck.activeJob.Aircraft != null)
                    count++;
            }
            return count;
        }
    }

    public static bool IsAircraftBeingRefuelled(
        AircraftTurnaroundController aircraft)
    {
        if (aircraft == null)
            return false;

        foreach (AirportFuelTruckController truck in activeTrucks)
        {
            if (truck != null &&
                truck.state == TruckState.RefuellingAircraft &&
                truck.activeJob != null &&
                truck.activeJob.Aircraft == aircraft)
                return true;
        }

        return false;
    }

    public float TruckFuelCapacityLitres => truckFuelCapacityLitres;
    public float CurrentTruckFuelLitres => currentTruckFuelLitres;
    public int QueuedJobCount => requestQueue.Count;
    public bool IsBusy => state != TruckState.Idle;

    private void Awake()
    {
        if (!activeTrucks.Contains(this))
            activeTrucks.Add(this);


        if (groundVehicleMovement == null)
            groundVehicleMovement = GetComponent<AirportGroundVehicleMovement>();

        if (gameTimeManager == null)
            gameTimeManager = FindFirstObjectByType<GameTimeManager>();

        truckFuelCapacityLitres = Mathf.Max(1f, truckFuelCapacityLitres);
        currentTruckFuelLitres = Mathf.Clamp(
            currentTruckFuelLitres,
            0f,
            truckFuelCapacityLitres);
    }

    private void Update()
    {
        queuedJobCount = requestQueue.Count;

        if (groundVehicleMovement == null)
        {
            groundVehicleMovement =
                GetComponent<AirportGroundVehicleMovement>();

            if (groundVehicleMovement == null)
                return;
        }

        switch (state)
        {
            case TruckState.DrivingToAircraft:
                UpdateDrivingToAircraft();
                break;

            case TruckState.RefuellingAircraft:
                UpdateAircraftRefuelling();
                break;

            case TruckState.WaitingForRefillPoint:
                TryStartRefillStationJourney();
                break;

            case TruckState.DrivingToRefillStation:
                UpdateDrivingToRefillStation();
                break;

            case TruckState.RefillingTruck:
                UpdateTruckRefilling();
                break;

            case TruckState.DrivingHome:
                UpdateDrivingHome();
                break;

            case TruckState.Idle:
                StartNextQueuedJob();
                break;
        }
    }

    /// <summary>
    /// Called by AircraftTurnaroundController after passenger deboarding
    /// has completed and its normal fueling task has started.
    /// </summary>
    public static bool RequestRefuelling(AircraftTurnaroundController aircraft)
    {
        if (aircraft == null)
            return false;

        if (globallyRequestedAircraft.Contains(aircraft))
            return true;

        AirportFuelTruckController selectedTruck = null;
        int lowestWorkload = int.MaxValue;

        for (int i = activeTrucks.Count - 1; i >= 0; i--)
        {
            AirportFuelTruckController truck = activeTrucks[i];
            if (truck == null || !truck.isActiveAndEnabled)
            {
                activeTrucks.RemoveAt(i);
                continue;
            }

            int workload = truck.requestQueue.Count +
                (truck.activeJob != null || truck.state != TruckState.Idle ? 1 : 0);

            if (workload < lowestWorkload)
            {
                lowestWorkload = workload;
                selectedTruck = truck;
            }
        }

        return selectedTruck != null && selectedTruck.QueueRefuelling(aircraft);
    }

    private bool QueueRefuelling(AircraftTurnaroundController aircraft)
    {
        if (aircraft == null ||
            aircraft.DeboardingStatus != AircraftTurnaroundTaskStatus.Completed ||
            aircraft.FuelingStatus != AircraftTurnaroundTaskStatus.InProgress)
        {
            return false;
        }

        if (globallyRequestedAircraft.Contains(aircraft))
            return true;

        AircraftFuelTank fuelTank =
            aircraft.GetComponent<AircraftFuelTank>();

        AircraftFlightIdentity identity =
            aircraft.GetComponent<AircraftFlightIdentity>();

        if (fuelTank == null ||
            identity == null ||
            !identity.IsInitialized ||
            !identity.HasActualGate)
        {
            Debug.LogWarning(
                $"{aircraft.name}: Cannot request airport refuelling because " +
                "its fuel tank or actual gate assignment is missing.",
                aircraft);
            return false;
        }

        AircraftGateRegistry gateRegistry =
            FindFirstObjectByType<AircraftGateRegistry>();

        if (gateRegistry == null ||
            !gateRegistry.TryGetGate(
                identity.ActualGateStableId,
                out AircraftGate actualGate))
        {
            Debug.LogWarning(
                $"{aircraft.name}: Could not resolve actual gate '" +
                $"{identity.ActualGateStableId}' for fuel service.",
                aircraft);
            return false;
        }

        Transform stopPoint = FindGateStopPoint(actualGate.transform);

        if (stopPoint == null)
        {
            Debug.LogError(
                $"{aircraft.name}: Gate '{actualGate.name}' has no child named " +
                "'FuelTruckVehicleStopPoint'.",
                actualGate);
            return false;
        }

        aircraft.EnableExternalFueling();

        if (fuelTank.IsFull)
        {
            aircraft.CompleteExternalFueling();
            return true;
        }

        RefuellingJob job = new RefuellingJob
        {
            Aircraft = aircraft,
            FuelTank = fuelTank,
            Gate = actualGate,
            StopPoint = stopPoint
        };

        globallyRequestedAircraft.Add(aircraft);
        requestQueue.Enqueue(job);
        queuedJobCount = requestQueue.Count;

        if (showDebugMessages)
        {
            Debug.Log(
                $"{name}: Queued refuelling for aircraft '{aircraft.name}' " +
                $"at gate '{actualGate.StableId}'. Queue: {queuedJobCount}.",
                this);
        }

        if (state == TruckState.DrivingHome)
        {
            groundVehicleMovement.StopMovement();
            state = TruckState.Idle;
            currentJobStatus = "New job received; dispatching";
        }

        StartNextQueuedJob();
        return true;
    }

    private Transform FindGateStopPoint(Transform gateRoot)
    {
        Transform[] children =
            gateRoot.GetComponentsInChildren<Transform>(true);

        foreach (Transform child in children)
        {
            if (child != gateRoot &&
                child.name == "FuelTruckVehicleStopPoint")
            {
                return child;
            }
        }

        return null;
    }

    private void StartNextQueuedJob()
    {
        if (state != TruckState.Idle || activeJob != null)
            return;

        while (requestQueue.Count > 0)
        {
            RefuellingJob next = requestQueue.Dequeue();

            if (next == null ||
                next.Aircraft == null ||
                next.FuelTank == null ||
                next.Gate == null ||
                next.StopPoint == null)
            {
                if (next != null && next.Aircraft != null)
                {
                    globallyRequestedAircraft.Remove(next.Aircraft);
                    next.Aircraft.CancelExternalFueling();
                }

                continue;
            }

            activeJob = next;

            if (activeJob.FuelTank.IsFull)
            {
                CompleteActiveAircraftJob();

                if (state != TruckState.Idle)
                    return;

                continue;
            }

            bool routeStarted = groundVehicleMovement.MoveTo(
                activeJob.StopPoint.position,
                activeJob.StopPoint,
                activeJob.Gate.gameObject);

            if (!routeStarted)
            {
                FailActiveJob(
                    "The route to the aircraft's fuel truck stop point could not be started.");
                continue;
            }

            state = TruckState.DrivingToAircraft;
            currentJobStatus =
                $"Driving to aircraft {activeJob.Aircraft.name}";

            LogStatus(currentJobStatus);
            return;
        }

        activeJob = null;
        queuedJobCount = 0;
    }

    private void UpdateDrivingToAircraft()
    {
        if (activeJob == null || activeJob.Aircraft == null)
        {
            AbandonActiveJob();
            return;
        }

        if (groundVehicleMovement.IsMoving)
            return;

        if (groundVehicleMovement.MovementStatus.StartsWith("Failed:"))
        {
            FailActiveJob(groundVehicleMovement.MovementStatus);
            StartNextQueuedJob();
            return;
        }

        if (groundVehicleMovement.MovementStatus != "Arrived")
            return;

        state = TruckState.RefuellingAircraft;
        currentJobStatus = $"Refuelling {activeJob.Aircraft.name}";
        LogStatus(currentJobStatus);
    }

    private void UpdateAircraftRefuelling()
    {
        if (activeJob == null ||
            activeJob.Aircraft == null ||
            activeJob.FuelTank == null)
        {
            AbandonActiveJob();
            return;
        }

        if (activeJob.FuelTank.IsFull)
        {
            CompleteActiveAircraftJob();
            return;
        }

        if (currentTruckFuelLitres <= 0f)
        {
            BeginRefillStationJourney(true);
            return;
        }

        float deltaGameSeconds = GetDeltaGameSeconds();
        if (deltaGameSeconds <= 0f)
            return;

        float proposedAmount =
            aircraftRefuellingRateLitresPerMinute *
            deltaGameSeconds / 60f;

        float amountAvailable = Mathf.Min(
            proposedAmount,
            currentTruckFuelLitres);

        float accepted = activeJob.FuelTank.AddFuel(amountAvailable);
        activeJob.FuelLitresDelivered += accepted;
        currentTruckFuelLitres =
            Mathf.Max(0f, currentTruckFuelLitres - accepted);

        if (activeJob.FuelTank.IsFull)
        {
            CompleteActiveAircraftJob();
            return;
        }

        if (currentTruckFuelLitres <= 0f)
            BeginRefillStationJourney(true);
    }

    private void CompleteActiveAircraftJob()
    {
        if (activeJob != null && activeJob.Aircraft != null)
        {
            ChargeAircraftForCompletedFuel(activeJob);
            activeJob.Aircraft.CompleteExternalFueling();
            globallyRequestedAircraft.Remove(activeJob.Aircraft);

            LogStatus(
                $"Completed aircraft refuelling for {activeJob.Aircraft.name}");
        }

        activeJob = null;
        state = TruckState.Idle;

        if (requestQueue.Count > 0)
        {
            StartNextQueuedJob();
            return;
        }

        // After the last job, the truck always visits its refill station
        // before deciding whether to take a newly arrived job or go home.
        BeginRefillStationJourney(false);
    }

    private void ChargeAircraftForCompletedFuel(RefuellingJob job)
    {
        if (job == null || job.PaymentProcessed)
            return;

        job.PaymentProcessed = true;

        if (job.FuelLitresDelivered <= 0f)
            return;

        AircraftFuelStation fuelStation =
            FindFirstObjectByType<AircraftFuelStation>();

        if (fuelStation == null)
        {
            Debug.LogWarning(
                $"{name}: Could not charge {job.Aircraft?.name ?? "aircraft"} " +
                "for fuel because no AircraftFuelStation was found to read its price.",
                this);
            return;
        }

        AirportEconomyManager economyManager =
            FindFirstObjectByType<AirportEconomyManager>();

        if (economyManager == null)
        {
            Debug.LogWarning(
                $"{name}: Could not record the aircraft fuel payment because " +
                "AirportEconomyManager was not found.",
                this);
            return;
        }

        float pricePerLitre = fuelStation.SellingPricePerLitre;
        int totalCost = Mathf.CeilToInt(
            job.FuelLitresDelivered * pricePerLitre);

        if (totalCost <= 0)
            return;

        AircraftAirportFees airportFees =
            job.Aircraft != null
                ? job.Aircraft.GetComponent<AircraftAirportFees>()
                : null;

        bool paymentRecorded = airportFees != null &&
            airportFees.RecordCharge(
                totalCost,
                $"CargoPax aircraft fuel sale ({job.Aircraft?.name ?? "aircraft"})");

        if (paymentRecorded)
        {
            Debug.Log(
                $"AIRPORT INCOME | Aircraft fuel: " +
                $"{job.FuelLitresDelivered:N1} L x ${pricePerLitre:N2}/L " +
                $"= +${totalCost:N0} | Aircraft: " +
                $"{job.Aircraft?.name ?? "aircraft"}",
                this);
        }
    }

    private void BeginRefillStationJourney(bool resumeCurrentAircraft)
    {
        resumeAircraftAfterTruckRefill = resumeCurrentAircraft;
        currentJobStatus = "Driving to airport fuel station";
        state = TruckState.WaitingForRefillPoint;
        TryStartRefillStationJourney();
    }

    private void TryStartRefillStationJourney()
    {
        if (currentRefillStopPoint == null)
        {
            GameObject stopPointObject =
                GameObject.Find(refillStopPointName);

            if (stopPointObject != null)
            {
                currentRefillStopPoint = stopPointObject.transform;
                warnedMissingRefillStopPoint = false;
            }
        }

        if (currentRefillStopPoint == null)
        {
            if (!warnedMissingRefillStopPoint)
            {
                Debug.LogWarning(
                    $"{name}: Waiting for a scene transform named " +
                    $"'{refillStopPointName}' for the airport fuel station.",
                    this);
                warnedMissingRefillStopPoint = true;
            }

            return;
        }

        // Permit the refill station's own occupied/foundation cells so
        // the truck can reach its designated parking stop point.
        bool routeStarted = groundVehicleMovement.MoveTo(
            currentRefillStopPoint.position,
            currentRefillStopPoint,
            currentRefillStopPoint.root.gameObject);

        if (!routeStarted)
        {
            currentJobStatus =
                "Waiting: could not start route to airport fuel station";
            Debug.LogWarning(
                $"{name}: Could not start the route to '{refillStopPointName}'.",
                this);
            return;
        }

        state = TruckState.DrivingToRefillStation;
        currentJobStatus = "Driving to airport fuel station";
        LogStatus(currentJobStatus);
    }

    private void UpdateDrivingToRefillStation()
    {
        if (groundVehicleMovement.IsMoving)
            return;

        if (groundVehicleMovement.MovementStatus.StartsWith("Failed:"))
        {
            state = TruckState.WaitingForRefillPoint;
            currentJobStatus =
                "Waiting: route to airport fuel station failed";
            Debug.LogWarning(
                $"{name}: Route to airport fuel station failed: " +
                groundVehicleMovement.MovementStatus,
                this);
            return;
        }

        if (groundVehicleMovement.MovementStatus != "Arrived")
            return;

        state = TruckState.RefillingTruck;
        currentJobStatus = "Refilling truck tank";
        LogStatus(currentJobStatus);
    }

    private void UpdateTruckRefilling()
    {
        if (currentTruckFuelLitres >= truckFuelCapacityLitres)
        {
            FinishTruckRefilling();
            return;
        }

        if (currentRefillStopPoint == null)
        {
            state = TruckState.WaitingForRefillPoint;
            TryStartRefillStationJourney();
            return;
        }

        if (pipelineNetworkManager == null)
            pipelineNetworkManager =
                FindFirstObjectByType<PipelineNetworkManager>();

        if (refillFuelConnector == null)
        {
            refillFuelConnector =
                currentRefillStopPoint.root.GetComponentInChildren<FuelConnector>(true);
        }

        if (pipelineNetworkManager == null || refillFuelConnector == null)
        {
            if (!warnedMissingRefillNetwork)
            {
                Debug.LogWarning(
                    $"{name}: Cannot refill because the airport fuel station's " +
                    "pipeline manager or FuelConnector is missing.",
                    this);
                warnedMissingRefillNetwork = true;
            }

            currentJobStatus = "Waiting: airport fuel network unavailable";
            return;
        }

        PipelineNetworkManager.PipelineNetwork network =
            pipelineNetworkManager.GetNetworkFor(refillFuelConnector);

        if (network == null)
        {
            if (!warnedMissingRefillNetwork)
            {
                Debug.LogWarning(
                    $"{name}: The airport fuel station connector is not assigned " +
                    "to a pipeline network yet.",
                    this);
                warnedMissingRefillNetwork = true;
            }

            currentJobStatus = "Waiting: airport fuel network unavailable";
            return;
        }

        warnedMissingRefillNetwork = false;

        float deltaGameSeconds = GetDeltaGameSeconds();
        if (deltaGameSeconds <= 0f)
            return;

        float requestedAmount = Mathf.Min(
            truckFuelCapacityLitres - currentTruckFuelLitres,
            truckRefillRateLitresPerMinute * deltaGameSeconds / 60f);

        float consumedAmount = network.ConsumeFuel(requestedAmount);

        if (consumedAmount <= 0f)
        {
            if (!warnedEmptyRefillNetwork)
            {
                Debug.LogWarning(
                    $"{name}: Airport fuel network is empty. The truck is waiting " +
                    "until fuel becomes available.",
                    this);
                warnedEmptyRefillNetwork = true;
            }

            currentJobStatus = "Waiting: airport fuel network empty";
            return;
        }

        warnedEmptyRefillNetwork = false;
        currentTruckFuelLitres = Mathf.Min(
            truckFuelCapacityLitres,
            currentTruckFuelLitres + consumedAmount);

        if (currentTruckFuelLitres >= truckFuelCapacityLitres)
            FinishTruckRefilling();
    }

    private void FinishTruckRefilling()
    {
        currentTruckFuelLitres = truckFuelCapacityLitres;
        LogStatus("Truck tank refilled from airport pipeline network");

        if (resumeAircraftAfterTruckRefill &&
            activeJob != null &&
            activeJob.Aircraft != null &&
            activeJob.FuelTank != null)
        {
            resumeAircraftAfterTruckRefill = false;

            bool routeStarted = groundVehicleMovement.MoveTo(
                activeJob.StopPoint.position,
                activeJob.StopPoint,
                activeJob.Gate.gameObject);

            if (routeStarted)
            {
                state = TruckState.DrivingToAircraft;
                currentJobStatus =
                    $"Returning to aircraft {activeJob.Aircraft.name}";
                LogStatus(currentJobStatus);
                return;
            }

            FailActiveJob(
                "Could not resume the route to the aircraft after refilling the truck.");
            StartNextQueuedJob();
            return;
        }

        resumeAircraftAfterTruckRefill = false;

        if (requestQueue.Count > 0)
        {
            state = TruckState.Idle;
            StartNextQueuedJob();
        }
        else
        {
            BeginReturnHome();
        }
    }

    private void BeginReturnHome()
    {
        if (homeSpawnPoint == null)
        {
            state = TruckState.Idle;
            currentJobStatus =
                "Idle: home parking position has not been assigned";
            Debug.LogWarning(
                $"{name}: Cannot return to the fuel truck lot because " +
                "the home spawn point has not been assigned.",
                this);
            return;
        }

        Vector3 target =
            homeSpawnPoint.position +
            homeSpawnPoint.rotation * homeSpawnOffset;

        target.y = homeSpawnHeight;

        if (!groundVehicleMovement.MoveTo(target, homeSpawnPoint))
        {
            state = TruckState.Idle;
            currentJobStatus = "Idle: return-home route failed";
            Debug.LogWarning(
                $"{name}: Could not start the route back to the fuel truck lot.",
                this);
            return;
        }

        state = TruckState.DrivingHome;
        currentJobStatus = "Returning to fuel truck lot";
        LogStatus(currentJobStatus);
    }

    private void UpdateDrivingHome()
    {
        if (groundVehicleMovement.IsMoving)
            return;

        if (groundVehicleMovement.MovementStatus == "Arrived")
        {
            state = TruckState.Idle;
            currentJobStatus = "Parked at fuel truck lot";
            LogStatus(currentJobStatus);
            StartNextQueuedJob();
        }
        else if (groundVehicleMovement.MovementStatus.StartsWith("Failed:"))
        {
            state = TruckState.Idle;
            currentJobStatus =
                "Idle: return-home route failed";
            Debug.LogWarning(
                $"{name}: Return-home route failed: " +
                groundVehicleMovement.MovementStatus,
                this);
        }
    }

    private void FailActiveJob(string reason)
    {
        if (activeJob != null && activeJob.Aircraft != null)
        {
            activeJob.Aircraft.CancelExternalFueling();
            globallyRequestedAircraft.Remove(activeJob.Aircraft);
        }

        Debug.LogWarning(
            $"{name}: Fuel truck job failed. {reason}",
            this);

        activeJob = null;
        state = TruckState.Idle;
        currentJobStatus = "Job failed";
    }

    private void AbandonActiveJob()
    {
        if (activeJob != null && activeJob.Aircraft != null)
        {
            activeJob.Aircraft.CancelExternalFueling();
            globallyRequestedAircraft.Remove(activeJob.Aircraft);
        }

        activeJob = null;
        state = TruckState.Idle;
        currentJobStatus = "Idle";
        StartNextQueuedJob();
    }

    private float GetDeltaGameSeconds()
    {
        float speedMultiplier =
            gameTimeManager != null
                ? Mathf.Max(0f, gameTimeManager.CurrentSpeedMultiplier)
                : 1f;

        return speedMultiplier > 0f
            ? Time.unscaledDeltaTime * speedMultiplier
            : 0f;
    }

    private void LogStatus(string message)
    {
        if (showDebugMessages)
            Debug.Log($"{name}: {message}.", this);
    }

    /// <summary>
    /// Called by FuelTruckLotSpawner to store the live home transform.
    /// </summary>
    public void SetHomeSpawnPoint(
        Transform spawnPoint,
        Vector3 localOffset,
        float worldHeight)
    {
        homeSpawnPoint = spawnPoint;
        homeSpawnOffset = localOffset;
        homeSpawnHeight = worldHeight;

        if (groundVehicleMovement == null)
            groundVehicleMovement =
                GetComponent<AirportGroundVehicleMovement>();

        if (groundVehicleMovement != null && spawnPoint != null)
            groundVehicleMovement.SetHomeArea(spawnPoint);
    }

    private void OnDestroy()
    {
        activeTrucks.Remove(this);

        foreach (RefuellingJob job in requestQueue)
        {
            if (job != null && job.Aircraft != null)
            {
                globallyRequestedAircraft.Remove(job.Aircraft);
                job.Aircraft.CancelExternalFueling();
            }
        }

        if (activeJob != null && activeJob.Aircraft != null)
        {
            globallyRequestedAircraft.Remove(activeJob.Aircraft);
            activeJob.Aircraft.CancelExternalFueling();
        }
    }
}
