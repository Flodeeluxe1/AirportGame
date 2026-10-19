
using System.Collections.Generic;
using UnityEngine;

public class TrainingFlightController : MonoBehaviour
{
    private enum TrainingState
    {
        Inactive,
        WaitingForTakeoff,
        FlyingCircuit,
        Landing,
        WaitingForTouchAndGoClearance,
        TaxiingBackToSchool,
        TurningAtParking,
        Completed,
        Failed
    }

    [Header("Lesson")]
    [SerializeField, Min(1)]
    private int requiredCircuits = 5;

    [SerializeField, Min(1f)]
    private float circuitExtension = 150f;

    [SerializeField, Min(1f)]
    private float circuitWidth = 100f;

    [SerializeField, Min(1f)]
    private float circuitSpeed = 8f;

    [SerializeField, Min(0.1f)]
    private float waypointArrivalDistance = 2f;

    [Header("Parking Turn")]
    [Tooltip("Degrees the aircraft turns after reaching its parking spot.")]
    [SerializeField, Range(0f, 360f)]
    private float parkingTurnAngle = 180f;

    [Tooltip("Speed of the turn in degrees per second.")]
    [SerializeField, Min(1f)]
    private float parkingTurnSpeed = 20f;

    [Header("Runtime Status")]
    [SerializeField] private int completedCircuits;
    [SerializeField] private bool departureRequested;

    [SerializeField]
    private TrainingState currentState = TrainingState.Inactive;

    [SerializeField]
    private string debugStatus = "Waiting for Flight School Manager.";

    private AircraftFlightIdentity flightIdentity;

    private AircraftTrainingDepartureTaxiController departureController;
    private AircraftTrainingTakeoffController takeoffController;
    private AircraftTrainingLandingController landingController;
    private AircraftTrainingLandingTaxiController taxiController;
    private AircraftRunwayTrafficManager trafficManager;

    private readonly List<Vector3> circuitWaypoints = new();

    private int currentWaypointIndex;
    private bool landingCounted;

    private AircraftRunway touchAndGoRunway;
    private Vector3 touchAndGoDirection;

    private Quaternion parkingTurnTargetRotation;
    private Vector3 parkingTurnPosition;

    public bool DepartureRequested => departureRequested;
    public int CompletedCircuits => completedCircuits;
    public int RequiredCircuits => requiredCircuits;

    public bool IsLessonComplete =>
        currentState == TrainingState.Completed;

    public bool HasFailed =>
        currentState == TrainingState.Failed;

    public string DebugStatus => debugStatus;

    private void Awake()
    {
        ResolveReferences();
    }

    private void ResolveReferences()
    {
        if (flightIdentity == null)
            flightIdentity = GetComponent<AircraftFlightIdentity>();

        if (departureController == null)
            departureController =
                GetComponent<AircraftTrainingDepartureTaxiController>();

        if (takeoffController == null)
            takeoffController =
                GetComponent<AircraftTrainingTakeoffController>();

        if (landingController == null)
            landingController =
                GetComponent<AircraftTrainingLandingController>();

        if (taxiController == null)
            taxiController =
                GetComponent<AircraftTrainingLandingTaxiController>();

        if (trafficManager == null)
            trafficManager =
                FindFirstObjectByType<AircraftRunwayTrafficManager>();
    }

    private void Update()
    {
        if (currentState == TrainingState.Inactive ||
            currentState == TrainingState.Completed ||
            currentState == TrainingState.Failed)
        {
            return;
        }

        ResolveReferences();

        if (!HasRequiredComponents())
        {
            FailTraining(
                "A required training, taxi, landing, or takeoff component is missing."
            );

            return;
        }

        switch (currentState)
        {
            case TrainingState.WaitingForTakeoff:
                UpdateWaitingForTakeoff();
                break;

            case TrainingState.FlyingCircuit:
                UpdateFlyingCircuit();
                break;

            case TrainingState.Landing:
                UpdateLanding();
                break;

            case TrainingState.WaitingForTouchAndGoClearance:
                UpdateWaitingForTouchAndGoClearance();
                break;

            case TrainingState.TaxiingBackToSchool:
                UpdateTaxiingBackToSchool();
                break;

            case TrainingState.TurningAtParking:
                UpdateTurningAtParking();
                break;
        }
    }

