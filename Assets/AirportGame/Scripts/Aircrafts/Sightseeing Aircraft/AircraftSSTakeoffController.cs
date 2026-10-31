using UnityEngine;

/// <summary>
/// Handles takeoff for sightseeing aircraft only.
/// Pays the airport's sightseeing commission after successful climb.
/// </summary>
[DisallowMultipleComponent]
public class AircraftSSTakeoffController : MonoBehaviour
{
    public enum TakeoffState
    {
        WaitingForLineUp,
        TakeoffRoll,
        Rotating,
        Climbing,
        Completed
    }

    [Header("References")]
    [SerializeField] private AircraftSSDepartureTaxiController departureController;
    [SerializeField] private AircraftSSTurnaroundController turnaroundController;
    [SerializeField] private AircraftRunwayTrafficManager trafficManager;
    [SerializeField] private GameTimeManager gameTimeManager;
    [SerializeField] private GridManager gridManager;
    [SerializeField] private AirportEconomyManager economyManager;
    [SerializeField] private BoxCollider aircraftCollider;

    [Header("Takeoff Speed")]
    [Min(1f)]
    [SerializeField] private float takeoffSpeedKnots = 80f;

    [Min(0.1f)]
    [SerializeField] private float groundAccelerationKnotsPerSecond = 15f;

    [Min(0.1f)]
    [SerializeField] private float airborneAccelerationKnotsPerSecond = 3f;

    private float worldUnitsPerSecondPerKnot =>
        AircraftStatus.WorldUnitsPerSecondPerKnot;

    [Header("Rotation")]
    [Range(0.1f, 1f)]
    [SerializeField] private float rotationStartSpeedFraction = 0.8f;

    [Min(0.1f)]
    [SerializeField] private float rotationRateDegreesPerSecond = 3f;

    [Range(1f, 45f)]
    [SerializeField] private float targetNoseUpDegrees = 13f;

    [Range(1f, 24f)]
    [SerializeField] private float liftoffStartNoseUpDegrees = 2f;

    [Tooltip("Adjust if the aircraft model's nose is not local +Z.")]
    [SerializeField] private Vector3 modelRotationOffset;

    [Header("Climb")]
    [Min(1f)]
    [SerializeField] private float targetAltitudeAboveRunway = 60f;

    [Min(0f)]
    [SerializeField] private float runwayClearanceBuffer = 1f;

    [Header("Runtime Status")]
    [SerializeField]
    private TakeoffState currentState =
        TakeoffState.WaitingForLineUp;

    [SerializeField] private float currentSpeedKnots;
    [SerializeField] private float currentNoseUpDegrees;
    [SerializeField] private float currentAltitudeAboveRunway;
    [SerializeField] private int passengersOnBoard;
    [SerializeField] private int commissionPaid;
    [SerializeField] private bool revenuePaid;
    [SerializeField] private bool runwayReservationReleased;
    [SerializeField] private string debugStatus = "Waiting for line-up";

    private SightseeingFlightEconomy sightseeingEconomy;
    private AircraftRunway selectedRunway;
    private Vector3 takeoffDirection;
    private Vector3 runwayCenter;
    private float runwayLength;
    private float runwaySurfaceY;
    private float groundLevelY;
    private float rotationTriggerSpeedKnots;

    public TakeoffState CurrentState => currentState;

    public bool IsTakingOff =>
        currentState == TakeoffState.TakeoffRoll ||
        currentState == TakeoffState.Rotating ||
        currentState == TakeoffState.Climbing;

    public bool IsCompleted => currentState == TakeoffState.Completed;
    public int PassengersOnBoard => passengersOnBoard;
    public int CommissionPaid => commissionPaid;

    private void Awake()
    {
        ResolveReferences();
    }

    private void Update()
    {
        if (currentState == TakeoffState.Completed)
            return;

        ResolveReferences();

        if (currentState == TakeoffState.WaitingForLineUp)
        {
            if (departureController == null ||
                !departureController.IsLinedUp)
                return;

            if (!BeginTakeoff())
                return;
        }

        float deltaTime = GetSimulationDeltaTime();

        if (deltaTime <= 0f)
            return;

        switch (currentState)
        {
            case TakeoffState.TakeoffRoll:
                UpdateTakeoffRoll(deltaTime);
                break;

            case TakeoffState.Rotating:
                UpdateRotationAndLiftoff(deltaTime);
                break;

            case TakeoffState.Climbing:
                UpdateClimb(deltaTime);
                break;
        }

        AircraftStatus status = GetComponent<AircraftStatus>();

        if (status != null)
        {
            status.SetCurrentSpeedKnots(currentSpeedKnots);

            if (currentState == TakeoffState.Climbing)
                status.SetFlying(true);
        }
    }

