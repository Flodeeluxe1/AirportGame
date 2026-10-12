using UnityEngine;

public class TrafficStuckFailsafe : MonoBehaviour
{
    [Header("Stuck Detection")]
    [Min(0.1f)]
    public float stuckTime = 10f;

    [Min(0.001f)]
    public float movementThreshold = 0.05f;

    [Header("Collider Recovery")]
    [Tooltip("Colliders stay disabled until movement is detected.")]
    public bool disableCollidersUntilMoving = true;

    [Header("Debug")]
    public bool logFailsafe = true;

    [Tooltip("Latest failsafe event. Updates during Play Mode.")]
    [TextArea(2, 5)]
    public string debugFailsafeLog = "Monitoring movement.";

    public bool debugCollidersDisabled;
    public float debugStationaryTime;

    private Collider[] vehicleColliders;
    private bool[] previousColliderStates;

    private Vector3 stationaryAnchor;
    private Vector3 disabledPosition;

    private float stationaryTimer;
    private bool collidersTemporarilyDisabled;

    private void Awake()
    {
        vehicleColliders =
            GetComponentsInChildren<Collider>(true);

        previousColliderStates =
            new bool[vehicleColliders.Length];

        stationaryAnchor = transform.position;

        if (vehicleColliders.Length == 0)
        {
            SetFailsafeLog(
                "WARNING: No colliders found on this vehicle."
            );
        }
        else
        {
            SetFailsafeLog(
                "Monitoring movement."
            );
        }
    }

    private void Update()
    {
        if (collidersTemporarilyDisabled)
        {
            CheckForMovementAfterFailsafe();
            return;
        }

        float movement =
            Vector3.Distance(
                transform.position,
                stationaryAnchor
            );

        if (movement >= movementThreshold)
        {
            stationaryAnchor = transform.position;
            stationaryTimer = 0f;
            debugStationaryTime = 0f;
            return;
        }

        stationaryTimer += Time.deltaTime;
        debugStationaryTime = stationaryTimer;

        if (stationaryTimer >= stuckTime)
        {
            DisableColliders();
        }
    }

    private void DisableColliders()
    {
        if (vehicleColliders.Length == 0)
        {
            stationaryTimer = 0f;
            stationaryAnchor = transform.position;

            SetFailsafeLog(
                "Cannot activate failsafe: no colliders found."
            );

            return;
        }

        for (int i = 0; i < vehicleColliders.Length; i++)
        {
            Collider vehicleCollider =
                vehicleColliders[i];

            if (vehicleCollider == null)
                continue;

            previousColliderStates[i] =
                vehicleCollider.enabled;

            vehicleCollider.enabled = false;
        }

        collidersTemporarilyDisabled = true;
        debugCollidersDisabled = true;

        disabledPosition = transform.position;
        stationaryTimer = 0f;
        debugStationaryTime = 0f;

        SetFailsafeLog(
            $"FAILSAFE ACTIVATED: Stationary for " +
            $"{stuckTime:F1}s. Colliders disabled " +
            "until movement resumes."
        );
    }

    private void CheckForMovementAfterFailsafe()
    {
        float movement =
            Vector3.Distance(
                transform.position,
                disabledPosition
            );

        if (movement < movementThreshold)
            return;

        RestoreColliders();
    }

    private void RestoreColliders()
    {
        for (int i = 0; i < vehicleColliders.Length; i++)
        {
            if (vehicleColliders[i] == null)
                continue;

            vehicleColliders[i].enabled =
                previousColliderStates[i];
        }

        collidersTemporarilyDisabled = false;
        debugCollidersDisabled = false;

        stationaryTimer = 0f;
        debugStationaryTime = 0f;

        stationaryAnchor = transform.position;

        SetFailsafeLog(
            "MOVEMENT DETECTED: Colliders restored."
        );
    }

    private void SetFailsafeLog(string message)
    {
        debugFailsafeLog = message;

        if (logFailsafe)
        {
            Debug.Log(
                $"{name}: {message}",
                this
            );
        }
    }
}