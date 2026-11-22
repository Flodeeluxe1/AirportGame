
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

    private readonly List<Transform> trailers =
        new List<Transform>();

    private readonly List<float> trailerHeights =
        new List<float>();

    private bool hasExplicitTrailerSet;
    private float pendingSimulationTime;

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
        hasExplicitTrailerSet = true;
        trailers.Clear();
        trailerHeights.Clear();
        pendingSimulationTime = 0f;

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

        if (tugHitchPoint == null)
        {
            LogError(
                "GuidedCargoTrailerFollower: Tug Hitch Point is not assigned.",
                this
            );
            return;
        }

        isInitialized = true;

        if (showDebugMessages && trailers.Count > 0)
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
        pendingSimulationTime = 0f;

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
            SetTrailers(new[] { trailer });

            if (positionTrailerAtStart && trailers.Count > 0)
                PositionTrailerBehindTug();
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

        // Keep trailer motion independent of Unity's capped timeScale.
        // Bounded substeps preserve gradual following and rotation at high
        // speeds without allowing an unbounded catch-up loop.
        const float maximumSimulationStep = 0.1f;
        const int maximumStepsPerFrame = 192;

        pendingSimulationTime += GameTimeManager.SimulationDeltaTime;

        int steps = 0;
        while (pendingSimulationTime > 0f && steps < maximumStepsPerFrame)
        {
            float step = Mathf.Min(pendingSimulationTime, maximumSimulationStep);
            UpdateTrailerChain(step);
            pendingSimulationTime = Mathf.Max(0f, pendingSimulationTime - step);
            steps++;
        }
    }

    private void UpdateTrailerChain(float deltaTime)
    {
        for (int i = 0; i < trailers.Count; i++)
        {
            Transform currentTrailer = trailers[i];

            if (currentTrailer == null)
                continue;

            Transform objectInFront = i == 0
                ? tugHitchPoint
                : trailers[i - 1];

            if (objectInFront == null)
                continue;

            Vector3 directionToFront =
                objectInFront.position - currentTrailer.position;

            directionToFront.y = 0f;

            if (directionToFront.sqrMagnitude < 0.0001f)
                continue;

            currentHitchDistance = directionToFront.magnitude;
            directionToFront.Normalize();

            float spacing = i == 0
                ? hitchToTrailerDistance
                : trailerToTrailerDistance;

            Vector3 targetPosition =
                objectInFront.position -
                directionToFront * spacing;

            targetPosition.y = trailerHeights[i];

            currentTrailer.position = Vector3.MoveTowards(
                currentTrailer.position,
                targetPosition,
                followSpeed * deltaTime
            );

            Quaternion targetRotation =
                Quaternion.LookRotation(directionToFront, Vector3.up);

            float rotationLerp = 1f - Mathf.Exp(-rotationSpeed * deltaTime);
            currentTrailer.rotation = Quaternion.Slerp(
                currentTrailer.rotation,
                targetRotation,
                rotationLerp
            );
        }
    }

    private void PositionTrailerBehindTug()
    {
        if (trailers.Count == 0 || tugHitchPoint == null)
            return;

        Vector3 tugForward = transform.forward;
        tugForward.y = 0f;

        if (tugForward.sqrMagnitude < 0.0001f)
            tugForward = Vector3.forward;

        tugForward.Normalize();

        Transform firstTrailer = trailers[0];

        firstTrailer.rotation = Quaternion.LookRotation(
            tugForward,
            Vector3.up
        );

        firstTrailer.position =
            tugHitchPoint.position -
            tugForward * hitchToTrailerDistance;

        Vector3 position = firstTrailer.position;
        position.y = trailerHeights[0];
        firstTrailer.position = position;
    }

    private void Log(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.Log(message, context); else Debug.Log(message); }
    private void LogWarning(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogWarning(message, context); else Debug.LogWarning(message); }
    private void LogError(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogError(message, context); else Debug.LogError(message); }
    private void LogException(System.Exception exception, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogException(exception, context); else Debug.LogException(exception); }
}
