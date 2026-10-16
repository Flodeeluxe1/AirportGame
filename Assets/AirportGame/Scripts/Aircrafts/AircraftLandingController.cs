using UnityEngine;

public class AircraftLandingController : MonoBehaviour
{
    private enum LandingState
    {
        Initializing,
        Aligning,
        FinalApproach,
        Flare,
        Rollout,
        LandingComplete,
        GoAround,
        Failed
    }

    [Header("References")]
    [SerializeField] private GridManager gridManager;
    [SerializeField] private Weather weather;

    [Header("Approach")]
    [Min(1f)]
    [SerializeField] private float approachDistance = 60f;

    [Range(1f, 6f)]
    [SerializeField] private float glideAngleDegrees = 3f;

    [Header("Speeds")]
    [Min(1f)]
    [SerializeField] private float approachSpeedKnots = 140f;

    [Min(1f)]
    [SerializeField] private float touchdownSpeedKnots = 120f;

    [Min(1f)]
    [SerializeField] private float rolloutTargetSpeedKnots = 30f;

    [Tooltip("Converts knots into Unity world units per second.")]
    [Min(0.001f)]
    [SerializeField] private float worldUnitsPerSecondPerKnot = 0.05f;

    [Min(0.1f)]
    [SerializeField] private float speedChangeRate = 3f;

    [Min(0.1f)]
    [SerializeField] private float rolloutDeceleration = 1.2f;

    [Header("Flare")]
    [Min(0.1f)]
    [SerializeField] private float flareHeight = 1.5f;

    [Range(0f, 10f)]
    [SerializeField] private float noseRaiseDegrees = 3f;

    [Min(0.1f)]
    [SerializeField] private float noseLoweringDuration = 1.5f;

    [Header("Crosswind and Go-Around")]
    [Tooltip("Maximum permitted crosswind component for landing, in knots.")]
    [Min(0f)]
    [SerializeField] private float maximumSidewindComponentKnots = 15f;

    [Tooltip("Nose-up attitude during a go-around.")]
    [Range(1f, 45f)]
    [SerializeField] private float goAroundNoseUpDegrees = 20f;

    [Tooltip("Go-around target height above the runway surface, in Unity world units.")]
    [Min(1f)]
    [SerializeField] private float goAroundAltitudeAboveRunway = 30f;

    [Header("Steering")]
    [Min(0.1f)]
    [SerializeField] private float rotationSpeed = 3f;

    [Tooltip("Use this if the aircraft model's nose is not its local +Z axis.")]
    [SerializeField] private Vector3 modelRotationOffset;

    [Header("Debug")]
    public string debugLandingState = "Initializing";
    public string debugSelectedRunway = "None";
    public string debugLandingHeading = "None";

    public float debugHeadwindComponentKnots;
    public float debugSidewindComponentKnots;
    public float debugCurrentSpeedKnots;
    public float debugDistanceToThreshold;
    public float debugGoAroundTargetAltitude;

    public string debugStatus = "Waiting for initialization";

    public bool IsLandingComplete =>
        state == LandingState.LandingComplete;

    public bool IsGoingAround =>
        state == LandingState.GoAround;

    public AircraftRunway SelectedRunway =>
        selectedRunway;

    public Vector3 LandingDirection => runwayDirection;

    private LandingState state =
        LandingState.Initializing;

    public bool IsLandingTrafficActive
    {
        get
        {
            return selectedRunway != null &&
                   (
                       state == LandingState.Aligning ||
                       state == LandingState.FinalApproach ||
                       state == LandingState.Flare ||
                       state == LandingState.Rollout ||
                       state == LandingState.GoAround
                   );
        }
    }

    private AircraftRunway selectedRunway;

    private Vector3 runwayDirection;
    private Vector3 runwayCenter;
    private Vector3 touchdownPoint;
    private Vector3 alignmentPoint;

    private float runwayLength;
    private float runwaySurfaceY;
    private float currentSpeed;
    private float rolloutElapsed;

    private bool flareDecisionMade;

