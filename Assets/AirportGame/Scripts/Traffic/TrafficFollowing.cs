using System;
using System.Text;
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
    [Tooltip("How often angled traffic sensors refresh; the forward sensor remains per-frame.")]
    [Min(0.02f)] [SerializeField] private float sideSensorInterval = 0.1f;
    [SerializeField] private bool captureRaycastDebugInfo;

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
    private float sideSensorTimer;
    private bool sideSensorsInitialized;
    private RaycastHit cachedSideBlockingHit;
    private bool cachedSideBlocking;

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

        if (skipRaycasts)
        {
            RaycastSummary = "Traffic raycasts skipped: intersection reservation held.";
            if (captureRaycastDebugInfo) debugRaycastSummary = RaycastSummary;
            return Mathf.MoveTowards(currentSpeed, desiredSpeed, acceleration * Time.deltaTime);
        }

        Vector3 origin = transform.position +
                         Vector3.up * rayOriginHeight +
                         transform.forward * rayOriginForwardOffset;
        float closestDistance = float.MaxValue;
        bool found = false;
        StringBuilder summary = captureRaycastDebugInfo ? new StringBuilder(160) : null;

        // Keep the forward sensor immediate for safe braking.
        if (Physics.Raycast(origin, transform.forward, out RaycastHit forwardHit,
                detectionDistance, trafficLayer, QueryTriggerInteraction.Ignore))
        {
            LastRayHitCollider = forwardHit.collider.name;
            LastRayDirection = Names[2];
            LastRayDistance = forwardHit.distance;
            bool blocks = shouldBlock != null && shouldBlock(forwardHit.collider);
            if (summary != null)
                summary.Append(Names[2]).Append(": ").Append(forwardHit.collider.name)
                    .Append(" (").Append(forwardHit.distance.ToString("F2"))
                    .Append("m)").Append(blocks ? " — blocking\n" : " — ignored\n");
            if (blocks)
            {
                closestDistance = forwardHit.distance;
                blockingHit = forwardHit;
                found = true;
            }
        }
        else if (summary != null) summary.Append(Names[2]).Append(": no hit\n");

        // Refresh angled sensors less often, retaining the latest blocking result.
        sideSensorTimer += Time.unscaledDeltaTime;
        if (!sideSensorsInitialized || sideSensorTimer >= Mathf.Max(0.02f, sideSensorInterval))
        {
            sideSensorTimer = 0f;
            sideSensorsInitialized = true;
            cachedSideBlocking = false;
            cachedSideBlockingHit = default;
            for (int i = 0; i < Angles.Length; i++)
            {
                if (i == 2) continue;
                Vector3 direction = Quaternion.Euler(0f, Angles[i], 0f) * transform.forward;
                if (!Physics.Raycast(origin, direction, out RaycastHit hit,
                        detectionDistance, trafficLayer, QueryTriggerInteraction.Ignore))
                {
                    if (summary != null) summary.Append(Names[i]).Append(": no hit\n");
                    continue;
                }

                bool blocks = shouldBlock != null && shouldBlock(hit.collider);
                if (summary != null)
                    summary.Append(Names[i]).Append(": ").Append(hit.collider.name)
                        .Append(" (").Append(hit.distance.ToString("F2"))
                        .Append("m)").Append(blocks ? " — blocking\n" : " — ignored\n");
                if (blocks && (!cachedSideBlocking || hit.distance < cachedSideBlockingHit.distance))
                {
                    cachedSideBlocking = true;
                    cachedSideBlockingHit = hit;
                }
            }
        }

        if (cachedSideBlocking && cachedSideBlockingHit.collider != null &&
            cachedSideBlockingHit.distance < closestDistance)
        {
            closestDistance = cachedSideBlockingHit.distance;
            blockingHit = cachedSideBlockingHit;
            found = true;
            LastRayHitCollider = cachedSideBlockingHit.collider.name;
            LastRayDirection = "Angled";
            LastRayDistance = cachedSideBlockingHit.distance;
        }

        if (summary != null)
            debugRaycastSummary = summary.Length == 0 ? "No raycast results." : summary.ToString();

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
