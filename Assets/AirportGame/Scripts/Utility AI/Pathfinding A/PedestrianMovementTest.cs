using UnityEngine;

/// <summary>
/// Temporary in-scene test helper for checking pedestrian pathfinding and movement.
/// Add it to a pedestrian instance, assign a destination Transform, enter Play Mode,
/// then use the component context menu to start or stop the test.
</summary>
[DisallowMultipleComponent]
public class PedestrianMovementTest : MonoBehaviour
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
            Debug.LogWarning("Enter Play Mode before starting the pedestrian movement test.", this);
            return;
        }

        if (movementController == null)
            movementController = GetComponent<PedestrianMovementController>();

        if (movementController == null)
        {
            Debug.LogError("PedestrianMovementController is missing from this GameObject.", this);
            return;
        }

        if (destination == null)
        {
            Debug.LogError("Assign a destination Transform before starting the test.", this);
            return;
        }

        bool started = movementController.MoveTo(destination.position);

        if (logResults)
        {
            Debug.Log(
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
}