    private void Start()
    {
        if (gridManager == null)
        {
            gridManager =
                FindFirstObjectByType<GridManager>();
        }

        if (weather == null)
        {
            weather =
                FindFirstObjectByType<Weather>();
        }

        if (gridManager == null ||
            weather == null)
        {
            Fail(
                "GridManager or Weather is missing."
            );

            return;
        }

        if (!SelectLandingRunway())
        {
            Fail(
                "No placed runway with AircraftRunway was found."
            );

            return;
        }

        currentSpeed =
            KnotsToWorldSpeed(
                approachSpeedKnots
            );

        rolloutElapsed = 0f;
        flareDecisionMade = false;

        state = LandingState.Aligning;

        debugStatus =
            "Flying toward the runway approach alignment point.";

        UpdateDebugInformation();
    }

    private void Update()
    {
        switch (state)
        {
            case LandingState.Aligning:
                UpdateAlignment();
                break;

            case LandingState.FinalApproach:
                UpdateFinalApproach();
                break;

            case LandingState.Flare:
                UpdateFlare();
                break;

            case LandingState.Rollout:
                UpdateRollout();
                break;

            case LandingState.GoAround:
                UpdateGoAround();
                break;
        }

        UpdateDebugInformation();
    }


    private bool SelectLandingRunway()
    {
        AircraftRunway[] runways =
            FindObjectsByType<AircraftRunway>(
                FindObjectsSortMode.None
            );

        if (runways.Length == 0)
            return false;

        AircraftRunwayTrafficManager trafficManager =
            FindFirstObjectByType<AircraftRunwayTrafficManager>();

        if (trafficManager == null)
        {
            Debug.LogWarning(
                "AircraftLandingController: " +
                "AircraftRunwayTrafficManager was not found. " +
                "Runway traffic balancing is disabled."
            );
        }

        Vector3 windFrom =
            weather.WindFromDirection;

        windFrom.y = 0f;

        if (windFrom.sqrMagnitude < 0.0001f)
            windFrom = Vector3.forward;

        windFrom.Normalize();

        int bestTrafficCount = int.MaxValue;
        float bestHeadwindKnots = -1f;
        float bestDistance = float.MaxValue;
        bool bestCrosswindWithinLimit = false;

        foreach (AircraftRunway candidate in runways)
        {
            if (candidate == null)
                continue;

            if (!candidate.TryGetGeometry(
                    gridManager,
                    out Vector3 center,
                    out Vector3 axis,
                    out float length,
                    out float surfaceY))
            {
                continue;
            }

            axis.y = 0f;

            if (axis.sqrMagnitude < 0.0001f)
                continue;

            axis.Normalize();

            float alignment =
                Vector3.Dot(axis, windFrom);

            Vector3 candidateDirection =
                alignment >= 0f
                    ? axis
                    : -axis;

            float headwindKnots =
                weather.windSpeedKnots *
                Mathf.Max(
                    0f,
                    Vector3.Dot(
                        candidateDirection,
                        windFrom
                    )
                );

            float sidewindKnots =
                CalculateSidewindComponentKnots(
                    candidateDirection
                );

            bool crosswindWithinLimit =
                sidewindKnots <=
                maximumSidewindComponentKnots + 0.001f;

            int trafficCount =
                trafficManager != null
                    ? trafficManager.GetLandingTrafficCount(
                        candidate,
                        this
                    )
                    : 0;

            Vector3 candidateTouchdown =
                center -
                candidateDirection *
                (length * 0.5f);

            float tangent =
                Mathf.Tan(
                    glideAngleDegrees *
                    Mathf.Deg2Rad
                );

            float minimumApproachDistance =
                flareHeight /
                Mathf.Max(tangent, 0.001f) + 5f;

            float effectiveApproachDistance =
                Mathf.Max(
                    approachDistance,
                    minimumApproachDistance
                );

            Vector3 candidateAlignment =
                candidateTouchdown -
                candidateDirection *
                effectiveApproachDistance;

            candidateAlignment.y =
                surfaceY +
                tangent *
                effectiveApproachDistance;

            float distance =
                Vector3.Distance(
                    transform.position,
                    candidateAlignment
                );

            bool chooseCandidate =
                selectedRunway == null;

            if (!chooseCandidate)
            {
                // Prefer a runway within the permitted
                // crosswind limit when one is available.
                if (crosswindWithinLimit !=
                    bestCrosswindWithinLimit)
                {
                    chooseCandidate =
                        crosswindWithinLimit;
                }
                else if (trafficCount != bestTrafficCount)
                {
                    // Primary selection rule:
                    // use the runway with the least
                    // active landing traffic.
                    chooseCandidate =
                        trafficCount < bestTrafficCount;
                }
                else if (headwindKnots >
                         bestHeadwindKnots + 0.01f)
                {
                    // Tie-breaker 1: stronger headwind.
                    chooseCandidate = true;
                }
                else if (Mathf.Abs(
                             headwindKnots -
                             bestHeadwindKnots
                         ) <= 0.01f &&
                         distance < bestDistance)
                {
                    // Tie-breaker 2: nearer approach.
                    chooseCandidate = true;
                }
            }

            if (!chooseCandidate)
                continue;

            bestCrosswindWithinLimit =
                crosswindWithinLimit;

            bestTrafficCount =
                trafficCount;

            bestHeadwindKnots =
                headwindKnots;

            bestDistance =
                distance;

            selectedRunway =
                candidate;

            runwayCenter =
                center;

            runwayDirection =
                candidateDirection;

            runwayLength =
                length;

            runwaySurfaceY =
                surfaceY;

            touchdownPoint =
                candidateTouchdown;

            alignmentPoint =
                candidateAlignment;

            debugHeadwindComponentKnots =
                headwindKnots;

            debugSidewindComponentKnots =
                sidewindKnots;
        }

        if (selectedRunway == null)
            return false;

        debugSelectedRunway =
            selectedRunway.gameObject.name;

        debugLandingHeading =
            HeadingName(runwayDirection);

        debugStatus =
            $"Selected runway: {debugSelectedRunway}. " +
            $"Landing traffic: {bestTrafficCount}. " +
            $"Headwind: {debugHeadwindComponentKnots:F1} knots. " +
            $"Sidewind: {debugSidewindComponentKnots:F1} knots.";

        Debug.Log(
            $"AIRCRAFT RUNWAY SELECTED | " +
            $"Runway: {debugSelectedRunway} | " +
            $"Heading: {debugLandingHeading} | " +
            $"Landing Traffic: {bestTrafficCount} | " +
            $"Headwind: {debugHeadwindComponentKnots:F1} knots | " +
            $"Sidewind: {debugSidewindComponentKnots:F1} knots"
        );

        return true;
    }

