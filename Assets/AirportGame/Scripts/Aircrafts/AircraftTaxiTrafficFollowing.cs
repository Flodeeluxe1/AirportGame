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

    private AircraftDepartureTaxiController ownDepartureController;
    private AircraftTaxiController ownArrivalController;

    private void Awake()
    {
        ownDepartureController =
            GetComponent<AircraftDepartureTaxiController>();

        ownArrivalController =
            GetComponent<AircraftTaxiController>();
    }

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
                out AircraftDepartureTaxiController blockingDeparture,
                out AircraftTaxiController blockingArrival,
                out float distance,
                out string direction,
                rayReports))
        {
            debugBlockingDistance = distance;
            debugBlockingAircraft = blockingDeparture != null
                ? blockingDeparture.name
                : blockingArrival.name;

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
                debugTrafficDecision = "Aircraft detected; near cruising speed";
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
        out AircraftDepartureTaxiController blockingDeparture,
        out AircraftTaxiController blockingArrival,
        out float closestDistance,
        out string closestDirection,
        List<string> rayReports)
    {
        blockingDeparture = null;
        blockingArrival = null;
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
                rayReports.Add(
                    $"{directionNames[i]}: no hit"
                );

                continue;
            }

            foreach (RaycastHit hit in hits)
            {
                Collider hitCollider = hit.collider;

                if (hitCollider == null)
                    continue;

                if (hitCollider.transform.root == transform.root)
                {
                    rayReports.Add(
                        $"{directionNames[i]}: ignored own collider " +
                        $"'{hitCollider.name}' at {hit.distance:F2}"
                    );

                    continue;
                }

                AircraftDepartureTaxiController departure =
                    hitCollider.GetComponentInParent<
                        AircraftDepartureTaxiController>();

                AircraftTaxiController arrival =
                    hitCollider.GetComponentInParent<
                        AircraftTaxiController>();

                string controllerInfo = "No aircraft controller found";
                bool isMovingAircraft = false;

                if (departure != null)
                {
                    controllerInfo =
                        $"Departure state: {departure.CurrentState}";

                    isMovingAircraft =
                        departure != ownDepartureController &&
                        (
                            departure.CurrentState ==
                                AircraftDepartureTaxiController.DepartureState.TaxiingToHoldingPoint ||
                            departure.CurrentState ==
                                AircraftDepartureTaxiController.DepartureState.TaxiingToRunwayEntry ||
                            departure.CurrentState ==
                                AircraftDepartureTaxiController.DepartureState.MovingOntoRunway
                        );
                }
                else if (arrival != null)
                {
                    controllerInfo =
                        $"Arrival IsTaxiing: {arrival.IsTaxiing}";

                    isMovingAircraft =
                        arrival != ownArrivalController &&
                        arrival.IsTaxiing;
                }

                rayReports.Add(
                    $"{directionNames[i]} HIT '{hitCollider.name}' " +
                    $"at {hit.distance:F2} | Layer: " +
                    $"{LayerMask.LayerToName(hitCollider.gameObject.layer)} | " +
                    $"{controllerInfo} | Moving blocker: {isMovingAircraft}"
                );

                // Record the first actual ray hit for debugging,
                // even if it isn't a moving aircraft.
                if (debugLastRayHitCollider == "None")
                {
                    debugLastRayHitCollider = hitCollider.name;
                    debugLastRayDirection = directionNames[i];
                    debugLastRayDistance = hit.distance;
                }

                if (!isMovingAircraft)
                    continue;

                if (hit.distance >= closestDistance)
                    continue;

                closestDistance = hit.distance;
                closestDirection = directionNames[i];
                blockingDeparture = departure;
                blockingArrival = arrival;
                foundAircraft = true;

                // Hits are sorted nearest-first for this ray.
                // Continue other rays to find the closest blocker overall.
                break;
            }
        }

        return foundAircraft;
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