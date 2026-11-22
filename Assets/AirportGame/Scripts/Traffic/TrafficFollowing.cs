using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Route-aware traffic spacing. Vehicles only follow another vehicle when
/// both routes share the same directed road segment ahead of the follower.
/// No physics queries or collider sensing are used.
/// </summary>
[DisallowMultipleComponent]
public sealed class TrafficFollowing : MonoBehaviour
{
    [Header("Route Following")]
    [Min(0.1f)]
    [SerializeField] private float desiredFollowingDistance = 2.5f;
    [Min(0.1f)]
    [SerializeField] private float routeMatchTolerance = 1.25f;

    [Header("Speed Control")]
    [Min(0f)] [SerializeField] private float acceleration = 4f;
    [Min(0f)] [SerializeField] private float braking = 8f;

    private static readonly HashSet<TrafficFollowing> ActiveVehicles =
        new HashSet<TrafficFollowing>();

    private IReadOnlyList<Vector2Int> currentRoute;
    private RoadNetwork roadNetwork;
    private GridManager gridManager;

    public string LastRayHitCollider => "None";
    public string LastRayDirection => "Route";
    public float LastRayDistance { get; private set; }
    public string RaycastSummary => "Route-based following; no raycasts.";

    private void OnEnable()
    {
        ActiveVehicles.Add(this);
    }

    private void OnDisable()
    {
        ActiveVehicles.Remove(this);
        currentRoute = null;
    }

    public float BrakeToStop(float currentSpeed)
    {
        return Mathf.MoveTowards(
            currentSpeed,
            0f,
            braking * GameTimeManager.SimulationDeltaTime);
    }

    public float GetFollowingSpeed(
        float desiredSpeed,
        float currentSpeed,
        IReadOnlyList<Vector2Int> route,
        RoadNetwork network,
        GridManager grid,
        bool skipFollowing,
        out float followingDistance)
    {
        currentRoute = route;
        roadNetwork = network;
        gridManager = grid;
        followingDistance = float.PositiveInfinity;
        LastRayDistance = 0f;

        float targetSpeed = Mathf.Max(0f, desiredSpeed);

        if (!skipFollowing &&
            route != null &&
            route.Count >= 2 &&
            roadNetwork != null &&
            gridManager != null &&
            TryGetRouteProgress(route, transform.position, transform.forward,
                out float ownProgress, out _))
        {
            foreach (TrafficFollowing other in ActiveVehicles)
            {
                if (other == null || other == this ||
                    !other.isActiveAndEnabled ||
                    !other.gameObject.activeInHierarchy ||
                    other.currentRoute == null ||
                    other.currentRoute.Count < 2)
                    continue;

                if (!TryGetLeaderProgressOnRoute(
                        other, route, out float leaderProgress))
                    continue;

                float gap = leaderProgress - ownProgress;
                if (gap <= 0f || gap >= followingDistance)
                    continue;

                followingDistance = gap;
            }

            if (!float.IsPositiveInfinity(followingDistance))
            {
                LastRayDistance = followingDistance;
                float safeDistance = Mathf.Max(0.1f, desiredFollowingDistance);
                float releaseDistance = safeDistance + Mathf.Max(1f, desiredSpeed * 0.75f);
                float spacingFactor = Mathf.InverseLerp(
                    safeDistance, releaseDistance, followingDistance);
                targetSpeed *= spacingFactor;
            }
        }

        float rate = targetSpeed < currentSpeed ? braking : acceleration;
        return Mathf.MoveTowards(
            currentSpeed,
            targetSpeed,
            rate * GameTimeManager.SimulationDeltaTime);
    }

    private bool TryGetLeaderProgressOnRoute(
        TrafficFollowing leader,
        IReadOnlyList<Vector2Int> followerRoute,
        out float leaderProgress)
    {
        leaderProgress = 0f;
        if (leader == null || leader.currentRoute == null)
            return false;

        float bestDistanceSqr = float.PositiveInfinity;
        bool found = false;
        float cumulativeDistance = 0f;

        for (int i = 0; i < followerRoute.Count - 1; i++)
        {
            Vector3 a = GetCellPosition(followerRoute[i]);
            Vector3 b = GetCellPosition(followerRoute[i + 1]);
            Vector3 segment = b - a;
            segment.y = 0f;
            float length = segment.magnitude;
            if (length <= 0.001f)
                continue;

            if (!ContainsDirectedSegment(leader.currentRoute,
                    followerRoute[i], followerRoute[i + 1]))
            {
                cumulativeDistance += length;
                continue;
            }

            Vector3 toLeader = leader.transform.position - a;
            toLeader.y = 0f;
            float t = Mathf.Clamp01(Vector3.Dot(toLeader, segment) /
                                    (length * length));
            Vector3 closest = a + segment * t;
            Vector3 offset = leader.transform.position - closest;
            offset.y = 0f;
            float distanceSqr = offset.sqrMagnitude;

            Vector3 direction = segment / length;
            float headingAgreement = Vector3.Dot(
                leader.transform.forward.normalized, direction);

            if (headingAgreement >= 0.35f &&
                distanceSqr < bestDistanceSqr)
            {
                bestDistanceSqr = distanceSqr;
                leaderProgress = cumulativeDistance + t * length;
                found = true;
            }

            cumulativeDistance += length;
        }

        return found &&
               bestDistanceSqr <= routeMatchTolerance * routeMatchTolerance;
    }

    private bool TryGetRouteProgress(
        IReadOnlyList<Vector2Int> route,
        Vector3 position,
        Vector3 forward,
        out float progress,
        out float distanceToRoute)
    {
        progress = 0f;
        distanceToRoute = float.PositiveInfinity;
        float bestDistanceSqr = float.PositiveInfinity;
        float cumulativeDistance = 0f;
        bool found = false;

        for (int i = 0; i < route.Count - 1; i++)
        {
            Vector3 a = GetCellPosition(route[i]);
            Vector3 b = GetCellPosition(route[i + 1]);
            Vector3 segment = b - a;
            segment.y = 0f;
            float length = segment.magnitude;
            if (length <= 0.001f)
                continue;

            Vector3 toPosition = position - a;
            toPosition.y = 0f;
            float t = Mathf.Clamp01(Vector3.Dot(toPosition, segment) /
                                    (length * length));
            Vector3 closest = a + segment * t;
            Vector3 offset = position - closest;
            offset.y = 0f;
            float distanceSqr = offset.sqrMagnitude;
            float headingAgreement = Vector3.Dot(
                forward.normalized, segment / length);

            if (headingAgreement >= -0.25f &&
                distanceSqr < bestDistanceSqr)
            {
                bestDistanceSqr = distanceSqr;
                progress = cumulativeDistance + t * length;
                found = true;
            }

            cumulativeDistance += length;
        }

        distanceToRoute = Mathf.Sqrt(bestDistanceSqr);
        return found && bestDistanceSqr <= routeMatchTolerance * routeMatchTolerance;
    }

    private Vector3 GetCellPosition(Vector2Int cell)
    {
        Vector3 position = roadNetwork.GetRoadCenterWorldPosition(
            cell, gridManager);
        position.y = 0f;
        return position;
    }

    private static bool ContainsDirectedSegment(
        IReadOnlyList<Vector2Int> route,
        Vector2Int from,
        Vector2Int to)
    {
        for (int i = 0; i < route.Count - 1; i++)
        {
            if (route[i] == from && route[i + 1] == to)
                return true;
        }

        return false;
    }
}
