
using UnityEngine;

public class TrafficStuckFailsafe : MonoBehaviour
{
    [Header("Stuck Detection")]
    [Min(0.1f)]
    public float stuckTime = 10f;

    [Min(0.001f)]
    public float movementThreshold = 0.05f;

    [Header("Parking Wait Failsafe")]
    [Tooltip("Destroy the vehicle if it waits this long for a parking space.")]
    [Min(0.1f)]
    public float parkingWaitDestroyTime = 10f;

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
    public float debugParkingWaitTime;

    private Collider[] vehicleColliders;
    private bool[] previousColliderStates;

    private Vector3 stationaryAnchor;
    private Vector3 disabledPosition;

    private float stationaryTimer;
    private float parkingWaitTimer;

    private bool collidersTemporarilyDisabled;

    private TrafficTestVehicle trafficVehicle;

    private void Awake()
    {
        vehicleColliders =
            GetComponentsInChildren<Collider>(true);

        previousColliderStates =
            new bool[vehicleColliders.Length];

        trafficVehicle =
            GetComponent<TrafficTestVehicle>();

        stationaryAnchor = transform.position;

        if (vehicleColliders.Length == 0)
        {
            SetFailsafeLog(
                "WARNING: No colliders found on this vehicle."
            );
        }
        else
        {
            SetFailsafeLog("Monitoring movement.");
        }

        if (trafficVehicle == null)
        {
            SetFailsafeLog(
                "WARNING: TrafficTestVehicle component not found. " +
                "Parking-wait failsafe will not work."
            );
        }
    }

    private void Update()
    {
        // Special failsafe: destroy vehicles that wait too long
        // for a parking space.
        if (trafficVehicle != null &&
            trafficVehicle.CurrentState ==
                TrafficTestVehicle.VehicleState.WaitingForParking)
        {
            parkingWaitTimer += Time.deltaTime;
            debugParkingWaitTime = parkingWaitTimer;

            if (parkingWaitTimer >= parkingWaitDestroyTime)
            {
                SetFailsafeLog(
                    $"PARKING FAILSAFE: Waiting for parking for " +
                    $"{parkingWaitDestroyTime:F1}s. Destroying vehicle."
                );

                Destroy(gameObject);
                return;
            }
        }
        else
        {
            parkingWaitTimer = 0f;
            debugParkingWaitTime = 0f;
        }

        // Preserve the existing collider recovery behavior.
        if (collidersTemporarilyDisabled)
        {
            if (disableCollidersUntilMoving)
            {
                CheckForMovementAfterFailsafe();
            }

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
            if (disableCollidersUntilMoving)
            {
                DisableColliders();
            }
            else
            {
                stationaryTimer = 0f;
                stationaryAnchor = transform.position;
            }
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
