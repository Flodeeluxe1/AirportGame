
using UnityEngine;

/// <summary>
/// Sightseeing aircraft landing controller.
/// Uses the airport's existing runway, weather and runway-traffic systems.
/// Does not depend on hobby-aircraft parking.
/// </summary>
[DisallowMultipleComponent]
public class AircraftSSLandingController : MonoBehaviour
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
    [SerializeField] private float touchdownOffset = 7f;

    [Header("Speeds")]
    [Min(1f)]
    [SerializeField] private float approachSpeedKnots = 90f;
    [Min(1f)]
    [SerializeField] private float touchdownSpeedKnots = 70f;
    [Min(1f)]
    [SerializeField] private float rolloutTargetSpeedKnots = 15f;
    [Min(0.001f)]
    [SerializeField] private float worldUnitsPerSecondPerKnot = 0.15f;
    [Min(0.1f)]
    [SerializeField] private float speedChangeRate = 3f;
    [Min(0.1f)]
    [SerializeField] private float rolloutDeceleration = 1f;

    [Header("Flare")]
    [Min(0.1f)]
    [SerializeField] private float flareHeight = 1.5f;
    [Range(0f, 10f)]
    [SerializeField] private float noseRaiseDegrees = 7f;
    [Min(0.1f)]
    [SerializeField] private float noseLoweringDuration = 1.5f;

    [Header("Crosswind")]
    [Min(0f)]
    [SerializeField] private float maximumSidewindComponentKnots = 15f;
    [Range(1f, 45f)]
    [SerializeField] private float goAroundNoseUpDegrees = 13f;
    [Min(1f)]
    [SerializeField] private float goAroundAltitudeAboveRunway = 30f;

    [Header("Steering")]
    [Min(0.1f)]
    [SerializeField] private float rotationSpeed = 2f;
    [SerializeField] private Vector3 modelRotationOffset;

    [Header("Debug")]
    public string debugLandingState = "Initializing";
    public string debugSelectedRunway = "None";
    public string debugLandingHeading = "None";
    public string debugStatus = "Waiting for initialization";
    public float debugHeadwindComponentKnots;
    public float debugSidewindComponentKnots;
    public float debugCurrentSpeedKnots;
    public float debugDistanceToThreshold;
    public float debugGoAroundTargetAltitude;

    public bool IsLandingComplete =>
        state == LandingState.LandingComplete;

    public bool IsGoingAround =>
        state == LandingState.GoAround;

    public AircraftRunway SelectedRunway => selectedRunway;
    public Vector3 LandingDirection => runwayDirection;

    public bool IsLandingTrafficActive =>
        selectedRunway != null &&
        (state == LandingState.Aligning ||
         state == LandingState.FinalApproach ||
         state == LandingState.Flare ||
         state == LandingState.Rollout ||
         state == LandingState.GoAround);

    private LandingState state = LandingState.Initializing;
    private AircraftRunway selectedRunway;
    private AircraftRunwayTrafficManager trafficManager;

    private Vector3 runwayDirection;
    private Vector3 touchdownPoint;
    private Vector3 alignmentPoint;

    private float runwaySurfaceY;
    private float currentSpeed;
    private float rolloutElapsed;
    private bool flareDecisionMade;

    private void Start()
    {
        if (gridManager == null)
            gridManager = FindFirstObjectByType<GridManager>();

        if (weather == null)
            weather = FindFirstObjectByType<Weather>();

        if (gridManager == null || weather == null)
        {
            Fail("GridManager or Weather is missing.");
            return;
        }

        if (!SelectLandingRunway())
        {
            Fail("No usable runway with AircraftRunway was found.");
            return;
        }

        currentSpeed = KnotsToWorldSpeed(approachSpeedKnots);
        rolloutElapsed = 0f;
        flareDecisionMade = false;
        state = LandingState.Aligning;

        debugStatus = "Flying toward the runway approach alignment point.";
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

        AircraftStatus status = GetComponent<AircraftStatus>();
        if (status != null && IsLandingComplete)
            status.MarkLandingComplete();

        if (trafficManager == null)
            trafficManager = FindFirstObjectByType<AircraftRunwayTrafficManager>();

        if (trafficManager != null)
        {
            if (selectedRunway != null)
            {
                trafficManager.UpdateLandingTraffic(
                    this, selectedRunway, IsLandingTrafficActive);
            }
            else
            {
                trafficManager.UnregisterLandingTraffic(this);
            }
        }

        UpdateDebugInformation();
    }

    private bool SelectLandingRunway()
    {
        AircraftRunway[] runways =
            FindObjectsByType<AircraftRunway>(FindObjectsSortMode.None);

        if (runways.Length == 0)
            return false;

        trafficManager =
            FindFirstObjectByType<AircraftRunwayTrafficManager>();

        Vector3 windFrom = weather.WindFromDirection;
        windFrom.y = 0f;

        if (windFrom.sqrMagnitude < 0.0001f)
            windFrom = Vector3.forward;

        windFrom.Normalize();

        int bestTraffic = int.MaxValue;
        float bestHeadwind = -1f;
        float bestDistance = float.MaxValue;
        bool bestCrosswindOkay = false;

        foreach (AircraftRunway candidate in runways)
        {
            if (candidate == null ||
                !candidate.TryGetGeometry(
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

            Vector3 direction =
                Vector3.Dot(axis, windFrom) >= 0f ? axis : -axis;

            float headwind =
                weather.windSpeedKnots *
                Mathf.Max(0f, Vector3.Dot(direction, windFrom));

            float crosswind = CalculateSidewindComponentKnots(direction);
            bool crosswindOkay =
                crosswind <= maximumSidewindComponentKnots + 0.001f;

            int traffic = trafficManager != null
                ? trafficManager.GetLandingTrafficCount(candidate, this)
                : 0;

            Vector3 touchdown = center - direction * (length * 0.5f) + direction * touchdownOffset;

            float tangent = Mathf.Tan(glideAngleDegrees * Mathf.Deg2Rad);
            float effectiveDistance = Mathf.Max(
                approachDistance,
                flareHeight / Mathf.Max(tangent, 0.001f) + 5f);

            Vector3 alignment =
                touchdown - direction * effectiveDistance;
            alignment.y = surfaceY + tangent * effectiveDistance;

            float distance = Vector3.Distance(transform.position, alignment);

            bool choose = selectedRunway == null;

            if (!choose)
            {
                if (crosswindOkay != bestCrosswindOkay)
                    choose = crosswindOkay;
                else if (traffic != bestTraffic)
                    choose = traffic < bestTraffic;
                else if (headwind > bestHeadwind + 0.01f)
                    choose = true;
                else if (Mathf.Abs(headwind - bestHeadwind) <= 0.01f &&
                         distance < bestDistance)
                    choose = true;
            }

            if (!choose)
                continue;

            selectedRunway = candidate;
            runwayDirection = direction;
            touchdownPoint = touchdown;
            alignmentPoint = alignment;
            runwaySurfaceY = surfaceY;

            bestTraffic = traffic;
            bestHeadwind = headwind;
            bestDistance = distance;
            bestCrosswindOkay = crosswindOkay;

            debugHeadwindComponentKnots = headwind;
            debugSidewindComponentKnots = crosswind;
        }

        if (selectedRunway == null)
            return false;

        debugSelectedRunway = selectedRunway.gameObject.name;
        debugLandingHeading = HeadingName(runwayDirection);

        debugStatus =
            $"Selected runway {debugSelectedRunway}; " +
            $"landing traffic {bestTraffic}; " +
            $"headwind {debugHeadwindComponentKnots:F1} knots.";

        Debug.Log(
            $"SIGHTSEEING RUNWAY SELECTED | {debugSelectedRunway} | " +
            $"Heading: {debugLandingHeading} | Traffic: {bestTraffic}",
            this);

        return true;
    }

    private void UpdateAlignment()
    {
        Vector3 movement = alignmentPoint - transform.position;

        if (movement.magnitude <= 0.05f)
        {
            transform.position = alignmentPoint;
            currentSpeed = KnotsToWorldSpeed(approachSpeedKnots);
            state = LandingState.FinalApproach;
            debugStatus = "Aligned with runway. Starting final approach.";
            return;
        }

        FaceMovementDirection(movement);
        transform.position = Vector3.MoveTowards(
            transform.position,
            alignmentPoint,
            currentSpeed * Time.deltaTime);
    }

    private void UpdateFinalApproach()
    {
        float remaining = GetDistanceToThreshold();
        float flareDistance = FlareDistance();

        if (remaining <= flareDistance)
        {
            DecideFlareOrGoAround();
            return;
        }

        currentSpeed = Mathf.MoveTowards(
            currentSpeed,
            KnotsToWorldSpeed(approachSpeedKnots),
            speedChangeRate * Time.deltaTime);

        float step = Mathf.Min(
            currentSpeed * Time.deltaTime,
            remaining - flareDistance);

        transform.position += runwayDirection * step;

        remaining = Mathf.Max(0f, GetDistanceToThreshold());

        Vector3 position = transform.position;
        position.y = runwaySurfaceY +
            Mathf.Tan(glideAngleDegrees * Mathf.Deg2Rad) * remaining;
        transform.position = position;

        SetFlightOrientation(glideAngleDegrees, 0f);

        if (remaining <= flareDistance + 0.001f)
            DecideFlareOrGoAround();
    }

    private void DecideFlareOrGoAround()
    {
        if (flareDecisionMade)
            return;

        flareDecisionMade = true;
        debugSidewindComponentKnots =
            CalculateSidewindComponentKnots(runwayDirection);

        if (debugSidewindComponentKnots > maximumSidewindComponentKnots)
        {
            state = LandingState.GoAround;
            debugStatus = "Crosswind exceeds limit. Performing go-around.";

            Debug.LogWarning(
                $"SIGHTSEEING GO-AROUND | Crosswind " +
                $"{debugSidewindComponentKnots:F1} knots exceeds " +
                $"{maximumSidewindComponentKnots:F1} knots.",
                this);

            return;
        }

        state = LandingState.Flare;
        debugStatus = "Crosswind within limit. Flare initiated.";
    }

    private void UpdateFlare()
    {
        float remaining = Mathf.Max(0f, GetDistanceToThreshold());

        if (remaining <= 0.001f)
        {
            TouchDown();
            return;
        }

        currentSpeed = Mathf.MoveTowards(
            currentSpeed,
            KnotsToWorldSpeed(touchdownSpeedKnots),
            speedChangeRate * Time.deltaTime);

        transform.position += runwayDirection *
            Mathf.Min(currentSpeed * Time.deltaTime, remaining);

        remaining = Mathf.Max(0f, GetDistanceToThreshold());

        float progress = Mathf.Clamp01(1f - remaining / FlareDistance());
        float smooth = Mathf.SmoothStep(0f, 1f, progress);

        Vector3 position = transform.position;
        position.y = runwaySurfaceY + flareHeight * (1f - smooth);
        transform.position = position;

        SetFlightOrientation(
            glideAngleDegrees * (1f - smooth),
            noseRaiseDegrees * smooth);

        if (remaining <= 0.001f)
            TouchDown();
    }

    private void TouchDown()
    {
        transform.position = touchdownPoint;
        currentSpeed = KnotsToWorldSpeed(touchdownSpeedKnots);
        rolloutElapsed = 0f;

        SetFlightOrientation(0f, noseRaiseDegrees);
        state = LandingState.Rollout;
        debugStatus = "Touchdown. Decelerating on runway.";

        Debug.Log(
            $"SIGHTSEEING TOUCHDOWN | Runway: {debugSelectedRunway}",
            this);
    }

    private void UpdateRollout()
    {
        float targetSpeed = KnotsToWorldSpeed(rolloutTargetSpeedKnots);

        float previousSpeed = currentSpeed;
        currentSpeed = Mathf.MoveTowards(
            currentSpeed,
            targetSpeed,
            rolloutDeceleration * Time.deltaTime);

        float averageSpeed = (previousSpeed + currentSpeed) * 0.5f;
        transform.position += runwayDirection * averageSpeed * Time.deltaTime;

        Vector3 position = transform.position;
        position.y = runwaySurfaceY;
        transform.position = position;

        rolloutElapsed += Time.deltaTime;

        float lowering = Mathf.Clamp01(
            rolloutElapsed / noseLoweringDuration);

        SetFlightOrientation(
            0f, noseRaiseDegrees * (1f - lowering));

        if (currentSpeed > targetSpeed + 0.001f)
            return;

        currentSpeed = targetSpeed;
        state = LandingState.LandingComplete;
        debugStatus =
            "Landing complete. Waiting for taxi to sightseeing parking.";

        AircraftFlightIdentity identity =
            GetComponent<AircraftFlightIdentity>();

        Debug.Log(
            $"SIGHTSEEING LANDING COMPLETE | Flight: " +
            $"{(identity != null ? identity.FlightNumber : "Unknown")} | " +
            $"Runway: {debugSelectedRunway}",
            this);
    }

    private void UpdateGoAround()
    {
        float targetY = runwaySurfaceY +
            Mathf.Max(goAroundAltitudeAboveRunway, flareHeight + 1f);

        float remainingAltitude = targetY - transform.position.y;

        if (remainingAltitude <= 0.001f)
        {
            state = LandingState.Failed;
            debugStatus = "Go-around altitude reached. Aircraft despawning.";
            Destroy(gameObject);
            return;
        }

        float climbAngle = Mathf.Clamp(goAroundNoseUpDegrees, 1f, 45f);
        float tangent = Mathf.Tan(climbAngle * Mathf.Deg2Rad);
        float horizontalStep = currentSpeed * Time.deltaTime;
        float verticalStep = horizontalStep * tangent;

        if (verticalStep >= remainingAltitude)
        {
            horizontalStep = remainingAltitude / tangent;
            Vector3 finalPosition =
                transform.position + runwayDirection * horizontalStep;
            finalPosition.y = targetY;
            transform.position = finalPosition;
            state = LandingState.Failed;
            Destroy(gameObject);
            return;
        }

        Vector3 position =
            transform.position + runwayDirection * horizontalStep;
        position.y += verticalStep;
        transform.position = position;

        SetFlightOrientation(0f, goAroundNoseUpDegrees);
    }

    private float CalculateSidewindComponentKnots(Vector3 direction)
    {
        Vector3 windFrom = weather.WindFromDirection;
        windFrom.y = 0f;
        direction.y = 0f;

        if (windFrom.sqrMagnitude < 0.0001f ||
            direction.sqrMagnitude < 0.0001f)
            return 0f;

        windFrom.Normalize();
        direction.Normalize();

        float alignment = Mathf.Clamp01(
            Mathf.Abs(Vector3.Dot(direction, windFrom)));

        float sidewindFraction = Mathf.Sqrt(
            Mathf.Max(0f, 1f - alignment * alignment));

        return weather.windSpeedKnots * sidewindFraction;
    }

    private float GetDistanceToThreshold()
    {
        Vector3 current = new Vector3(
            transform.position.x, 0f, transform.position.z);

        Vector3 threshold = new Vector3(
            touchdownPoint.x, 0f, touchdownPoint.z);

        return Vector3.Dot(threshold - current, runwayDirection);
    }

    private float FlareDistance()
    {
        return flareHeight /
            Mathf.Max(
                Mathf.Tan(glideAngleDegrees * Mathf.Deg2Rad),
                0.001f);
    }

    private float KnotsToWorldSpeed(float knots)
    {
        return knots * worldUnitsPerSecondPerKnot;
    }

    private void FaceMovementDirection(Vector3 movement)
    {
        if (movement.sqrMagnitude < 0.0001f)
            return;

        Quaternion target = Quaternion.LookRotation(
            movement.normalized, Vector3.up);

        target *= Quaternion.Euler(modelRotationOffset);

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            target,
            rotationSpeed * Time.deltaTime);
    }

    private void SetFlightOrientation(float noseDownDegrees, float noseUpDegrees)
    {
        Vector3 forward = new Vector3(
            runwayDirection.x,
            -Mathf.Tan(noseDownDegrees * Mathf.Deg2Rad),
            runwayDirection.z).normalized;

        Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
        rotation *= Quaternion.Euler(-noseUpDegrees, 0f, 0f);
        rotation *= Quaternion.Euler(modelRotationOffset);

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            rotation,
            rotationSpeed * Time.deltaTime);
    }

    private string HeadingName(Vector3 direction)
    {
        float heading = Mathf.Repeat(
            Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg,
            360f);

        string cardinal =
            heading >= 315f || heading < 45f ? "North" :
            heading < 135f ? "East" :
            heading < 225f ? "South" : "West";

        return $"{cardinal} ({heading:F0}°)";
    }

    private void Fail(string reason)
    {
        state = LandingState.Failed;
        debugStatus = reason;

        Debug.LogError(
            $"SIGHTSEEING LANDING FAILED | {reason}",
            this);
    }

    private void UpdateDebugInformation()
    {
        debugLandingState = state.ToString();

        debugCurrentSpeedKnots = worldUnitsPerSecondPerKnot > 0f
            ? currentSpeed / worldUnitsPerSecondPerKnot
            : 0f;

        debugDistanceToThreshold = selectedRunway != null
            ? GetDistanceToThreshold()
            : 0f;

        debugGoAroundTargetAltitude = selectedRunway != null
            ? runwaySurfaceY +
              Mathf.Max(goAroundAltitudeAboveRunway, flareHeight + 1f)
            : 0f;
    }

    private void OnDestroy()
    {
        if (trafficManager != null)
            trafficManager.UnregisterLandingTraffic(this);
    }
}
