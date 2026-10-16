
using UnityEngine;

[DisallowMultipleComponent]
public class GuidedCargoTrailerFollower : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The root Transform of the trailer.")]
    [SerializeField] private Transform trailer;

    [Tooltip("An empty child Transform at the rear towing point of the tug.")]
    [SerializeField] private Transform tugHitchPoint;

    [Header("Trailer Position")]
    [Min(0.1f)]
    [Tooltip("Distance from the tug hitch to the trailer root.")]
    [SerializeField] private float hitchToTrailerDistance = 2f;

    [Min(0.1f)]
    [Tooltip("Maximum speed at which the trailer catches up.")]
    [SerializeField] private float followSpeed = 8f;

    [Min(0.1f)]
    [Tooltip("How quickly the trailer rotates to face the tug hitch.")]
    [SerializeField] private float rotationSpeed = 5f;

    [Header("Initialization")]
    [Tooltip("Position the trailer behind the tug when play starts.")]
    [SerializeField] private bool positionTrailerAtStart = true;

    [Header("Debug")]
    [SerializeField] private bool showDebugMessages = true;
    [SerializeField] private bool isInitialized;
    [SerializeField] private float currentHitchDistance;

    private float trailerHeight;

    public void SetTrailer(Transform trailerTransform)
    {
        trailer = trailerTransform;
    }

    private void Start()
    {
        if (trailer == null)
        {
            Debug.LogError(
                "GuidedCargoTrailerFollower: Trailer is not assigned.",
                this
            );
            enabled = false;
            return;
        }

        if (tugHitchPoint == null)
        {
            Debug.LogError(
                "GuidedCargoTrailerFollower: Tug Hitch Point is not assigned.",
                this
            );
            enabled = false;
            return;
        }

        if (trailer == transform ||
            trailer.IsChildOf(transform))
        {
            Debug.LogError(
                "GuidedCargoTrailerFollower: The trailer must be a separate " +
                "scene object, not a child of the tug.",
                this
            );
            enabled = false;
            return;
        }

        trailerHeight = trailer.position.y;

        if (positionTrailerAtStart)
        {
            PositionTrailerBehindTug();
        }

        isInitialized = true;

        if (showDebugMessages)
        {
            Debug.Log(
                $"Guided trailer initialized. Trailer: {trailer.name}, " +
                $"distance: {hitchToTrailerDistance:F2} m.",
                this
            );
        }
    }

    private void LateUpdate()
    {
        if (!isInitialized ||
            trailer == null ||
            tugHitchPoint == null)
        {
            return;
        }

        Vector3 directionToHitch =
            tugHitchPoint.position - trailer.position;

        directionToHitch.y = 0f;

        currentHitchDistance = directionToHitch.magnitude;

        if (directionToHitch.sqrMagnitude < 0.0001f)
        {
            return;
        }

        directionToHitch.Normalize();

        Vector3 targetPosition =
            tugHitchPoint.position -
            directionToHitch * hitchToTrailerDistance;

        targetPosition.y = trailerHeight;

        trailer.position = Vector3.MoveTowards(
            trailer.position,
            targetPosition,
            followSpeed * Time.deltaTime
        );

        Quaternion targetRotation =
            Quaternion.LookRotation(directionToHitch, Vector3.up);

        trailer.rotation = Quaternion.Slerp(
            trailer.rotation,
            targetRotation,
            rotationSpeed * Time.deltaTime
        );
    }

    private void PositionTrailerBehindTug()
    {
        Vector3 tugForward = transform.forward;
        tugForward.y = 0f;

        if (tugForward.sqrMagnitude < 0.0001f)
        {
            tugForward = Vector3.forward;
        }

        tugForward.Normalize();

        trailer.rotation = Quaternion.LookRotation(
            tugForward,
            Vector3.up
        );

        trailerHeight = trailer.position.y;

        trailer.position =
            tugHitchPoint.position -
            tugForward * hitchToTrailerDistance;

        Vector3 position = trailer.position;
        position.y = trailerHeight;
        trailer.position = position;
    }
}
