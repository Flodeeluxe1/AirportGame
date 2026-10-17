
using System.Collections.Generic;
using UnityEngine;

public class AircraftTaxiTrafficFollowing : MonoBehaviour
{
    [Header("Traffic Layer")]
    [SerializeField] private LayerMask trafficLayer;

    [Header("Detection")]
    [Min(0.1f)]
    [SerializeField] private float trafficDetectionDistance = 8f;

    [Min(0.1f)]
    [SerializeField] private float safeFollowingDistance = 3f;

    [Min(0f)]
    [SerializeField] private float rayOriginHeight = 0.5f;

    [Min(0f)]
    [SerializeField] private float rayOriginForwardOffset = 1f;

    [Header("Speed Adjustment")]
    [Min(0.01f)]
    [SerializeField] private float acceleration = 1.5f;

    [Min(0.01f)]
    [SerializeField] private float braking = 3f;

    [Header("Traffic Debug")]
    public float debugCurrentSpeed;
    public string debugBlockingAircraft = "None";
    public float debugBlockingDistance;
    public string debugLastRayHitCollider = "None";
    public string debugLastRayDirection = "None";
    public float debugLastRayDistance;
    public string debugTrafficDecision = "Not tested";

    [TextArea(4, 12)]
    public string debugRaycastSummary = "No raycast performed yet.";

    private float currentSpeed;

    public float GetTrafficFollowingSpeed(float cruisingSpeed)
    {
        cruisingSpeed = Mathf.Max(0f, cruisingSpeed);

        if (currentSpeed <= 0f)
            currentSpeed = cruisingSpeed;

        float targetSpeed = cruisingSpeed;

        debugBlockingAircraft = "None";
        debugBlockingDistance = 0f;
        debugLastRayHitCollider = "None";
        debugLastRayDirection = "None";
        debugLastRayDistance = 0f;
        debugTrafficDecision = "Path clear";

        List<string> rayReports = new();

        if (TryFindAircraftAhead(
                out Transform blockingAircraft,
                out float distance,
                out _,
                rayReports))
        {
            debugBlockingDistance = distance;
            debugBlockingAircraft = blockingAircraft.name;

            float safeDistance = Mathf.Max(0.1f, safeFollowingDistance);
            float detectionDistance = Mathf.Max(
                safeDistance + 0.1f,
                trafficDetectionDistance
            );

            float speedFactor = Mathf.InverseLerp(
                safeDistance,
                detectionDistance,
                distance
            );

            targetSpeed = cruisingSpeed * speedFactor;

            if (distance <= safeDistance)
            {
                targetSpeed = 0f;
                debugTrafficDecision = "Stopped: aircraft too close";
            }
            else if (targetSpeed < cruisingSpeed * 0.95f)
            {
                debugTrafficDecision = "Slowing to follow aircraft";
            }
            else
            {
                debugTrafficDecision =
                    "Aircraft detected; near cruising speed";
            }

            debugRaycastSummary =
                string.Join("\n", rayReports) +
                $"\nBLOCKING AIRCRAFT: {debugBlockingAircraft}" +
                $"\nBLOCKING DISTANCE: {distance:F2}" +
                $"\nDECISION: {debugTrafficDecision}";
        }
        else
        {
            debugRaycastSummary = rayReports.Count > 0
                ? string.Join("\n", rayReports) +
                  "\nRESULT: No moving aircraft recognized as a blocker."
                : "No colliders hit by any traffic ray.";
        }

        float adjustmentRate =
            targetSpeed < currentSpeed ? braking : acceleration;

        currentSpeed = Mathf.MoveTowards(
            currentSpeed,
            targetSpeed,
            adjustmentRate * Time.deltaTime
        );

        currentSpeed = Mathf.Clamp(currentSpeed, 0f, cruisingSpeed);
        debugCurrentSpeed = currentSpeed;

        return currentSpeed;
    }

    private bool TryFindAircraftAhead(
        out Transform blockingAircraft,
        out float closestDistance,
        out string closestDirection,
        List<string> rayReports)
    {
        blockingAircraft = null;
        closestDistance = float.MaxValue;
        closestDirection = "None";

        Vector3 origin =
            transform.position +
            Vector3.up * rayOriginHeight +
            transform.forward * rayOriginForwardOffset;

        Vector3[] directions =
        {
            transform.forward,
            Quaternion.AngleAxis(-35f, Vector3.up) * transform.forward,
            Quaternion.AngleAxis(35f, Vector3.up) * transform.forward
        };

        string[] directionNames =
        {
            "Forward",
            "Forward-Left",
            "Forward-Right"
        };

        bool foundAircraft = false;

        for (int i = 0; i < directions.Length; i++)
        {
            Ray ray = new Ray(origin, directions[i].normalized);

            RaycastHit[] hits = Physics.RaycastAll(
                ray,
                trafficDetectionDistance,
                trafficLayer,
                QueryTriggerInteraction.Ignore
            );

            System.Array.Sort(
                hits,
                (a, b) => a.distance.CompareTo(b.distance)
            );

            if (hits.Length == 0)
            {
                rayReports.Add($"{directionNames[i]}: no hit");
                continue;
            }

            foreach (RaycastHit hit in hits)
            {
                Collider hitCollider = hit.collider;

                if (hitCollider == null)
                    continue;

                Transform aircraftRoot = hitCollider.transform.root;

                if (aircraftRoot == transform.root)
                {
                    rayReports.Add(
                        $"{directionNames[i]}: ignored own collider " +
                        $"'{hitCollider.name}' at {hit.distance:F2}"
                    );

                    continue;
                }

                bool isMovingAircraft = IsMovingAircraft(
                    hitCollider,
                    out Transform detectedAircraft,
                    out string controllerInfo
                );

                rayReports.Add(
                    $"{directionNames[i]} HIT '{hitCollider.name}' " +
                    $"at {hit.distance:F2} | Layer: " +
                    $"{LayerMask.LayerToName(hitCollider.gameObject.layer)} | " +
                    $"{controllerInfo} | Moving blocker: {isMovingAircraft}"
                );

                if (debugLastRayHitCollider == "None")
                {
                    debugLastRayHitCollider = hitCollider.name;
                    debugLastRayDirection = directionNames[i];
                    debugLastRayDistance = hit.distance;
                }

                if (!isMovingAircraft || detectedAircraft == null)
                    continue;

                if (hit.distance >= closestDistance)
                    continue;

                closestDistance = hit.distance;
                closestDirection = directionNames[i];
                blockingAircraft = detectedAircraft;
                foundAircraft = true;

                // Hits on this ray are sorted nearest-first.
                break;
            }
        }

        return foundAircraft;
    }