    private void ResolveReferences()
    {
        if (departureController == null)
            departureController =
                GetComponent<AircraftSSDepartureTaxiController>();

        if (turnaroundController == null)
            turnaroundController =
                GetComponent<AircraftSSTurnaroundController>();

        if (trafficManager == null)
            trafficManager =
                FindFirstObjectByType<AircraftRunwayTrafficManager>();

        if (gameTimeManager == null)
            gameTimeManager = FindFirstObjectByType<GameTimeManager>();

        if (gridManager == null)
            gridManager = FindFirstObjectByType<GridManager>();

        if (economyManager == null)
            economyManager = FindFirstObjectByType<AirportEconomyManager>();

        if (aircraftCollider == null)
            aircraftCollider = GetComponentInChildren<BoxCollider>(true);

        if (sightseeingEconomy == null)
            sightseeingEconomy = SightseeingFlightEconomy.Instance;
    }

    private float GetSimulationDeltaTime()
    {
        if (gameTimeManager == null)
            return Time.unscaledDeltaTime;

        float speed = gameTimeManager.CurrentSpeedMultiplier;

        if (speed <= 0f)
            return 0f;

        return Time.unscaledDeltaTime * speed;
    }

    private bool BeginTakeoff()
    {
        if (departureController == null ||
            trafficManager == null ||
            gridManager == null ||
            turnaroundController == null ||
            !turnaroundController.IsBoardingComplete)
        {
            debugStatus =
                "Cannot take off: required references or boarding are missing.";

            Debug.LogError($"{name}: {debugStatus}", this);
            return false;
        }

        selectedRunway = departureController.SelectedRunway;

        if (selectedRunway == null ||
            !selectedRunway.TryGetGeometry(
                gridManager,
                out runwayCenter,
                out Vector3 runwayAxis,
                out runwayLength,
                out runwaySurfaceY))
        {
            debugStatus = "Cannot take off: runway geometry is unavailable.";
            Debug.LogError($"{name}: {debugStatus}", this);
            return false;
        }

        takeoffDirection = departureController.TakeoffDirection;
        takeoffDirection.y = 0f;

        if (takeoffDirection.sqrMagnitude < 0.0001f)
        {
            debugStatus = "Cannot take off: invalid takeoff direction.";
            Debug.LogError($"{name}: {debugStatus}", this);
            return false;
        }

        takeoffDirection.Normalize();

        // Use the passenger count allocated when boarding completed.
        passengersOnBoard = turnaroundController.PassengersOnBoard;

        groundLevelY = transform.position.y;
        currentSpeedKnots = 0f;
        currentNoseUpDegrees = 0f;

        rotationTriggerSpeedKnots =
            takeoffSpeedKnots * rotationStartSpeedFraction;

        currentAltitudeAboveRunway =
            Mathf.Max(0f, transform.position.y - runwaySurfaceY);

        runwayReservationReleased = false;

        ApplyTakeoffOrientation(0f);
        currentState = TakeoffState.TakeoffRoll;

        int ticketPrice = sightseeingEconomy != null
            ? sightseeingEconomy.TicketPrice
            : 0;

        debugStatus =
            $"Takeoff roll started with {passengersOnBoard} passengers.";

        Debug.Log(
            $"SIGHTSEEING TAKEOFF STARTED | Aircraft: {name} | " +
            $"Runway: {selectedRunway.name} | " +
            $"Passengers: {passengersOnBoard} | " +
            $"Ticket: ${ticketPrice} | " +
            $"Rotation at {rotationTriggerSpeedKnots:F1} knots",
            this);

        return true;
    }