    private void UpdateAlignment()
    {
        Vector3 movement =
            alignmentPoint -
            transform.position;

        if (movement.magnitude <= 0.05f)
        {
            transform.position =
                alignmentPoint;

            currentSpeed =
                KnotsToWorldSpeed(
                    approachSpeedKnots
                );

            state =
                LandingState.FinalApproach;

            debugStatus =
                "Aligned. Starting the 3-degree final approach.";

            return;
        }

        FaceMovementDirection(movement);

        transform.position =
            Vector3.MoveTowards(
                transform.position,
                alignmentPoint,
                currentSpeed * Time.deltaTime
            );
    }

    private void UpdateFinalApproach()
    {
        float remainingDistance =
            GetDistanceToThreshold();

        float flareDistance =
            FlareDistance();

        if (remainingDistance <= flareDistance)
        {
            // Make the crosswind decision exactly once,
            // at the beginning of the flare phase.
            DecideFlareOrGoAround();
            return;
        }

        currentSpeed =
            Mathf.MoveTowards(
                currentSpeed,
                KnotsToWorldSpeed(
                    approachSpeedKnots
                ),
                speedChangeRate *
                Time.deltaTime
            );

        float availableDistance =
            remainingDistance -
            flareDistance;

        float step =
            Mathf.Min(
                currentSpeed * Time.deltaTime,
                availableDistance
            );

        transform.position +=
            runwayDirection * step;

        float newRemainingDistance =
            Mathf.Max(
                0f,
                GetDistanceToThreshold()
            );

        Vector3 position =
            transform.position;

        position.y =
            runwaySurfaceY +
            Mathf.Tan(
                glideAngleDegrees *
                Mathf.Deg2Rad
            ) *
            newRemainingDistance;

        transform.position = position;

        SetFlightOrientation(
            glideAngleDegrees,
            0f
        );

        if (newRemainingDistance <= flareDistance + 0.001f)
        {
            DecideFlareOrGoAround();
        }
    }

