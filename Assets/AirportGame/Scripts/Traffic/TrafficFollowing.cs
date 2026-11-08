using System;
using UnityEngine;

/// <summary>
/// Shared multi-ray traffic sensing and speed control.
/// Vehicle controllers provide their own collider filtering and movement.
/// </summary>
[DisallowMultipleComponent]
public sealed class TrafficFollowing : MonoBehaviour
{
    [Header("Sensor")]
    [SerializeField] private LayerMask trafficLayer;
    [Min(0.1f)] [SerializeField] private float detectionDistance = 3f;
    [Min(0.01f)] [SerializeField] private float safeFollowingDistance = 0.7f;
    [SerializeField] private float rayOriginHeight = 0.15f;
    [SerializeField] private float rayOriginForwardOffset = 0.3f;

    [Header("Speed Control")]
    [Min(0f)] [SerializeField] private float acceleration = 4f;
    [Min(0f)] [SerializeField] private float braking = 8f;

    public string LastRayHitCollider { get; private set; } = "None";
    public string LastRayDirection { get; private set; } = "None";
    public float LastRayDistance { get; private set; }
    public string RaycastSummary { get; private set; } = "Not tested";

    [Header("Debug - Raycast Information")]
    [SerializeField, TextArea(5, 12)]
    private string debugRaycastSummary = "Not tested";

    private static readonly float[] Angles = { -60f, -30f, 0f, 30f, 60f };
    private static readonly string[] Names = { "Left 60", "Left 30", "Forward", "Right 30", "Right 60" };

    public float BrakeToStop(float currentSpeed)
    {
        return Mathf.MoveTowards(currentSpeed, 0f, braking * Time.deltaTime);
    }

    public float GetFollowingSpeed(
        float desiredSpeed,
        float currentSpeed,
        Func<Collider, bool> shouldBlock,
        bool skipRaycasts,
        out RaycastHit blockingHit)
    {
        blockingHit = default;
        LastRayHitCollider = "None";
        LastRayDirection = "None";
        LastRayDistance = 0f;
        RaycastSummary = "";

        if (skipRaycasts)
        {
            RaycastSummary = "Traffic raycasts skipped: intersection reservation held.";
            debugRaycastSummary = RaycastSummary;
            return Mathf.MoveTowards(currentSpeed, desiredSpeed, acceleration * Time.deltaTime);
        }

        Vector3 origin = transform.position +
                         Vector3.up * rayOriginHeight +
                         transform.forward * rayOriginForwardOffset;

        float closestDistance = float.MaxValue;
        bool found = false;

        for (int i = 0; i < Angles.Length; i++)
        {
            Vector3 direction = Quaternion.Euler(0f, Angles[i], 0f) * transform.forward;

            if (!Physics.Raycast(origin, direction, out RaycastHit hit,
                    detectionDistance, trafficLayer, QueryTriggerInteraction.Ignore))
            {
                RaycastSummary += Names[i] + ": no hit\n";
                continue;
            }

            LastRayHitCollider = hit.collider.name;
            LastRayDirection = Names[i];
            LastRayDistance = hit.distance;

            bool blocks = shouldBlock != null && shouldBlock(hit.collider);
            RaycastSummary += $"{Names[i]}: {hit.collider.name} ({hit.distance:F2}m)" +
                              (blocks ? " — blocking\n" : " — ignored\n");

            if (blocks && hit.distance < closestDistance)
            {
                closestDistance = hit.distance;
                blockingHit = hit;
                found = true;
            }
        }

        debugRaycastSummary = string.IsNullOrEmpty(RaycastSummary) ? "No raycast results." : RaycastSummary;

        float targetSpeed = desiredSpeed;
        if (found)
        {
            float safeDistance = Mathf.Max(0.01f, safeFollowingDistance);
            float sensorDistance = Mathf.Max(detectionDistance, safeDistance + 0.01f);
            float normalizedDistance = Mathf.InverseLerp(safeDistance, sensorDistance, closestDistance);
            targetSpeed = Mathf.Lerp(0f, desiredSpeed, normalizedDistance);
        }

        float rate = targetSpeed < currentSpeed ? braking : acceleration;
        return Mathf.MoveTowards(currentSpeed, targetSpeed, rate * Time.deltaTime);
    }

    private void OnDrawGizmos()
    {
        Vector3 origin = transform.position +
                         Vector3.up * rayOriginHeight +
                         transform.forward * rayOriginForwardOffset;

        for (int i = 0; i < Angles.Length; i++)
        {
            Vector3 direction = Quaternion.Euler(0f, Angles[i], 0f) * transform.forward;
            float length = detectionDistance;
            Color color = i == 2 ? Color.green : Color.yellow;

            if (Physics.Raycast(origin, direction, out RaycastHit hit,
                    detectionDistance, trafficLayer, QueryTriggerInteraction.Ignore))
            {
                length = hit.distance;
                color = Color.red;
            }

            Gizmos.color = color;
            Gizmos.DrawLine(origin, origin + direction.normalized * length);
            Gizmos.DrawWireSphere(origin + direction.normalized * length, 0.06f);
        }
    }
}
