using UnityEngine;

/// <summary>
/// Opens the sliding panel of FenceGate1 when RoadTraffic vehicles approach
/// or pass through its detection area. Detection uses the existing
/// TrafficFollowing registry and does not require Rigidbody components,
/// trigger callbacks, or physics queries.
/// </summary>
[DefaultExecutionOrder(-10000)]
[DisallowMultipleComponent]
public sealed class AutomaticFenceGate : MonoBehaviour
{
    [Header("Sliding Panel")]
    [Tooltip("The child transform named FenceGate is found automatically if this is empty.")]
    [SerializeField] private Transform slidingPanel;

    [Tooltip("Distance to slide the panel along the gate's local X axis.")]
    [SerializeField] private float openLocalXOffset = 3f;

    [Min(0.01f)]
    [SerializeField] private float slideSpeed = 2.5f;

    [Header("Vehicle Detection")]
    [Tooltip("Detection box center in this gate's local space.")]
    [SerializeField] private Vector3 detectionBoxCenter =
        new Vector3(1.5f, 1f, 0f);

    [Tooltip("Detection box size in this gate's local space. It covers both approach directions.")]
    [SerializeField] private Vector3 detectionBoxSize =
        new Vector3(8f, 4f, 12f);

    [Min(0f)]
    [Tooltip("Real seconds to keep the gate open after the last vehicle leaves the detection area.")]
    [SerializeField] private float holdOpenAfterClear = 3f;

    private Vector3 closedLocalPosition;
    private float clearTimer;
    private bool initialized;

    public bool IsOpen { get; private set; }
    public bool IsVehicleDetected { get; private set; }

    private void Awake()
    {
        if (slidingPanel == null)
            slidingPanel = FindSlidingPanel();

        if (slidingPanel == null)
        {
            Debug.LogError(
                "AutomaticFenceGate: Could not find child 'FenceGate'. " +
                "Assign the sliding panel in the Inspector.",
                this);
            return;
        }

        closedLocalPosition = slidingPanel.localPosition;
        initialized = true;
    }

    /// <summary>
    /// Runs after vehicle Update movement but before TrafficFollowing stores
    /// the next frame's previous positions. This makes the swept detection
    /// reliable regardless of normal Update execution order.
    /// </summary>
    private void LateUpdate()
    {
        if (!initialized || slidingPanel == null)
            return;

        IsVehicleDetected =
            TrafficFollowing.IsAnyVehicleInsideOrCrossingBox(
                transform,
                detectionBoxCenter,
                detectionBoxSize);

        if (IsVehicleDetected)
        {
            clearTimer = holdOpenAfterClear;
        }
        else
        {
            clearTimer = Mathf.Max(
                0f,
                clearTimer - Time.unscaledDeltaTime);
        }

        IsOpen = IsVehicleDetected || clearTimer > 0f;

        Vector3 targetLocalPosition = closedLocalPosition;

        if (IsOpen)
            targetLocalPosition.x += openLocalXOffset;

        float movementThisFrame =
            Mathf.Max(0.01f, slideSpeed) * Time.unscaledDeltaTime;

        slidingPanel.localPosition = Vector3.MoveTowards(
            slidingPanel.localPosition,
            targetLocalPosition,
            movementThisFrame);
    }

    private Transform FindSlidingPanel()
    {
        Transform[] childTransforms =
            GetComponentsInChildren<Transform>(true);

        for (int i = 0; i < childTransforms.Length; i++)
        {
            if (childTransforms[i] != transform &&
                childTransforms[i].name == "FenceGate")
            {
                return childTransforms[i];
            }
        }

        return null;
    }

    private void OnDrawGizmosSelected()
    {
        Matrix4x4 previousMatrix = Gizmos.matrix;

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(
            detectionBoxCenter,
            detectionBoxSize);

        Gizmos.matrix = previousMatrix;
    }

    private void OnValidate()
    {
        detectionBoxSize.x = Mathf.Max(0.1f, detectionBoxSize.x);
        detectionBoxSize.y = Mathf.Max(0.1f, detectionBoxSize.y);
        detectionBoxSize.z = Mathf.Max(0.1f, detectionBoxSize.z);
        slideSpeed = Mathf.Max(0.01f, slideSpeed);
        holdOpenAfterClear = Mathf.Max(0f, holdOpenAfterClear);
    }
}