    private bool IsMovingAircraft(
        Collider hitCollider,
        out Transform aircraftRoot,
        out string controllerInfo)
    {
        aircraftRoot = null;
        controllerInfo = "No recognized taxi controller found";

        // CargoPax departure taxiing
        AircraftCargoPaxDepartureTaxiController cargoDeparture =
            hitCollider.GetComponentInParent<
                AircraftCargoPaxDepartureTaxiController>();

        if (cargoDeparture != null)
        {
            aircraftRoot = cargoDeparture.transform;
            var state = cargoDeparture.CurrentState;

            bool moving =
                state ==
                    AircraftCargoPaxDepartureTaxiController.DepartureState.TaxiingToHoldingPoint ||
                state ==
                    AircraftCargoPaxDepartureTaxiController.DepartureState.TaxiingToRunwayEntry ||
                state ==
                    AircraftCargoPaxDepartureTaxiController.DepartureState.MovingOntoRunway;

            controllerInfo = $"CargoPax departure state: {state}";
            return moving && aircraftRoot != transform.root;
        }

        // Training departure taxiing
        AircraftTrainingDepartureTaxiController trainingDeparture =
            hitCollider.GetComponentInParent<
                AircraftTrainingDepartureTaxiController>();

        if (trainingDeparture != null)
        {
            aircraftRoot = trainingDeparture.transform;
            var state = trainingDeparture.CurrentState;

            bool moving =
                state ==
                    AircraftTrainingDepartureTaxiController.DepartureState.TaxiingToHoldingPoint ||
                state ==
                    AircraftTrainingDepartureTaxiController.DepartureState.TaxiingToRunwayEntry ||
                state ==
                    AircraftTrainingDepartureTaxiController.DepartureState.MovingOntoRunway;

            controllerInfo = $"Training departure state: {state}";
            return moving && aircraftRoot != transform.root;
        }

        // Hobby departure taxiing
        AircraftHobbyDepartureTaxiController hobbyDeparture =
            hitCollider.GetComponentInParent<
                AircraftHobbyDepartureTaxiController>();

        if (hobbyDeparture != null)
        {
            aircraftRoot = hobbyDeparture.transform;
            var state = hobbyDeparture.CurrentState;

            bool moving =
                state ==
                    AircraftHobbyDepartureTaxiController.DepartureState.TaxiingToHoldingPoint ||
                state ==
                    AircraftHobbyDepartureTaxiController.DepartureState.TaxiingToRunwayEntry ||
                state ==
                    AircraftHobbyDepartureTaxiController.DepartureState.MovingOntoRunway;

            controllerInfo = $"Hobby departure state: {state}";
            return moving && aircraftRoot != transform.root;
        }

        // CargoPax landing taxiing
        AircraftCargoPaxLandingTaxiController cargoLanding =
            hitCollider.GetComponentInParent<
                AircraftCargoPaxLandingTaxiController>();

        if (cargoLanding != null)
        {
            aircraftRoot = cargoLanding.transform;
            bool moving = cargoLanding.IsTaxiing;

            controllerInfo = $"CargoPax landing IsTaxiing: {moving}";
            return moving && aircraftRoot != transform.root;
        }

        // Training landing taxiing
        AircraftTrainingLandingTaxiController trainingLanding =
            hitCollider.GetComponentInParent<
                AircraftTrainingLandingTaxiController>();

        if (trainingLanding != null)
        {
            aircraftRoot = trainingLanding.transform;
            bool moving = trainingLanding.IsTaxiing;

            controllerInfo = $"Training landing IsTaxiing: {moving}";
            return moving && aircraftRoot != transform.root;
        }

        // Hobby landing taxiing
        AircraftHobbyLandingTaxiController hobbyLanding =
            hitCollider.GetComponentInParent<
                AircraftHobbyLandingTaxiController>();

        if (hobbyLanding != null)
        {
            aircraftRoot = hobbyLanding.transform;
            bool moving = hobbyLanding.IsTaxiing;

            controllerInfo = $"Hobby landing IsTaxiing: {moving}";
            return moving && aircraftRoot != transform.root;
        }

        return false;
    }

    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying)
            return;

        Vector3 origin =
            transform.position +
            Vector3.up * rayOriginHeight +
            transform.forward * rayOriginForwardOffset;

        Vector3[] directions =
        {
            transform.forward,
            Quaternion.AngleAxis(-35f, Vector3.up) * transform.forward,
            Quaternion.AngleAxis(35f, Vector3.up) * transform.forward
        };

        Gizmos.color = Color.yellow;

        foreach (Vector3 direction in directions)
        {
            Gizmos.DrawRay(
                origin,
                direction.normalized * trafficDetectionDistance
            );
        }
    }
}
