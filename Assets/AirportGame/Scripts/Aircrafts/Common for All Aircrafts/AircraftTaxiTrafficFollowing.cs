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

    [Header("Traffic Debug (Inspector)")]
    [SerializeField] private float debugCurrentSpeed;
    [SerializeField] private string debugBlockingAircraft = "None";
    [SerializeField] private float debugBlockingDistance;
    [SerializeField] private string debugTrafficDecision = "Not tested";

    [TextArea(2, 5)]
    [SerializeField] private string debugForwardRay = "Not scanned";

    [TextArea(2, 5)]
    [SerializeField] private string debugLeftRay = "Not scanned";

    [TextArea(2, 5)]
    [SerializeField] private string debugRightRay = "Not scanned";

    [TextArea(4, 12)]
    [SerializeField] private string debugRaycastSummary = "No raycast performed yet.";

    private AircraftStatus aircraftStatus;

    private void Awake()
    {
        aircraftStatus = GetComponent<AircraftStatus>();
    }

    public float GetTrafficFollowingSpeed(float cruisingSpeedKnots)
    {
        cruisingSpeedKnots = Mathf.Max(0f, cruisingSpeedKnots);

        if (aircraftStatus == null)
        {
            aircraftStatus = GetComponent<AircraftStatus>();
        }

        if (aircraftStatus == null)
        {
            Debug.LogWarning(
                $"{name}: AircraftStatus is missing. Traffic following cannot manage current speed.",
                this
            );

            return cruisingSpeedKnots;
        }

        float currentSpeedKnots = aircraftStatus.CurrentSpeedKnots;

        // Stop traffic raycasts once the aircraft starts moving onto the runway.
        AircraftCargoPaxDepartureTaxiController departureController =
            GetComponent<AircraftCargoPaxDepartureTaxiController>();

        if (departureController != null &&
            departureController.CurrentState ==
            AircraftCargoPaxDepartureTaxiController.DepartureState.MovingOntoRunway)
        {
            aircraftStatus.SetCurrentSpeedKnots(cruisingSpeedKnots);
            debugCurrentSpeed = cruisingSpeedKnots;

            debugBlockingAircraft = "Raycasts disabled";
            debugBlockingDistance = 0f;
            debugTrafficDecision = "Runway line-up: traffic detection disabled";

            debugForwardRay = "Disabled: aircraft moving onto runway";
            debugLeftRay = "Disabled: aircraft moving onto runway";
            debugRightRay = "Disabled: aircraft moving onto runway";
            debugRaycastSummary = "Traffic raycasts disabled during runway line-up.";

            return cruisingSpeedKnots;
        }

        float targetSpeedKnots = cruisingSpeedKnots;

        debugBlockingAircraft = "None";
        debugBlockingDistance = 0f;
        debugTrafficDecision = "Path clear";

        if (TryFindAircraftAhead(out Transform blockingAircraft, out float distance))
        {
            debugBlockingAircraft = blockingAircraft.name;
            debugBlockingDistance = distance;

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

            targetSpeedKnots = cruisingSpeedKnots * speedFactor;

            if (distance <= safeDistance)
            {
                targetSpeedKnots = 0f;
                debugTrafficDecision = "Stopped: aircraft too close";
            }
            else if (targetSpeedKnots < cruisingSpeedKnots * 0.95f)
            {
                debugTrafficDecision = "Slowing to follow aircraft";
            }
            else
            {
                debugTrafficDecision = "Aircraft detected; near cruising speed";
            }
        }

        // The Inspector acceleration and braking values are still
        // expressed in world units per second squared.
        // Convert those rates to knots per second.
        float adjustmentRate = targetSpeedKnots < currentSpeedKnots
            ? braking
            : acceleration;

        float adjustmentRateKnots =
            adjustmentRate / AircraftStatus.WorldUnitsPerSecondPerKnot;

        currentSpeedKnots = Mathf.MoveTowards(
            currentSpeedKnots,
            targetSpeedKnots,
            adjustmentRateKnots * Time.deltaTime
        );

        currentSpeedKnots = Mathf.Clamp(
            currentSpeedKnots,
            0f,
            cruisingSpeedKnots
        );

        aircraftStatus.SetCurrentSpeedKnots(currentSpeedKnots);

        debugCurrentSpeed = currentSpeedKnots;

        return currentSpeedKnots;
    }

    private bool TryFindAircraftAhead(
        out Transform blockingAircraft,
        out float closestDistance)
    {
        blockingAircraft = null;
        closestDistance = float.MaxValue;

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

        string[] rayResults = new string[directions.Length];
        bool foundAircraft = false;
        List<string> scanSummary = new List<string>();

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

            System.Text.StringBuilder rayReport =
                new System.Text.StringBuilder();

            rayReport.AppendLine(
                $"{directionNames[i]} | Range: {trafficDetectionDistance:F2}"
            );

            if (hits.Length == 0)
            {
                rayReport.AppendLine("No colliders hit.");
                rayResults[i] = rayReport.ToString();
                scanSummary.Add($"{directionNames[i]}: no hits");
                continue;
            }

            bool reportedHit = false;

            foreach (RaycastHit hit in hits)
            {
                Collider hitCollider = hit.collider;

                if (hitCollider == null)
                    continue;

                Transform colliderRoot = hitCollider.transform.root;

                if (colliderRoot == transform.root)
                    continue;

                reportedHit = true;

                bool isAircraft = IsMovingAircraft(
                    hitCollider,
                    out Transform detectedAircraft
                );

                string layerName =
                    LayerMask.LayerToName(hitCollider.gameObject.layer);

                string hitDescription =
                    $"{hitCollider.name} | {hit.distance:F2} units | " +
                    $"Layer: {layerName} | " +
                    (isAircraft
                        ? $"BLOCKING: {detectedAircraft.name}"
                        : "Not blocking");

                rayReport.AppendLine(hitDescription);
                scanSummary.Add($"{directionNames[i]}: {hitDescription}");

                if (!isAircraft || detectedAircraft == null)
                    continue;

                if (hit.distance >= closestDistance)
                    continue;

                closestDistance = hit.distance;
                blockingAircraft = detectedAircraft;
                foundAircraft = true;

                // Hits are sorted nearest-first.
                break;
            }

            if (!reportedHit)
                rayReport.AppendLine("No other colliders hit.");

            rayResults[i] = rayReport.ToString();
        }

        debugForwardRay = rayResults[0];
        debugLeftRay = rayResults[1];
        debugRightRay = rayResults[2];

        debugRaycastSummary = string.Join("\n", scanSummary);

        return foundAircraft;
    }

    private bool IsMovingAircraft(
        Collider hitCollider,
        out Transform aircraftRoot)
    {
        aircraftRoot = null;

        if (hitCollider == null)
            return false;

        AircraftStatus status =
            hitCollider.GetComponentInParent<AircraftStatus>();

        if (status == null)
            return false;

        aircraftRoot = status.transform;

        if (aircraftRoot.root == transform.root)
            return false;

        // Parked aircraft must not block taxi traffic.
        if (status.IsParked)
            return false;

        // Ignore aircraft that are actively taking off.
        if (IsAircraftTakingOff(aircraftRoot))
            return false;

        // Other aircraft, including stationary aircraft on a taxiway,
        // are treated as traffic obstacles.
        return true;
    }

    private bool IsAircraftTakingOff(Transform aircraftRoot)
    {
        if (aircraftRoot == null)
            return false;

        AircraftCargoPaxTakeoffController cargoPax =
            aircraftRoot.GetComponentInChildren<AircraftCargoPaxTakeoffController>(true);

        if (cargoPax != null && cargoPax.IsTakingOff)
            return true;

        AircraftHobbyTakeoffController hobby =
            aircraftRoot.GetComponentInChildren<AircraftHobbyTakeoffController>(true);

        if (hobby != null && hobby.IsTakingOff)
            return true;

        AircraftSSTakeoffController sightseeing =
            aircraftRoot.GetComponentInChildren<AircraftSSTakeoffController>(true);

        if (sightseeing != null && sightseeing.IsTakingOff)
            return true;

        AircraftTrainingTakeoffController training =
            aircraftRoot.GetComponentInChildren<AircraftTrainingTakeoffController>(true);

        if (training != null && training.IsTakingOff)
            return true;

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