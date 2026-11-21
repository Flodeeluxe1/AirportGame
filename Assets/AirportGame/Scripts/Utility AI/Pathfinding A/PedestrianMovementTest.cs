using UnityEngine;

[DisallowMultipleComponent]
public class PedestrianMovementTest : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool logToConsole = false;
{
    [SerializeField] private PedestrianMovementController movementController;
    [SerializeField] private Transform destination;
    [SerializeField] private bool logResults = true;

    private void Awake()
    {
        if (movementController == null)
            movementController = GetComponent<PedestrianMovementController>();
    }

    [ContextMenu("Pedestrian Test/Move To Destination")]
    private void MoveToDestination()
    {
        if (!Application.isPlaying)
        {
            LogWarning("Enter Play Mode before starting the pedestrian movement test.", this);
            return;
        }

        if (movementController == null)
            movementController = GetComponent<PedestrianMovementController>();

        if (movementController == null)
        {
            LogError("PedestrianMovementController is missing from this GameObject.", this);
            return;
        }

        if (destination == null)
        {
            LogError("Assign a destination Transform before starting the test.", this);
            return;
        }

        bool started = movementController.MoveTo(destination.position);

        if (logResults)
        {
            Log(
                started
                    ? $"Pedestrian movement test started. Destination: {destination.position}"
                    : $"Pedestrian movement test failed. Status: {movementController.MovementStatus}",
                this);
        }
    }

    [ContextMenu("Pedestrian Test/Stop")]
    private void StopMovement()
    {
        if (movementController == null)
            movementController = GetComponent<PedestrianMovementController>();

        if (movementController != null)
            movementController.Stop();
    }

    private void OnDrawGizmosSelected()
    {
        if (destination == null)
            return;

        Gizmos.DrawLine(transform.position, destination.position);
        Gizmos.DrawWireSphere(destination.position, 0.2f);
    }

    private void Log(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.Log(message, context); else Debug.Log(message); }
    private void LogWarning(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogWarning(message, context); else Debug.LogWarning(message); }
    private void LogError(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogError(message, context); else Debug.LogError(message); }
    private void LogException(System.Exception exception, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogException(exception, context); else Debug.LogException(exception); }
}