    public bool StartTrainingLesson()
    {
        ResolveReferences();

        if (currentState != TrainingState.Inactive)
        {
            Debug.LogWarning(
                $"{name}: This training lesson has already been started.",
                this
            );

            return false;
        }

        if (flightIdentity == null ||
            !flightIdentity.IsInitialized ||
            !flightIdentity.IsTrainingFlight)
        {
            Debug.LogWarning(
                $"{name}: Initialize this aircraft as a training flight " +
                "before starting its lesson.",
                this
            );

            return false;
        }

        if (!HasRequiredComponents())
        {
            FailTraining(
                "Cannot start the lesson because required components are missing."
            );

            return false;
        }

        if (requiredCircuits < 1)
            requiredCircuits = 1;

        completedCircuits = 0;
        landingCounted = false;

        touchAndGoRunway = null;
        touchAndGoDirection = Vector3.zero;

        parkingTurnTargetRotation = Quaternion.identity;
        parkingTurnPosition = transform.position;

        // The taxi controller is only needed after the final landing.
        taxiController.enabled = false;

        departureRequested = true;
        currentState = TrainingState.WaitingForTakeoff;

        debugStatus =
            "Lesson started. Taxiing from school parking for takeoff.";

        Debug.Log(
            $"{name}: Training lesson started. " +
            $"Required circuits: {requiredCircuits}.",
            this
        );

        return true;
    }

    private bool HasRequiredComponents()
    {
        return flightIdentity != null &&
               departureController != null &&
               takeoffController != null &&
               landingController != null &&
               taxiController != null;
    }

    public void ClearDepartureRequest()
    {
        departureRequested = false;
    }

    private void UpdateWaitingForTakeoff()
    {
        if (!takeoffController.IsCompleted)
            return;

        if (!BuildCircuitWaypoints())
        {
            FailTraining(
                "Could not build the circuit because runway geometry is unavailable."
            );

            return;
        }

        currentWaypointIndex = 0;
        currentState = TrainingState.FlyingCircuit;

        debugStatus =
            $"Takeoff complete. Flying circuit " +
            $"{completedCircuits + 1} of {requiredCircuits}.";

        Debug.Log(
            $"{name}: Takeoff complete. Beginning training circuit " +
            $"{completedCircuits + 1}/{requiredCircuits}.",
            this
        );
    }

    private bool BuildCircuitWaypoints()
    {
        circuitWaypoints.Clear();

        AircraftRunway runway = departureController.SelectedRunway;

        if (runway == null)
            return false;

        GridManager gridManager =
            FindFirstObjectByType<GridManager>();

        if (gridManager == null ||
            !runway.TryGetGeometry(
                gridManager,
                out Vector3 runwayCenter,
                out Vector3 runwayAxis,
                out float runwayLength,
                out float runwaySurfaceY))
        {
            return false;
        }

        Vector3 direction = departureController.TakeoffDirection;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.0001f)
        {
            runwayAxis.y = 0f;

            if (runwayAxis.sqrMagnitude < 0.0001f)
                return false;

            direction = runwayAxis;
        }

        direction.Normalize();

        Vector3 crosswindDirection =
            Vector3.Cross(Vector3.up, direction).normalized;

        float downwindDistance =
            runwayLength * 0.5f + circuitExtension;

        Vector3 upwindPoint =
            runwayCenter + direction * downwindDistance;

        Vector3 crosswindPoint =
            upwindPoint + crosswindDirection * circuitWidth;

        Vector3 downwindPoint =
            runwayCenter -
            direction * downwindDistance +
            crosswindDirection * circuitWidth;

        Vector3 basePoint =
            runwayCenter -
            direction * downwindDistance;

        float flightAltitude = runwaySurfaceY + 20f;

