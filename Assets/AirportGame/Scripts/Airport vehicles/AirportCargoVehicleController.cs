using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(AirportGroundVehicleMovement))]
public class AirportCargoVehicleController : MonoBehaviour
{
    [Header("Movement Component")]
    [SerializeField]
    private AirportGroundVehicleMovement groundVehicleMovement;

    [Header("Cargo Tug")]
    [SerializeField]
    private GuidedCargoTrailerFollower trailerFollower;

    [Header("Home Return")]
    [Tooltip("The live CargoTugLot transform that spawned this vehicle.")]
    [SerializeField] private Transform homeSpawnPoint;

    [Tooltip("The tug's original spawn offset relative to the lot.")]
    [SerializeField] private Vector3 homeSpawnOffset;

    [Tooltip("World-space height used for the tug's home position.")]
    [SerializeField] private float homeSpawnHeight = 0.23f;

    [SerializeField] private bool isReturningHome;

    [Header("Cargo Zone Navigation Test")]
    [SerializeField] private CargoZone cargoZoneTestTarget;
    [SerializeField] private bool testCargoZoneOnPlay = true;

    [Header("Cargo Collection Test")]
    [SerializeField] private bool testCollectionOnArrival = true;
    [SerializeField] private string testFlightOperationId = "0:AB109";

    [Header("Cargo Capacity")]
    [Min(1)]
    [SerializeField] private int maximumCargoCapacity = 10;

    [Header("Runtime Status")]
    [SerializeField] private int carriedPackageCount;

    private readonly List<CargoPackage> carriedPackages =
        new List<CargoPackage>();

    private bool testCollectionAttempted;
    private Vector3 lastHomeTarget;

    public bool IsMoving =>
        groundVehicleMovement != null &&
        groundVehicleMovement.IsMoving;

    public string MovementStatus =>
        groundVehicleMovement != null
            ? groundVehicleMovement.MovementStatus
            : "Failed: AirportGroundVehicleMovement is missing.";

    public bool IsReturningHome => isReturningHome;

    public int CarriedPackageCount => carriedPackages.Count;

    public int MaximumCargoCapacity => maximumCargoCapacity;

    public int AvailableCargoCapacity =>
        Mathf.Max(0, maximumCargoCapacity - carriedPackages.Count);

    public IReadOnlyList<CargoPackage> CarriedPackages =>
        carriedPackages;

    private void Awake()
    {
        if (groundVehicleMovement == null)
        {
            groundVehicleMovement =
                GetComponent<AirportGroundVehicleMovement>();
        }

        if (groundVehicleMovement == null)
        {
            Debug.LogError(
                $"{name}: AirportGroundVehicleMovement is missing. " +
                "Add it to the cargo tug prefab.",
                this);
        }
        else if (homeSpawnPoint != null)
        {
            groundVehicleMovement.SetHomeArea(homeSpawnPoint);
        }

        if (trailerFollower == null)
            trailerFollower = GetComponent<GuidedCargoTrailerFollower>();

        maximumCargoCapacity = Mathf.Max(1, maximumCargoCapacity);

        RefreshCarriedPackageCount();
    }

    private void Start()
    {
        if (!testCargoZoneOnPlay)
            return;

        if (cargoZoneTestTarget == null)
        {
            Debug.LogWarning(
                $"{name}: Assign a Cargo Zone Test Target in the Inspector.",
                this);
            return;
        }

        MoveToCargoZone(cargoZoneTestTarget);
    }

    private void Update()
    {
        UpdateHomeReturnTarget();

        if (!IsMoving &&
            testCollectionOnArrival &&
            !testCollectionAttempted &&
            MovementStatus == "Arrived")
        {
            testCollectionAttempted = true;

            int collected = CollectOutgoingPackages(
                cargoZoneTestTarget,
                testFlightOperationId);

            Debug.Log(
                $"{name}: Collection test finished. " +
                $"Collected: {collected}, " +
                $"carried: {CarriedPackageCount}/{MaximumCargoCapacity}, " +
                $"remaining capacity: {AvailableCargoCapacity}.",
                this);
        }
    }

    // ---------------------------------------------------------------------
    // Home return
    // ---------------------------------------------------------------------

    /// <summary>
    /// Called by CargoTugLotSpawner to store the live home transform.
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
        {
            groundVehicleMovement =
                GetComponent<AirportGroundVehicleMovement>();
        }

        if (groundVehicleMovement != null)
            groundVehicleMovement.SetHomeArea(homeSpawnPoint);

