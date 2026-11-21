using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class GuidedCargoTrailerFollower : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool logToConsole = false;

    [Header("References")]
    [Tooltip("Legacy single trailer reference. Normally assigned by the lot spawner.")]
    [SerializeField] private Transform trailer;

    [Tooltip("An empty child Transform at the rear towing point of the tug.")]
    [SerializeField] private Transform tugHitchPoint;

    [Header("Trailer Capacity")]
    [Min(1)]
    [SerializeField] private int cargoCapacityPerTrailer = 10;

    [Header("Trailer Position")]
    [Min(0.1f)]
    [Tooltip("Distance between the tug hitch and the first trailer.")]
    [SerializeField] private float hitchToTrailerDistance = 1.25f;

    [Min(0.1f)]
    [Tooltip("Distance between each trailer in the trailer chain.")]
    [SerializeField] private float trailerToTrailerDistance = 2.6f;

    [Min(0.1f)]
    [Tooltip("Maximum speed at which trailers catch up.")]
    [SerializeField] private float followSpeed = 8f;

    [Min(0.1f)]
    [Tooltip("How quickly trailers rotate to follow the vehicle in front.")]
    [SerializeField] private float rotationSpeed = 5f;

    [Header("Initialization")]
    [Tooltip("Position the assigned trailer behind the tug when play starts.")]
    [SerializeField] private bool positionTrailerAtStart = true;

    [Header("Debug")]
    [SerializeField] private bool showDebugMessages = true;
    [SerializeField] private bool isInitialized;
    [SerializeField] private float currentHitchDistance;

    private readonly List<Transform> trailers = new List<Transform>();
    private readonly List<float> trailerHeights = new List<float>();

    // Newest hitch position is at index zero. Older positions form the
    // path the tug has travelled, allowing trailers to follow corners
    // instead of cutting directly toward the tug's current position.
    private readonly List<Vector3> hitchPath = new List<Vector3>();

    private const float MinimumPathSampleDistance = 0.05f;
    private const float PathHistoryBuffer = 3f;

    private bool hasExplicitTrailerSet;

    public int AttachedTrailerCount =>
        trailers.FindAll(item => item != null).Count;

    public int TotalCargoCapacity =>
        AttachedTrailerCount * Mathf.Max(1, cargoCapacityPerTrailer);

    public void SetTrailer(Transform trailerTransform)
    {
        Transform[] newTrailers = trailerTransform != null
            ? new[] { trailerTransform }
            : new Transform[0];

        SetTrailers(newTrailers);
    }

    public void SetTrailers(Transform[] trailerTransforms)
    {
        SetTrailersInternal(trailerTransforms, true);
    }

    private void SetTrailersInternal(
        Transform[] trailerTransforms,
        bool snapToHitch)
    {
        hasExplicitTrailerSet = true;
        trailers.Clear();
        trailerHeights.Clear();
        hitchPath.Clear();
        isInitialized = false;

        if (trailerTransforms == null)
            return;

        foreach (Transform trailerTransform in trailerTransforms)
        {
            if (trailerTransform == null)
                continue;

            if (trailerTransform == transform ||
                trailerTransform.IsChildOf(transform))
            {
                LogError(
                    "GuidedCargoTrailerFollower: A trailer must be a " +
                    "separate scene object, not a child of the tug.",
                    this
                );
                continue;
            }

            trailers.Add(trailerTransform);
            trailerHeights.Add(trailerTransform.position.y);
        }

        if (trailers.Count == 0)
            return;

        if (tugHitchPoint == null)
        {
            LogError(
                "GuidedCargoTrailerFollower: Tug Hitch Point is not assigned.",
                this
            );
            return;
        }

        isInitialized = true;
        InitializeHitchPath();

        if (snapToHitch)
            UpdateTrailerPositions(true);

        if (showDebugMessages)
        {
            Log(
                $"Guided trailer chain initialized with {trailers.Count} trailer(s).",
                this
            );
        }
    }

    public void ClearTrailers()
    {
        trailers.Clear();
        trailerHeights.Clear();
        hitchPath.Clear();
        isInitialized = false;

        if (showDebugMessages)
        {
            Log(
                "GuidedCargoTrailerFollower: Trailer chain detached.",
                this
            );
        }
    }

    private void Start()
    {
        if (tugHitchPoint == null)
        {
            LogError(
                "GuidedCargoTrailerFollower: Tug Hitch Point is not assigned.",
                this
            );

            enabled = false;
            return;
        }

        // Preserve support for a trailer assigned directly in the Inspector.
        if (!hasExplicitTrailerSet && trailer != null && trailers.Count == 0)
        {
            SetTrailersInternal(
                new[] { trailer },
                positionTrailerAtStart
            );
        }
    }

    private void LateUpdate()
    {
        if (!isInitialized ||
            trailers.Count == 0 ||
            tugHitchPoint == null)
        {
            return;
        }

        RecordHitchPosition();
        UpdateTrailerPositions(false);
    }

    private void InitializeHitchPath()
    {
        hitchPath.Clear();

        Vector3 hitchPosition = tugHitchPoint.position;
        hitchPath.Add(hitchPosition);

        Vector3 rearDirection = hitchPosition - transform.position;
        rearDirection.y = 0f;

        if (rearDirection.sqrMagnitude < 0.0001f)
        {
            rearDirection = transform.forward;
            rearDirection.y = 0f;
        }

        if (rearDirection.sqrMagnitude < 0.0001f)
            rearDirection = Vector3.forward;

        rearDirection.Normalize();

        float requiredHistoryDistance = GetTotalTrailerChainDistance() +
            PathHistoryBuffer;

        hitchPath.Add(
            hitchPosition + rearDirection * requiredHistoryDistance
        );
    }

    private void RecordHitchPosition()
    {
        Vector3 currentPosition = tugHitchPoint.position;

        if (hitchPath.Count == 0)
        {
            hitchPath.Add(currentPosition);
            return;
        }

        float movement = Vector3.Distance(
            hitchPath[0],
            currentPosition
        );

        if (movement < MinimumPathSampleDistance)
            return;

        hitchPath.Insert(0, currentPosition);
        TrimHitchPath();
    }

    private void TrimHitchPath()
    {
        float maximumHistory = GetTotalTrailerChainDistance() +
            PathHistoryBuffer;

        while (hitchPath.Count > 2)
        {
            float pathLength = GetHitchPathLength();

            if (pathLength <= maximumHistory)
                break;

            hitchPath.RemoveAt(hitchPath.Count - 1);
        }
    }

    private float GetHitchPathLength()
    {
        float length = 0f;

        for (int i = 0; i < hitchPath.Count - 1; i++)
            length += Vector3.Distance(hitchPath[i], hitchPath[i + 1]);

        return length;
    }

    private float GetTotalTrailerChainDistance()
    {
        if (trailers.Count == 0)
            return hitchToTrailerDistance;

        return Mathf.Max(0f, hitchToTrailerDistance) +
            Mathf.Max(0, trailers.Count - 1) *
            Mathf.Max(0f, trailerToTrailerDistance);
    }

    private void UpdateTrailerPositions(bool snapRotation)
    {
        float distanceBehindHitch = Mathf.Max(0.1f, hitchToTrailerDistance);

        for (int i = 0; i < trailers.Count; i++)
        {
            Transform currentTrailer = trailers[i];

            if (currentTrailer == null)
            {
                distanceBehindHitch += trailerToTrailerDistance;
                continue;
            }

            if (TryGetHitchPathPosition(
                    distanceBehindHitch,
                    out Vector3 targetPosition,
                    out Vector3 directionToHitch))
            {
                targetPosition.y = trailerHeights[i];
                currentHitchDistance = Vector3.Distance(
                    currentTrailer.position,
                    targetPosition
                );

                currentTrailer.position = snapRotation
                    ? targetPosition
                    : Vector3.MoveTowards(
                        currentTrailer.position,
                        targetPosition,
                        Mathf.Max(0.1f, followSpeed) * Time.deltaTime
                    );

                if (directionToHitch.sqrMagnitude > 0.0001f)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(
                        directionToHitch,
                        Vector3.up
                    );

                    currentTrailer.rotation = snapRotation
                        ? targetRotation
                        : Quaternion.Slerp(
                            currentTrailer.rotation,
                            targetRotation,
                            Mathf.Clamp01(rotationSpeed * Time.deltaTime)
                        );
                }
            }

            distanceBehindHitch += Mathf.Max(0.1f, trailerToTrailerDistance);
        }
    }

    private bool TryGetHitchPathPosition(
        float distanceBehindHitch,
        out Vector3 position,
        out Vector3 directionToHitch)
    {
        position = Vector3.zero;
        directionToHitch = Vector3.zero;

        if (hitchPath.Count < 2)
            return false;

        float distanceAlongPath = 0f;

        for (int i = 0; i < hitchPath.Count - 1; i++)
        {
            Vector3 newerPoint = hitchPath[i];
            Vector3 olderPoint = hitchPath[i + 1];

            float segmentLength = Vector3.Distance(
                newerPoint,
                olderPoint
            );

            if (segmentLength <= 0.0001f)
                continue;

            if (distanceAlongPath + segmentLength >= distanceBehindHitch)
            {
                float segmentProgress =
                    (distanceBehindHitch - distanceAlongPath) /
                    segmentLength;

                position = Vector3.Lerp(
                    newerPoint,
                    olderPoint,
                    segmentProgress
                );

                directionToHitch = newerPoint - olderPoint;
                directionToHitch.y = 0f;

                if (directionToHitch.sqrMagnitude > 0.0001f)
                    directionToHitch.Normalize();

                return true;
            }

            distanceAlongPath += segmentLength;
        }

        // If the tug has not yet travelled far enough to fill the requested
        // trailer spacing, extend the last path segment behind the tug.
        Vector3 lastDirection =
            hitchPath[hitchPath.Count - 2] -
            hitchPath[hitchPath.Count - 1];

        lastDirection.y = 0f;

        if (lastDirection.sqrMagnitude < 0.0001f)
            return false;

        lastDirection.Normalize();

        float extraDistance = distanceBehindHitch - distanceAlongPath;

        position = hitchPath[hitchPath.Count - 1] -
            lastDirection * extraDistance;

        directionToHitch = lastDirection;
        return true;
    }

    private void Log(object message, UnityEngine.Object context = null)
    {
        if (!logToConsole) return;
        if (context != null) Debug.Log(message, context); else Debug.Log(message);
    }

    private void LogWarning(object message, UnityEngine.Object context = null)
    {
        if (!logToConsole) return;
        if (context != null) Debug.LogWarning(message, context); else Debug.LogWarning(message);
    }

    private void LogError(object message, UnityEngine.Object context = null)
    {
        if (!logToConsole) return;
        if (context != null) Debug.LogError(message, context); else Debug.LogError(message);
    }

    private void LogException(System.Exception exception, UnityEngine.Object context = null)
    {
        if (!logToConsole) return;
        if (context != null) Debug.LogException(exception, context); else Debug.LogException(exception);
    }
}