    private void UpdateTakeoffRoll(float deltaTime)
    {
        float previousSpeed = currentSpeedKnots;

        currentSpeedKnots = Mathf.MoveTowards(
            currentSpeedKnots,
            rotationTriggerSpeedKnots,
            groundAccelerationKnotsPerSecond * deltaTime);

        float averageSpeed = (previousSpeed + currentSpeedKnots) * 0.5f;

        MoveAlongRunway(averageSpeed, deltaTime, false);
        ApplyTakeoffOrientation(0f);
        UpdateRunwayReservation();

        debugStatus = $"Takeoff roll: {currentSpeedKnots:F1} knots.";

        if (currentSpeedKnots >= rotationTriggerSpeedKnots - 0.001f)
        {
            currentSpeedKnots = rotationTriggerSpeedKnots;
            currentState = TakeoffState.Rotating;

            Debug.Log(
                $"SIGHTSEEING ROTATION STARTED | Aircraft: {name} | " +
                $"Speed: {currentSpeedKnots:F1} knots",
                this);
        }
    }

    private void UpdateRotationAndLiftoff(float deltaTime)
    {
        float previousSpeed = currentSpeedKnots;

        currentSpeedKnots = Mathf.MoveTowards(
            currentSpeedKnots,
            takeoffSpeedKnots,
            airborneAccelerationKnotsPerSecond * deltaTime);

        float averageSpeed = (previousSpeed + currentSpeedKnots) * 0.5f;

        currentNoseUpDegrees = Mathf.MoveTowards(
            currentNoseUpDegrees,
            targetNoseUpDegrees,
            rotationRateDegreesPerSecond * deltaTime);

        float horizontalDistance =
            KnotsToWorldSpeed(averageSpeed) * deltaTime;

        float verticalDistance = 0f;

        if (currentNoseUpDegrees > liftoffStartNoseUpDegrees)
        {
            float progress = Mathf.InverseLerp(
                liftoffStartNoseUpDegrees,
                targetNoseUpDegrees,
                currentNoseUpDegrees);

            progress = Mathf.SmoothStep(0f, 1f, progress);

            verticalDistance =
                horizontalDistance *
                Mathf.Tan(currentNoseUpDegrees * Mathf.Deg2Rad) *
                progress;
        }

        MoveAlongRunway(
            averageSpeed,
            deltaTime,
            true,
            verticalDistance);

        ApplyTakeoffOrientation(currentNoseUpDegrees);
        UpdateAltitude();
        UpdateRunwayReservation();

        if (currentNoseUpDegrees >= targetNoseUpDegrees - 0.001f)
        {
            currentNoseUpDegrees = targetNoseUpDegrees;
            ApplyTakeoffOrientation(currentNoseUpDegrees);
            currentState = TakeoffState.Climbing;

            Debug.Log(
                $"SIGHTSEEING CLIMB STARTED | Aircraft: {name}",
                this);
        }

        CheckTargetAltitude();
    }

    private void UpdateClimb(float deltaTime)
    {
        float previousSpeed = currentSpeedKnots;

        currentSpeedKnots = Mathf.MoveTowards(
            currentSpeedKnots,
            takeoffSpeedKnots,
            airborneAccelerationKnotsPerSecond * deltaTime);

        float averageSpeed = (previousSpeed + currentSpeedKnots) * 0.5f;

        float horizontalDistance =
            KnotsToWorldSpeed(averageSpeed) * deltaTime;

        float verticalDistance =
            horizontalDistance *
            Mathf.Tan(targetNoseUpDegrees * Mathf.Deg2Rad);

        float targetY = runwaySurfaceY + targetAltitudeAboveRunway;
        float remainingAltitude = targetY - transform.position.y;

        if (verticalDistance >= remainingAltitude)
        {
            float slope = Mathf.Max(
                0.001f,
                Mathf.Tan(targetNoseUpDegrees * Mathf.Deg2Rad));

            horizontalDistance =
                Mathf.Max(0f, remainingAltitude / slope);

            verticalDistance = Mathf.Max(0f, remainingAltitude);
        }

        MoveAlongRunway(
            averageSpeed,
            deltaTime,
            true,
            verticalDistance,
            horizontalDistance);

        ApplyTakeoffOrientation(targetNoseUpDegrees);
        UpdateAltitude();
        UpdateRunwayReservation();

        debugStatus =
            $"Climbing: {currentAltitudeAboveRunway:F1}/" +
            $"{targetAltitudeAboveRunway:F1} altitude units.";

        CheckTargetAltitude();
    }