        upwindPoint.y = flightAltitude;
        crosswindPoint.y = flightAltitude;
        downwindPoint.y = flightAltitude;
        basePoint.y = flightAltitude;

        circuitWaypoints.Add(upwindPoint);
        circuitWaypoints.Add(crosswindPoint);
        circuitWaypoints.Add(downwindPoint);
        circuitWaypoints.Add(basePoint);

        return true;
    }

    private void UpdateFlyingCircuit()
    {
        if (currentWaypointIndex >= circuitWaypoints.Count)
        {
            BeginLanding();
            return;
        }

        Vector3 target = circuitWaypoints[currentWaypointIndex];
        Vector3 toTarget = target - transform.position;

        if (toTarget.magnitude <= waypointArrivalDistance)
        {
            currentWaypointIndex++;

            if (currentWaypointIndex >= circuitWaypoints.Count)
                BeginLanding();

            return;
        }

        Vector3 direction = toTarget.normalized;

        if (direction.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRotation =
                Quaternion.LookRotation(direction, Vector3.up);

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                30f * Time.deltaTime
            );
        }

        transform.position = Vector3.MoveTowards(
            transform.position,
            target,
            circuitSpeed * Time.deltaTime
        );
    }

    private void BeginLanding()
    {
        landingController.enabled = true;

        if (!landingController.BeginTrainingLanding())
        {
            FailTraining(
                "The landing controller could not begin the training landing."
            );

            return;
        }

        if (completedCircuits >= requiredCircuits - 1)
            taxiController.enabled = true;

        landingCounted = false;
        currentState = TrainingState.Landing;

        debugStatus =
            $"Circuit {completedCircuits + 1}: approaching runway to land.";

        Debug.Log(
            $"{name}: Circuit pattern complete. Beginning landing " +
            $"{completedCircuits + 1}/{requiredCircuits}.",
            this
        );
    }

    private void UpdateLanding()
    {
        if (!landingController.IsLandingComplete)
            return;

        if (!landingCounted)
        {
            completedCircuits++;
            landingCounted = true;

            Debug.Log(
                $"{name}: Training landing complete. " +
                $"Circuits completed: {completedCircuits}/{requiredCircuits}.",
                this
            );
        }

        if (completedCircuits >= requiredCircuits)
        {
            taxiController.enabled = true;

            currentState = TrainingState.TaxiingBackToSchool;
            debugStatus =
                "Final landing complete. Taxiing back to school parking.";

            Debug.Log(
                $"{name}: Final training landing complete. " +
                "Taxiing to the flight-school parking spot.",
                this
            );

            return;
        }

        touchAndGoRunway = landingController.SelectedRunway;
        touchAndGoDirection = landingController.LandingDirection;
        touchAndGoDirection.y = 0f;

        if (touchAndGoRunway == null ||
            touchAndGoDirection.sqrMagnitude < 0.0001f)
        {
            FailTraining(
                "Touch-and-go failed because the landing runway or direction is invalid."
            );

            return;
        }

        touchAndGoDirection.Normalize();

        if (trafficManager != null)
            trafficManager.UnregisterLandingTraffic(landingController);

        landingController.enabled = false;
        taxiController.enabled = false;

        ClearDepartureRequest();

        if (!takeoffController.ResetForTrainingCircuit())
        {
            FailTraining(
                "Could not reset the takeoff controller for touch-and-go."
            );

            return;
        }

        currentState = TrainingState.WaitingForTouchAndGoClearance;

        debugStatus =
            $"Landing {completedCircuits}/{requiredCircuits} complete. " +
            "Preparing immediate runway takeoff.";

        Debug.Log(
            $"{name}: Preparing touch-and-go after landing " +
            $"{completedCircuits}/{requiredCircuits}.",
            this
        );
    }

    private void UpdateWaitingForTouchAndGoClearance()
    {
        if (touchAndGoRunway == null)
        {
            FailTraining(
                "Touch-and-go failed because the saved runway is missing."
            );

            return;
        }

        if (!departureController.PrepareForTouchAndGo(
                touchAndGoRunway,
                touchAndGoDirection))
        {
            debugStatus =
                "Waiting for runway clearance for touch-and-go.";

            return;
        }

        touchAndGoRunway = null;
        touchAndGoDirection = Vector3.zero;

        currentState = TrainingState.WaitingForTakeoff;

        debugStatus =
            $"Touch-and-go cleared. Taking off for circuit " +
            $"{completedCircuits + 1}/{requiredCircuits}.";

        Debug.Log(
            $"{name}: Touch-and-go cleared. Taking off directly from " +
            $"the runway for circuit {completedCircuits + 1}/{requiredCircuits}.",
            this
        );
    }

    private void UpdateTaxiingBackToSchool()
    {
        if (!taxiController.HasArrivedAtParking)
            return;

        // The taxi controller has reported arrival. Keep the aircraft at
        // this position and rotate it in place before marking the lesson done.
        parkingTurnPosition = transform.position;

        parkingTurnTargetRotation =
            Quaternion.AngleAxis(parkingTurnAngle, Vector3.up) *
            transform.rotation;

        currentState = TrainingState.TurningAtParking;

        debugStatus =
            $"Parked. Turning {parkingTurnAngle:0} degrees to face the taxiway.";

        Debug.Log(
            $"{name}: Arrived at school parking. Beginning a " +
            $"{parkingTurnAngle:0}-degree turn in place.",
            this
        );

        if (parkingTurnAngle <= 0.01f)
            FinishParkingTurn();
    }

    private void UpdateTurningAtParking()
    {
        // Prevent any positional drift while the aircraft turns.
        transform.position = parkingTurnPosition;

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            parkingTurnTargetRotation,
            parkingTurnSpeed * Time.deltaTime
        );

        if (Quaternion.Angle(
                transform.rotation,
                parkingTurnTargetRotation) <= 0.1f)
        {
            FinishParkingTurn();
        }
    }

    private void FinishParkingTurn()
    {
        transform.position = parkingTurnPosition;
        transform.rotation = parkingTurnTargetRotation;

        currentState = TrainingState.Completed;
        departureRequested = false;

        debugStatus =
            $"Lesson complete: {completedCircuits} circuits. " +
            "Aircraft turned at parking and is ready for Flight School Manager payout.";

        Debug.Log(
            $"{name}: TRAINING LESSON COMPLETE | " +
            $"Circuits: {completedCircuits}/{requiredCircuits}. " +
            "Aircraft is parked and has finished turning toward the taxiway. " +
            "The Flight School Manager can now pay the lesson reward.",
            this
        );
    }

    private void FailTraining(string reason)
    {
        currentState = TrainingState.Failed;
        departureRequested = false;
        debugStatus = reason;

        Debug.LogError(
            $"{name}: Training lesson stopped. {reason}",
            this
        );
    }

    public bool ResetForNextLesson()
    {
        ResolveReferences();

        if (currentState != TrainingState.Completed)
        {
            Debug.LogWarning(
                $"{name}: Cannot reset for the next lesson because " +
                "the current lesson has not completed.",
                this
            );

            return false;
        }

        if (!HasRequiredComponents())
        {
            Debug.LogError(
                $"{name}: Cannot reset for the next lesson because " +
                "required components are missing.",
                this
            );

            return false;
        }

        if (!takeoffController.ResetForTrainingCircuit())
            return false;

        if (!departureController.ResetForTrainingCircuit())
            return false;

        completedCircuits = 0;
        currentWaypointIndex = 0;
        landingCounted = false;
        departureRequested = false;

        circuitWaypoints.Clear();

        touchAndGoRunway = null;
        touchAndGoDirection = Vector3.zero;

        parkingTurnTargetRotation = Quaternion.identity;
        parkingTurnPosition = transform.position;

        taxiController.ResetForNextTrainingLesson();

        landingController.enabled = true;
        taxiController.enabled = false;

        currentState = TrainingState.Inactive;
        debugStatus = "Ready to start the next training lesson.";

        Debug.Log(
            $"{name}: Reset complete. Ready for the next training lesson.",
            this
        );

        return true;
    }
}