    private void DecideFlareOrGoAround()
    {
        if (flareDecisionMade)
            return;

        flareDecisionMade = true;

        debugSidewindComponentKnots =
            CalculateSidewindComponentKnots();

        Debug.Log(
            $"AIRCRAFT FLARE DECISION | " +
            $"Runway: {debugSelectedRunway} | " +
            $"Sidewind: {debugSidewindComponentKnots:F1} knots | " +
            $"Maximum: {maximumSidewindComponentKnots:F1} knots"
        );

        if (debugSidewindComponentKnots >
            maximumSidewindComponentKnots)
        {
            state =
                LandingState.GoAround;

            debugGoAroundTargetAltitude =
                runwaySurfaceY +
                Mathf.Max(
                    goAroundAltitudeAboveRunway,
                    flareHeight + 1f
                );

            debugStatus =
                $"GO-AROUND: Crosswind " +
                $"{debugSidewindComponentKnots:F1} knots exceeds limit " +
                $"{maximumSidewindComponentKnots:F1} knots.";

            Debug.LogWarning(
                $"AIRCRAFT GO-AROUND | " +
                $"Sidewind: {debugSidewindComponentKnots:F1} knots | " +
                $"Limit: {maximumSidewindComponentKnots:F1} knots | " +
                $"Climb attitude: {goAroundNoseUpDegrees:F0} degrees | " +
                $"Target altitude above runway: " +
                $"{debugGoAroundTargetAltitude - runwaySurfaceY:F1}"
            );

            SetFlightOrientation(
                0f,
                goAroundNoseUpDegrees
            );

            return;
        }

        state =
            LandingState.Flare;

        debugStatus =
            "Crosswind within limit. Flare initiated.";
    }

    private float CalculateSidewindComponentKnots()
    {
        return CalculateSidewindComponentKnots(
            runwayDirection
        );
    }

    private float CalculateSidewindComponentKnots(
    Vector3 landingDirection)
    {
        Vector3 windFrom =
            weather.WindFromDirection;

        windFrom.y = 0f;
        landingDirection.y = 0f;

        if (windFrom.sqrMagnitude < 0.0001f ||
            landingDirection.sqrMagnitude < 0.0001f)
        {
            return 0f;
        }

        windFrom.Normalize();
        landingDirection.Normalize();

        float longitudinalAlignment =
            Mathf.Clamp01(
                Mathf.Abs(
                    Vector3.Dot(
                        landingDirection,
                        windFrom
                    )
                )
            );

        float sidewindFraction =
            Mathf.Sqrt(
                Mathf.Max(
                    0f,
                    1f -
                    longitudinalAlignment *
                    longitudinalAlignment
                )
            );

        return weather.windSpeedKnots *
               sidewindFraction;
    }

    private void UpdateFlare()
    {
        float remainingDistance =
            Mathf.Max(
                0f,
                GetDistanceToThreshold()
            );

        if (remainingDistance <= 0.001f)
        {
            TouchDown();
            return;
        }

        currentSpeed =
            Mathf.MoveTowards(
                currentSpeed,
                KnotsToWorldSpeed(
                    touchdownSpeedKnots
                ),
                speedChangeRate *
                Time.deltaTime
            );

        float step =
            Mathf.Min(
                currentSpeed * Time.deltaTime,
                remainingDistance
            );

        transform.position +=
            runwayDirection * step;

        remainingDistance =
            Mathf.Max(
                0f,
                GetDistanceToThreshold()
            );

        float progress =
            Mathf.Clamp01(
                1f -
                remainingDistance /
                FlareDistance()
            );

        float smoothProgress =
            Mathf.SmoothStep(
                0f,
                1f,
                progress
            );

        Vector3 position =
            transform.position;

        position.y =
            runwaySurfaceY +
            flareHeight *
            (1f - smoothProgress);

        transform.position = position;

        SetFlightOrientation(
            glideAngleDegrees *
                (1f - smoothProgress),
            noseRaiseDegrees *
                smoothProgress
        );

        if (remainingDistance <= 0.001f)
        {
            TouchDown();
        }
    }

    private void TouchDown()
    {
        transform.position =
            touchdownPoint;

        currentSpeed =
            KnotsToWorldSpeed(
                touchdownSpeedKnots
            );

        rolloutElapsed = 0f;

        SetFlightOrientation(
            0f,
            noseRaiseDegrees
        );

        state =
            LandingState.Rollout;

        debugStatus =
            "Touchdown. Rolling out and decelerating.";

        Debug.Log(
            $"AIRCRAFT TOUCHDOWN | " +
            $"Runway: {debugSelectedRunway} | " +
            $"Speed: {touchdownSpeedKnots:F1} knots"
        );
    }


