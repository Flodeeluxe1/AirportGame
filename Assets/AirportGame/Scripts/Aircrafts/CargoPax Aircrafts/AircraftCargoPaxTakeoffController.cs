
using UnityEngine;

public class AircraftCargoPaxTakeoffController : MonoBehaviour
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
    [SerializeField] private AircraftCargoPaxDepartureTaxiController departureController;
    [SerializeField] private AircraftRunwayTrafficManager trafficManager;
    [SerializeField] private GameTimeManager gameTimeManager;
    [SerializeField] private GridManager gridManager;
    [SerializeField] private AircraftStatus aircraftStatus;

    [Tooltip("BoxCollider enclosing the aircraft.")]
    [SerializeField] private BoxCollider aircraftCollider;

    [Header("Takeoff Speed")]
    [Tooltip("Target takeoff speed in knots.")]
    [Min(1f)]
    [SerializeField] private float takeoffSpeedKnots = 90f;

    [Tooltip("Acceleration along the runway before rotation, in knots per second.")]
    [Min(0.1f)]
    [SerializeField] private float groundAccelerationKnotsPerSecond = 20f;

    [Tooltip("Acceleration during rotation and climb, in knots per second.")]
    [Min(0.1f)]
    [SerializeField] private float airborneAccelerationKnotsPerSecond = 3f;

    [Header("Rotation")]
    [Tooltip("Rotation begins at this fraction of takeoff speed.")]
    [Range(0.1f, 1f)]
    [SerializeField] private float rotationStartSpeedFraction = 0.8f;

    [Tooltip("Rate at which the aircraft's nose rotates upward.")]
    [Min(0.1f)]
    [SerializeField] private float rotationRateDegreesPerSecond = 3f;

    [Tooltip("Final nose-up attitude.")]
    [Range(1f, 45f)]
    [SerializeField] private float targetNoseUpDegrees = 15f;

    [Tooltip("The aircraft starts gaining altitude gradually after reaching this nose-up angle.")]
    [Range(1f, 24f)]
    [SerializeField] private float liftoffStartNoseUpDegrees = 2f;

    [Tooltip("Use this if the aircraft model's nose isn't its local +Z axis.")]
    [SerializeField] private Vector3 modelRotationOffset;

    [Header("Climb")]
    [Tooltip("Target altitude above the runway surface, in Unity world units.")]
    [Min(1f)]
    [SerializeField] private float targetAltitudeAboveRunway = 60f;

    [Header("Runway Clearance")]
    [Tooltip("Extra distance beyond the runway end before releasing reservations.")]
    [Min(0f)]
    [SerializeField] private float runwayClearanceBuffer = 1f;

    [Header("Runtime Status")]
    [SerializeField]
    private TakeoffState currentState =
        TakeoffState.WaitingForLineUp;

    [SerializeField] private float currentSpeedKnots;
    [SerializeField] private float currentNoseUpDegrees;
    [SerializeField] private float rotationTriggerSpeedKnots;
    [SerializeField] private float currentAltitudeAboveRunway;
    [SerializeField] private bool runwayReservationReleased;
    [SerializeField] private string debugStatus = "Waiting for line-up";

    private AircraftRunway selectedRunway;
    private Vector3 takeoffDirection;
    private Vector3 runwayCenter;
    private float runwayLength;
    private float runwaySurfaceY;
    private float groundLevelY;

    public TakeoffState CurrentState => currentState;

    public bool IsTakingOff =>
        currentState == TakeoffState.TakeoffRoll ||
        currentState == TakeoffState.Rotating ||
        currentState == TakeoffState.Climbing;

    public bool IsCompleted => currentState == TakeoffState.Completed;

    private void Awake()
    {
        ResolveReferences();
    }

    private void Update()
    {
        if (currentState == TakeoffState.Completed)
            return;

        if (currentState == TakeoffState.WaitingForLineUp)
        {
            ResolveReferences();

            if (departureController == null ||
                !departureController.IsLinedUp)
            {
                return;
            }

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

        PublishCurrentSpeed();
    }

    private void ResolveReferences()
    {
        if (departureController == null)
            departureController =
                GetComponent<AircraftCargoPaxDepartureTaxiController>();

        if (trafficManager == null)
            trafficManager =
                FindFirstObjectByType<AircraftRunwayTrafficManager>();

        if (gameTimeManager == null)
            gameTimeManager =
                FindFirstObjectByType<GameTimeManager>();

        if (gridManager == null)
            gridManager =
                FindFirstObjectByType<GridManager>();

        if (aircraftStatus == null)
            aircraftStatus = GetComponent<AircraftStatus>();

        if (aircraftCollider == null)
            aircraftCollider =
                GetComponentInChildren<BoxCollider>(true);
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
            gridManager == null)
        {
            debugStatus =
                "Cannot take off: a required controller is missing.";

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
            debugStatus =
                "Cannot take off: selected runway geometry is unavailable.";

            Debug.LogError($"{name}: {debugStatus}", this);
            return false;
        }

        if (aircraftCollider == null)
        {
            debugStatus =
                "Cannot take off: aircraft-sized BoxCollider is missing.";

            Debug.LogError($"{name}: {debugStatus}", this);
            return false;
        }

        takeoffDirection = departureController.TakeoffDirection;
        takeoffDirection.y = 0f;

        if (takeoffDirection.sqrMagnitude < 0.0001f)
        {
            debugStatus = "Cannot take off: takeoff direction is invalid.";

            Debug.LogError($"{name}: {debugStatus}", this);
            return false;
        }

        takeoffDirection.Normalize();

        groundLevelY = transform.position.y;
        currentSpeedKnots = 0f;
        currentNoseUpDegrees = 0f;

        PublishCurrentSpeed();

        rotationTriggerSpeedKnots =
            takeoffSpeedKnots * rotationStartSpeedFraction;

        currentAltitudeAboveRunway =
            Mathf.Max(0f, transform.position.y - runwaySurfaceY);

        runwayReservationReleased = false;

        ApplyTakeoffOrientation(0f);

        currentState = TakeoffState.TakeoffRoll;

        debugStatus =
            $"Accelerating on runway. Rotation begins at " +
            $"{rotationTriggerSpeedKnots:F1} knots.";

        Debug.Log(
            $"AIRCRAFT TAKEOFF ROLL STARTED | " +
            $"Aircraft: {name} | Runway: {selectedRunway.name} | " +
            $"Takeoff speed: {takeoffSpeedKnots:F1} knots | " +
            $"Rotation threshold: {rotationTriggerSpeedKnots:F1} knots",
            this
        );

        return true;
    }

    private void UpdateTakeoffRoll(float deltaTime)
    {
        float previousSpeed = currentSpeedKnots;

        currentSpeedKnots = Mathf.MoveTowards(
            currentSpeedKnots,
            rotationTriggerSpeedKnots,
            groundAccelerationKnotsPerSecond * deltaTime
        );

        float averageSpeed =
            (previousSpeed + currentSpeedKnots) * 0.5f;

        MoveAlongRunway(averageSpeed, deltaTime, false);

        ApplyTakeoffOrientation(0f);
        UpdateRunwayReservation();

        debugStatus =
            $"Takeoff roll: {currentSpeedKnots:F1} knots. " +
            $"Rotation begins at {rotationTriggerSpeedKnots:F1} knots.";

        if (currentSpeedKnots >= rotationTriggerSpeedKnots - 0.001f)
        {
            currentSpeedKnots = rotationTriggerSpeedKnots;
            currentState = TakeoffState.Rotating;

            debugStatus =
                $"Rotation started at {currentSpeedKnots:F1} knots.";

            Debug.Log(
                $"AIRCRAFT ROTATION STARTED | Aircraft: {name} | " +
                $"Speed: {currentSpeedKnots:F1} knots | " +
                $"Nose rotation: {rotationRateDegreesPerSecond:F1} degrees/sec",
                this
            );
        }
    }

    private void UpdateRotationAndLiftoff(float deltaTime)
    {
        float previousSpeed = currentSpeedKnots;

        currentSpeedKnots = Mathf.MoveTowards(
            currentSpeedKnots,
            takeoffSpeedKnots,
            airborneAccelerationKnotsPerSecond * deltaTime
        );

        float averageSpeed =
            (previousSpeed + currentSpeedKnots) * 0.5f;

        currentNoseUpDegrees = Mathf.MoveTowards(
            currentNoseUpDegrees,
            targetNoseUpDegrees,
            rotationRateDegreesPerSecond * deltaTime
        );

        float horizontalDistance =
            AircraftStatus.KnotsToWorldSpeed(averageSpeed) * deltaTime;

        float verticalDistance = 0f;

        if (currentNoseUpDegrees > liftoffStartNoseUpDegrees)
        {
            float liftoffProgress = Mathf.InverseLerp(
                liftoffStartNoseUpDegrees,
                targetNoseUpDegrees,
                currentNoseUpDegrees
            );

            liftoffProgress = Mathf.SmoothStep(
                0f,
                1f,
                liftoffProgress
            );

            float climbSlope = Mathf.Tan(
                currentNoseUpDegrees * Mathf.Deg2Rad
            );

            verticalDistance =
                horizontalDistance *
                climbSlope *
                liftoffProgress;
        }

        MoveAlongRunway(
            averageSpeed,
            deltaTime,
            true,
            verticalDistance
        );

        ApplyTakeoffOrientation(currentNoseUpDegrees);
        UpdateAltitude();
        UpdateRunwayReservation();

        debugStatus =
            $"Rotating: {currentNoseUpDegrees:F1} degrees nose-up, " +
            $"{currentSpeedKnots:F1} knots.";

        if (currentNoseUpDegrees >= targetNoseUpDegrees - 0.001f)
        {
            currentNoseUpDegrees = targetNoseUpDegrees;
            ApplyTakeoffOrientation(currentNoseUpDegrees);

            currentState = TakeoffState.Climbing;

            debugStatus =
                $"Climbing at {targetNoseUpDegrees:F1} degrees nose-up.";

            Debug.Log(
                $"AIRCRAFT ROTATION COMPLETE | Aircraft: {name} | " +
                $"Speed: {currentSpeedKnots:F1} knots | " +
                $"Nose-up attitude: {currentNoseUpDegrees:F1} degrees",
                this
            );
        }

        CheckTargetAltitude();
    }

    private void UpdateClimb(float deltaTime)
    {
        float previousSpeed = currentSpeedKnots;

        currentSpeedKnots = Mathf.MoveTowards(
            currentSpeedKnots,
            takeoffSpeedKnots,
            airborneAccelerationKnotsPerSecond * deltaTime
        );

        float averageSpeed =
            (previousSpeed + currentSpeedKnots) * 0.5f;

        float horizontalDistance =
            AircraftStatus.KnotsToWorldSpeed(averageSpeed) * deltaTime;

        float verticalDistance =
            horizontalDistance *
            Mathf.Tan(targetNoseUpDegrees * Mathf.Deg2Rad);

        float targetY =
            runwaySurfaceY + targetAltitudeAboveRunway;

        float remainingAltitude =
            targetY - transform.position.y;

        if (verticalDistance >= remainingAltitude)
        {
            float climbSlope = Mathf.Max(
                0.001f,
                Mathf.Tan(targetNoseUpDegrees * Mathf.Deg2Rad)
            );

            horizontalDistance =
                Mathf.Max(0f, remainingAltitude / climbSlope);

            verticalDistance = Mathf.Max(0f, remainingAltitude);
        }

        MoveAlongRunway(
            averageSpeed,
            deltaTime,
            true,
            verticalDistance,
            horizontalDistance
        );

        ApplyTakeoffOrientation(targetNoseUpDegrees);
        UpdateAltitude();
        UpdateRunwayReservation();

        debugStatus =
            $"Climbing: {currentSpeedKnots:F1} knots, " +
            $"{currentAltitudeAboveRunway:F1}/" +
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
        float horizontalDistance =
            horizontalDistanceOverride ??
            AircraftStatus.KnotsToWorldSpeed(averageSpeedKnots) * deltaTime;

        Vector3 position =
            transform.position +
            takeoffDirection * horizontalDistance;

        if (allowVerticalMovement)
            position.y += verticalDistance;
        else
            position.y = groundLevelY;

        transform.position = position;
    }

    private void ApplyTakeoffOrientation(float noseUpDegrees)
    {
        Quaternion headingRotation =
            Quaternion.LookRotation(takeoffDirection, Vector3.up);

        Quaternion pitchRotation =
            Quaternion.Euler(-noseUpDegrees, 0f, 0f);

        transform.rotation =
            headingRotation *
            pitchRotation *
            Quaternion.Euler(modelRotationOffset);
    }

    private void PublishCurrentSpeed()
    {
        if (aircraftStatus != null)
            aircraftStatus.SetCurrentSpeedKnots(currentSpeedKnots);
    }

    private void UpdateAltitude()
    {
        currentAltitudeAboveRunway =
            Mathf.Max(0f, transform.position.y - runwaySurfaceY);
    }

    private void CheckTargetAltitude()
    {
        if (currentAltitudeAboveRunway >=
            targetAltitudeAboveRunway - 0.001f)
        {
            CompleteTakeoff();
        }
    }

    private void UpdateRunwayReservation()
    {
        if (runwayReservationReleased ||
            selectedRunway == null ||
            aircraftCollider == null ||
            departureController == null)
        {
            return;
        }

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
            runwayEnd +
            forwardExtent +
            runwayClearanceBuffer;

        if (distanceAlongRunway < requiredDistance)
            return;

        ReleaseRunwayReservations();

        Debug.Log(
            $"AIRCRAFT CLEARED RUNWAY | Aircraft: {name} | " +
            $"Runway: {selectedRunway.name}",
            this
        );
    }

    private void ReleaseRunwayReservations()
    {
        if (runwayReservationReleased)
            return;

        if (trafficManager != null &&
            departureController != null)
        {
            trafficManager.ReleaseRunwayLineUpReservation(
                departureController
            );

            trafficManager.ReleaseDepartureReservation(
                departureController
            );
        }

        runwayReservationReleased = true;
    }

    private void CompleteTakeoff()
    {
        if (currentState == TakeoffState.Completed)
            return;

        UpdateAltitude();

        currentState = TakeoffState.Completed;

        PublishCurrentSpeed();

        ReleaseRunwayReservations();

        debugStatus = "Target altitude reached. Aircraft despawning.";

        Debug.Log(
            $"AIRCRAFT TAKEOFF COMPLETE | Aircraft: {name} | " +
            $"Runway: {(selectedRunway != null ? selectedRunway.name : "Unknown")} | " +
            $"Final speed: {currentSpeedKnots:F1} knots | " +
            $"Altitude above runway: {currentAltitudeAboveRunway:F1}",
            this
        );

        PassengerFlightRevenue passengerRevenue =
            GetComponent<PassengerFlightRevenue>();

        if (passengerRevenue != null)
            passengerRevenue.RecordSuccessfulDeparture();

        AircraftFlightIdentity flightIdentity =
            GetComponent<AircraftFlightIdentity>();

        if (FlightOperationsBoardUI.Instance != null)
        {
            FlightOperationsBoardUI.Instance.MarkDeparted(flightIdentity);
        }

        FlightScheduleBoardPanel.RecordDeparture(
            GetComponent<AircraftFlightIdentity>());

        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (runwayReservationReleased)
            return;

        ReleaseRunwayReservations();
    }
}