    private void MoveAlongRunway(
        float averageSpeedKnots,
        float deltaTime,
        bool allowVerticalMovement,
        float verticalDistance = 0f,
        float? horizontalDistanceOverride = null)
    {
        float horizontalDistance = horizontalDistanceOverride ??
            KnotsToWorldSpeed(averageSpeedKnots) * deltaTime;

        Vector3 position = transform.position +
            takeoffDirection * horizontalDistance;

        if (allowVerticalMovement)
            position.y += verticalDistance;
        else
            position.y = groundLevelY;

        transform.position = position;
    }

    private void ApplyTakeoffOrientation(float noseUpDegrees)
    {
        Quaternion heading = Quaternion.LookRotation(
            takeoffDirection,
            Vector3.up);

        Quaternion pitch = Quaternion.Euler(-noseUpDegrees, 0f, 0f);

        transform.rotation =
            heading * pitch * Quaternion.Euler(modelRotationOffset);
    }

    private float KnotsToWorldSpeed(float knots)
    {
        return knots * worldUnitsPerSecondPerKnot;
    }

    private void UpdateAltitude()
    {
        currentAltitudeAboveRunway =
            Mathf.Max(0f, transform.position.y - runwaySurfaceY);
    }

    private void CheckTargetAltitude()
    {
        if (currentAltitudeAboveRunway >= targetAltitudeAboveRunway - 0.001f)
            CompleteTakeoff();
    }

    private void UpdateRunwayReservation()
    {
        if (runwayReservationReleased ||
            selectedRunway == null ||
            aircraftCollider == null ||
            departureController == null)
            return;

        Vector3 offset = transform.position - runwayCenter;
        offset.y = 0f;

        float distanceAlongRunway =
            Vector3.Dot(offset, takeoffDirection);

        Bounds bounds = aircraftCollider.bounds;

        float forwardExtent =
            Mathf.Abs(takeoffDirection.x) * bounds.extents.x +
            Mathf.Abs(takeoffDirection.z) * bounds.extents.z;

        float runwayEnd = runwayLength * 0.5f;
        float requiredDistance =
            runwayEnd + forwardExtent + runwayClearanceBuffer;

        if (distanceAlongRunway >= requiredDistance)
            ReleaseRunwayReservations();
    }

    private void ReleaseRunwayReservations()
    {
        if (runwayReservationReleased)
            return;

        if (trafficManager != null && departureController != null)
        {
            trafficManager.ReleaseRunwayLineUpReservation(departureController);
            trafficManager.ReleaseDepartureReservation(departureController);
        }

        runwayReservationReleased = true;
    }

    private void CompleteTakeoff()
    {
        if (currentState == TakeoffState.Completed)
            return;

        UpdateAltitude();
        currentState = TakeoffState.Completed;

        PaySightseeingCommissionOnce();
        ReleaseRunwayReservations();

        debugStatus = "Takeoff complete. Aircraft despawning.";

        Debug.Log(
            $"SIGHTSEEING TAKEOFF COMPLETE | Aircraft: {name} | " +
            $"Passengers: {passengersOnBoard} | " +
            $"Commission: ${commissionPaid}",
            this);

        Destroy(gameObject);
    }

    private void PaySightseeingCommissionOnce()
    {
        if (revenuePaid)
            return;

        // Set the guard first so this aircraft cannot pay twice.
        revenuePaid = true;

        if (passengersOnBoard <= 0)
        {
            commissionPaid = 0;

            Debug.Log(
                $"SIGHTSEEING NO-PASSENGER DEPARTURE | Aircraft: {name} | " +
                "No ticket commission earned.",
                this);

            return;
        }

        if (sightseeingEconomy == null)
        {
            Debug.LogError(
                $"{name}: SightseeingFlightEconomy is missing. " +
                "Commission was not paid.",
                this);
            return;
        }

        if (economyManager == null)
        {
            Debug.LogError(
                $"{name}: AirportEconomyManager could not be found. " +
                "Commission was not paid.",
                this);
            return;
        }

        commissionPaid =
            sightseeingEconomy.CalculateAirportCommission(passengersOnBoard);

        if (commissionPaid <= 0)
            return;

        bool paid = economyManager.AddRevenue(
            commissionPaid,
            $"Sightseeing flight ({passengersOnBoard} passengers)");

        if (!paid)
        {
            commissionPaid = 0;

            Debug.LogWarning(
                $"{name}: Sightseeing commission payment failed.",
                this);
        }
    }

    private void OnDestroy()
    {
        ReleaseRunwayReservations();
    }
}