    private void UpdateRollout()
    {
        float targetSpeed =
            KnotsToWorldSpeed(
                rolloutTargetSpeedKnots
            );

        float previousSpeed =
            currentSpeed;

        currentSpeed =
            Mathf.MoveTowards(
                currentSpeed,
                targetSpeed,
                rolloutDeceleration *
                Time.deltaTime
            );

        float averageSpeed =
            (previousSpeed + currentSpeed) *
            0.5f;

        transform.position +=
            runwayDirection *
            averageSpeed *
            Time.deltaTime;

        Vector3 position =
            transform.position;

        position.y =
            runwaySurfaceY;

        transform.position =
            position;

        rolloutElapsed +=
            Time.deltaTime;

        float noseLoweringProgress =
            Mathf.Clamp01(
                rolloutElapsed /
                noseLoweringDuration
            );

        float remainingNoseRaise =
            noseRaiseDegrees *
            (1f - noseLoweringProgress);

        SetFlightOrientation(
            0f,
            remainingNoseRaise
        );

        if (currentSpeed > targetSpeed + 0.001f)
            return;

        currentSpeed = targetSpeed;
        state = LandingState.LandingComplete;

        AircraftFlightIdentity flightIdentity =
            GetComponent<AircraftFlightIdentity>();

        if (flightIdentity != null &&
            flightIdentity.IsHobbyFlight)
        {
            HobbyAircraftParkingSpot[] parkingSpots =
                FindObjectsByType<HobbyAircraftParkingSpot>(
                    FindObjectsSortMode.None
                );

            HobbyAircraftParkingSpot reservedSpot = null;

            foreach (HobbyAircraftParkingSpot spot in parkingSpots)
            {
                if (spot == null ||
                    !spot.IsPlacedBuilding ||
                    spot.IsOccupied)
                {
                    continue;
                }

                if (spot.TryReserve(gameObject))
                {
                    reservedSpot = spot;
                    break;
                }
            }

            if (reservedSpot != null)
            {
                debugStatus =
                    $"VFR landing complete. Reserved grass parking: " +
                    $"{reservedSpot.name}. Waiting for taxi routing.";
            }
            else
            {
                debugStatus =
                    "VFR landing complete, but no free placed grass " +
                    "parking spot is available.";

                Debug.LogWarning(
                    $"{flightIdentity.FlightNumber}: Landing completed, " +
                    "but no free hobby parking spot could be reserved.",
                    this
                );
            }
        }
        else if (flightIdentity != null &&
                 GateAssignmentController.Instance != null)
        {
            if (GateAssignmentController.Instance.TryAssignGate(
                    flightIdentity,
                    out AircraftGate assignedGate,
                    out string assignmentMessage))
            {
                debugStatus = assignmentMessage;
            }
            else
            {
                debugStatus = assignmentMessage;
            }
        }
        else
        {
            debugStatus =
                "Landing complete. Gate assignment controller or " +
                "flight identity is missing.";
        }

        Debug.Log(
            $"AIRCRAFT LANDING COMPLETE | " +
            $"Flight: {(flightIdentity != null ? flightIdentity.FlightNumber : "Unknown")} | " +
            $"Runway: {debugSelectedRunway} | " +
            $"Speed: {rolloutTargetSpeedKnots:F1} knots"
        );
    }



    private void UpdateGoAround()
    {
        float targetAltitude =
            Mathf.Max(
                goAroundAltitudeAboveRunway,
                flareHeight + 1f
            );

        float targetY =
            runwaySurfaceY +
            targetAltitude;

        float remainingAltitude =
            targetY -
            transform.position.y;

        if (remainingAltitude <= 0.001f)
        {
            FinishGoAround();
            return;
        }

        // This is a kinematic climb: the aircraft continues
        // along the selected runway direction while gaining
        // altitude at the configured climb angle.
        float climbAngle =
            Mathf.Clamp(
                goAroundNoseUpDegrees,
                1f,
                45f
            );

        float climbTangent =
            Mathf.Tan(
                climbAngle *
                Mathf.Deg2Rad
            );

        float horizontalStep =
            currentSpeed *
            Time.deltaTime;

        float verticalStep =
            horizontalStep *
            climbTangent;

        // Limit the final step so the target altitude
        // is reached exactly instead of being overshot.
        if (verticalStep >= remainingAltitude)
        {
            horizontalStep =
                remainingAltitude /
                climbTangent;

            Vector3 finalPosition =
                transform.position +
                runwayDirection *
                horizontalStep;

            finalPosition.y =
                targetY;

            transform.position =
                finalPosition;

            SetFlightOrientation(
                0f,
                goAroundNoseUpDegrees
            );

            FinishGoAround();
            return;
        }

        Vector3 position =
            transform.position +
            runwayDirection *
            horizontalStep;

        position.y +=
            verticalStep;

        transform.position =
            position;

        SetFlightOrientation(
            0f,
            goAroundNoseUpDegrees
        );
    }