        Debug.Log(
            $"{name}: Home spawn point set to " +
            $"{(spawnPoint != null ? spawnPoint.name : "None")}.",
            this);
    }

    /// <summary>
    /// Starts a return journey if the vehicle has no active assignment
    /// or baggage job and is not carrying packages.
    /// </summary>
    public bool BeginReturnHomeIfIdle()
    {
        if (homeSpawnPoint == null)
        {
            Debug.LogWarning(
                $"{name}: Cannot return home because no home spawn point " +
                "has been assigned.",
                this);
            return false;
        }

        AirportCargoVehicleDispatcherRegistration registration =
            GetComponent<AirportCargoVehicleDispatcherRegistration>();

        if (registration != null && registration.HasActiveAssignment)
            return false;

        AirportCargoIncomingBaggageJob incomingJob =
            GetComponent<AirportCargoIncomingBaggageJob>();

        AirportCargoOutgoingBaggageJob outgoingJob =
            GetComponent<AirportCargoOutgoingBaggageJob>();

        if ((incomingJob != null && incomingJob.IsBusy) ||
            (outgoingJob != null && outgoingJob.IsBusy))
        {
            return false;
        }

        if (IsMoving || carriedPackages.Count > 0)
            return false;

        if (groundVehicleMovement == null)
            return false;

        Vector3 target = GetCurrentHomeTarget();
        lastHomeTarget = target;

        isReturningHome = true;

        bool routeStarted = groundVehicleMovement.MoveTo(target);

        if (!routeStarted)
        {
            isReturningHome = false;
            return false;
        }

        Debug.Log(
            $"{name}: Returning to cargo tug lot '{homeSpawnPoint.name}' " +
            $"at {target}.",
            this);

        return true;
    }

    [ContextMenu("Recalculate Last Route")]
    public void RecalculateLastRoute()
    {
        if (groundVehicleMovement == null)
            return;

        if (IsMoving)
        {
            Debug.LogWarning(
                $"{name}: Cannot recalculate while the vehicle is moving. " +
                "Stop the vehicle first.",
                this);
            return;
        }

        bool routeStarted =
            groundVehicleMovement.RecalculateLastRoute();

        if (!routeStarted && isReturningHome)
            isReturningHome = false;
    }

    public void CancelReturnHome()
    {
        if (!isReturningHome)
            return;

        isReturningHome = false;

        Debug.Log(
            $"{name}: Return-home journey cancelled for a new assignment.",
            this);
    }

    private Vector3 GetCurrentHomeTarget()
    {
        Vector3 target =
            homeSpawnPoint.position +
            homeSpawnPoint.rotation * homeSpawnOffset;

        target.y = homeSpawnHeight;
        return target;
    }

    private void UpdateHomeReturnTarget()
    {
        if (!isReturningHome)
            return;

        if (homeSpawnPoint == null)
        {
            isReturningHome = false;
            return;
        }

        AirportCargoVehicleDispatcherRegistration registration =
            GetComponent<AirportCargoVehicleDispatcherRegistration>();

        if (registration != null && registration.HasActiveAssignment)
        {
            CancelReturnHome();
            return;
        }

        if (!IsMoving)
        {
            if (MovementStatus == "Arrived")
            {
                isReturningHome = false;

                Debug.Log(
                    $"{name}: Returned to the cargo tug lot.",
                    this);
            }

            return;
        }

        Vector3 currentTarget = GetCurrentHomeTarget();

        if (Vector3.Distance(currentTarget, lastHomeTarget) > 0.1f)
        {
            lastHomeTarget = currentTarget;

            if (groundVehicleMovement == null)
            {
                isReturningHome = false;
                return;
            }

            bool routeStarted =
                groundVehicleMovement.MoveTo(currentTarget);

            if (!routeStarted)
            {
                isReturningHome = false;

                Debug.LogWarning(
                    $"{name}: Could not recalculate the return route " +
                    "after the cargo tug lot moved.",
                    this);
            }
        }
    }

    // ---------------------------------------------------------------------
    // Movement interface
    // ---------------------------------------------------------------------

    /// <summary>
    /// Normal movement with an optional final facing transform.
    /// Kept for compatibility with existing cargo-tug callers.
    /// </summary>
    public bool MoveTo(
        Vector3 worldDestination,
        Transform facingTransform = null)
    {
        if (groundVehicleMovement == null)
            return ReportMissingMovementComponent();

        isReturningHome = false;

        return groundVehicleMovement.MoveTo(
            worldDestination,
            facingTransform);
    }

    /// <summary>
    /// Movement to a specific gate. The movement component permits
    /// only that gate's obstacle footprint for this route.
    /// </summary>
    public bool MoveTo(
        Vector3 worldDestination,
        GameObject destinationGate)
    {
        if (groundVehicleMovement == null)
            return ReportMissingMovementComponent();

        isReturningHome = false;

        return groundVehicleMovement.MoveTo(
            worldDestination,
            destinationGate);
    }

    /// <summary>
    /// Movement with both a final facing transform and a permitted gate.
    /// </summary>
    public bool MoveTo(
        Vector3 worldDestination,
        Transform facingTransform,
        GameObject destinationGate)
    {
        if (groundVehicleMovement == null)
            return ReportMissingMovementComponent();

        isReturningHome = false;

        return groundVehicleMovement.MoveTo(
            worldDestination,
            facingTransform,
            destinationGate);
    }

    public void StopMovement()
    {
        if (groundVehicleMovement != null)
            groundVehicleMovement.StopMovement();

        isReturningHome = false;
    }

    private bool ReportMissingMovementComponent()
    {
        Debug.LogError(
            $"{name}: Cannot move because AirportGroundVehicleMovement " +
            "is missing.",
            this);

        return false;
    }

    // ---------------------------------------------------------------------
    // Cargo zone navigation
    // ---------------------------------------------------------------------

    public bool MoveToCargoZone(CargoZone zone)
    {
        if (zone == null)
        {
            Debug.LogWarning(
                $"{name}: Cannot move to cargo zone: the zone reference is missing.",
                this);
            return false;
        }

        if (zone.AirportVehicleStopPoint == null)
        {
            Debug.LogWarning(
                $"Cargo zone '{zone.name}' has no AirportVehicleStopPoint.",
                this);
            return false;
        }

        Debug.Log(
            $"{name}: Navigating to cargo zone '{zone.name}'.",
            this);

        return MoveTo(zone.AirportVehicleStopPoint.position);
    }

    // ---------------------------------------------------------------------
    // Cargo collection
    // ---------------------------------------------------------------------

    public int CollectOutgoingPackages(
        CargoZone zone,
        string flightOperationId)
    {
        if (zone == null)
        {
            Debug.LogWarning(
                $"{name}: Cannot collect cargo because the zone is missing.",
                this);
            return 0;
        }

        if (string.IsNullOrWhiteSpace(flightOperationId))
        {
            Debug.LogWarning(
                $"{name}: Cannot collect cargo without a flight operation ID.",
                this);
            return 0;
        }

        int freeCapacity = AvailableCargoCapacity;

        if (freeCapacity <= 0)
        {
            Debug.Log(
                $"{name}: Cargo capacity is full " +
                $"({CarriedPackageCount}/{MaximumCargoCapacity}).",
                this);
            return 0;
        }

        List<CargoPackage> availablePackages =
            zone.GetAvailablePackages(
                CargoDirection.Outgoing,
                flightOperationId);

        int collected = 0;

        foreach (CargoPackage package in availablePackages)
        {
            if (collected >= freeCapacity)
                break;

            if (package == null || !package.TryReserve())
                continue;

            zone.UnregisterPackage(package);
            package.SetInTransit();
            HidePackageVisual(package);
            carriedPackages.Add(package);
            collected++;
        }

        RefreshCarriedPackageCount();

        Debug.Log(
            $"{name}: Collected {collected} package(s) for flight " +
            $"{flightOperationId} from zone '{zone.name}'. " +
            $"Vehicle load: {CarriedPackageCount}/{MaximumCargoCapacity}.",
            this);

        return collected;
    }

    private void HidePackageVisual(CargoPackage package)
    {
        Renderer[] renderers =
            package.GetComponentsInChildren<Renderer>(true);

        foreach (Renderer packageRenderer in renderers)
            packageRenderer.enabled = false;

        Collider[] colliders =
            package.GetComponentsInChildren<Collider>(true);

        foreach (Collider packageCollider in colliders)
            packageCollider.enabled = false;
    }

    private void RefreshCarriedPackageCount()
    {
        carriedPackages.RemoveAll(package => package == null);
        carriedPackageCount = carriedPackages.Count;
    }

    // ---------------------------------------------------------------------
    // Cargo delivery and package resolution
    // ---------------------------------------------------------------------

    public bool ResolveCarriedPackage(
        CargoPackage package,
        bool delivered,
        CargoZone returnZone = null)
    {
        if (package == null || !carriedPackages.Contains(package))
            return false;

        if (delivered)
        {
            package.SetDelivered();
            carriedPackages.Remove(package);
            Destroy(package.gameObject);
        }
        else
        {
            if (returnZone == null)
                return false;

            package.ReturnToZone(returnZone);

            if (!returnZone.RegisterPackage(package))
                return false;

            Renderer[] renderers =
                package.GetComponentsInChildren<Renderer>(true);

            foreach (Renderer packageRenderer in renderers)
                packageRenderer.enabled = true;

            Collider[] colliders =
                package.GetComponentsInChildren<Collider>(true);

            foreach (Collider packageCollider in colliders)
                packageCollider.enabled = true;

            carriedPackages.Remove(package);
        }

        RefreshCarriedPackageCount();
        return true;
    }
}