    private void FinishGoAround()
    {
        debugStatus =
            "Go-around altitude reached. Aircraft despawning.";

        Debug.Log(
            $"AIRCRAFT GO-AROUND COMPLETE | " +
            $"Target altitude above runway: " +
            $"{Mathf.Max(goAroundAltitudeAboveRunway, flareHeight + 1f):F1}"
        );

        state =
            LandingState.Failed;

        debugLandingState =
            "GoAroundComplete";

        Destroy(gameObject);
    }

    private float GetDistanceToThreshold()
    {
        Vector3 currentFlat =
            new Vector3(
                transform.position.x,
                0f,
                transform.position.z
            );

        Vector3 thresholdFlat =
            new Vector3(
                touchdownPoint.x,
                0f,
                touchdownPoint.z
            );

        return Vector3.Dot(
            thresholdFlat - currentFlat,
            runwayDirection
        );
    }

    private float FlareDistance()
    {
        return flareHeight /
            Mathf.Max(
                Mathf.Tan(
                    glideAngleDegrees *
                    Mathf.Deg2Rad
                ),
                0.001f
            );
    }

    private float KnotsToWorldSpeed(
        float knots)
    {
        return knots *
            worldUnitsPerSecondPerKnot;
    }

    private void FaceMovementDirection(
        Vector3 movement)
    {
        if (movement.sqrMagnitude < 0.0001f)
            return;

        Quaternion targetRotation =
            Quaternion.LookRotation(
                movement.normalized,
                Vector3.up
            );

        targetRotation *=
            Quaternion.Euler(
                modelRotationOffset
            );

        transform.rotation =
            Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                rotationSpeed *
                Time.deltaTime
            );
    }

    private void SetFlightOrientation(
        float noseDownPathDegrees,
        float noseUpDegrees)
    {
        Vector3 forward =
            new Vector3(
                runwayDirection.x,
                -Mathf.Tan(
                    noseDownPathDegrees *
                    Mathf.Deg2Rad
                ),
                runwayDirection.z
            ).normalized;

        Quaternion rotation =
            Quaternion.LookRotation(
                forward,
                Vector3.up
            );

        rotation *= Quaternion.Euler(
            -noseUpDegrees,
            0f,
            0f
        );

        rotation *= Quaternion.Euler(
            modelRotationOffset
        );

        transform.rotation =
            Quaternion.Slerp(
                transform.rotation,
                rotation,
                rotationSpeed *
                Time.deltaTime
            );
    }

    private string HeadingName(
        Vector3 direction)
    {
        float heading =
            Mathf.Repeat(
                Mathf.Atan2(
                    direction.x,
                    direction.z
                ) *
                Mathf.Rad2Deg,
                360f
            );

        string cardinal;

        if (heading >= 315f ||
            heading < 45f)
        {
            cardinal = "North";
        }
        else if (heading < 135f)
        {
            cardinal = "East";
        }
        else if (heading < 225f)
        {
            cardinal = "South";
        }
        else
        {
            cardinal = "West";
        }

        return $"{cardinal} ({heading:F0}°)";
    }

    private void Fail(string reason)
    {
        state =
            LandingState.Failed;

        debugStatus = reason;

        Debug.LogError(
            $"AIRCRAFT LANDING FAILED | {reason}",
            this
        );

        UpdateDebugInformation();
    }

    private void UpdateDebugInformation()
    {
        debugLandingState =
            state.ToString();

        debugCurrentSpeedKnots =
            worldUnitsPerSecondPerKnot > 0f
                ? currentSpeed /
                  worldUnitsPerSecondPerKnot
                : 0f;

        debugDistanceToThreshold =
            selectedRunway != null
                ? GetDistanceToThreshold()
                : 0f;

        debugGoAroundTargetAltitude =
            selectedRunway != null
                ? runwaySurfaceY +
                  Mathf.Max(
                      goAroundAltitudeAboveRunway,
                      flareHeight + 1f
                  )
                : 0f;
    }
}