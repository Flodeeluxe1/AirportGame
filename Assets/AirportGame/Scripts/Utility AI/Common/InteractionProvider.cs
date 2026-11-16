using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Advertises the interactions available at a world object and provides
/// the world-space point where an agent should approach to use it.
/// </summary>
[DisallowMultipleComponent]
public class InteractionProvider : MonoBehaviour
{
    [Header("Available Interactions")]
    [SerializeField] private List<InteractionDefinition> interactions =
        new List<InteractionDefinition>();
    [SerializeField] private Transform interactionPoint;
    [SerializeField] private bool interactionsEnabled = true;

    private InteractionQueue interactionQueue;
    private bool interactionPointResolved;
    private static GridManager cachedGridManager;
    private static bool gridManagerLookupAttempted;

    public IReadOnlyList<InteractionDefinition> Interactions => interactions;
    public InteractionQueue Queue
    {
        get
        {
            if (interactionQueue == null)
                interactionQueue = GetComponent<InteractionQueue>();

            return interactionQueue;
        }
    }

    /// <summary>
    /// World-space position where the agent should approach this object.
    /// Uses the assigned point when present; otherwise one grid cell forward
    /// from the object's origin.
    /// </summary>
    public Vector3 InteractionPosition
    {
        get
        {
            if (!interactionPointResolved)
            {
                interactionPointResolved = true;
                if (interactionPoint == null)
                    interactionPoint = transform.Find("interactionPoint");
            }

            if (interactionPoint != null)
                return interactionPoint.position;

            if (!gridManagerLookupAttempted)
            {
                cachedGridManager = FindFirstObjectByType<GridManager>();
                gridManagerLookupAttempted = true;
            }

            float gridUnit = cachedGridManager != null
                ? cachedGridManager.CellSize
                : 1f;

            return transform.position + transform.forward * gridUnit;
        }
    }

    private void OnEnable()
    {
        UtilityAISpatialRegistry<InteractionProvider>.Register(this);
    }

    public bool InteractionsEnabled =>
        interactionsEnabled && isActiveAndEnabled && gameObject.activeInHierarchy;

    /// <summary>
    /// Returns whether this provider offers the definition and either has
    /// a free slot or room in its enabled queue.
    /// </summary>
    public bool CanOffer(InteractionDefinition definition)
    {
        if (!InteractionsEnabled ||
            definition == null ||
            interactions == null ||
            !interactions.Contains(definition))
        {
            return false;
        }

        if (Queue != null && Queue.QueueEnabled)
            return Queue.CanAccept(definition);

        return InteractionReservation.HasAvailableSlot(this, definition);
    }

    /// <summary>
    /// Claims one usage slot for the supplied agent.
    /// </summary>
    public bool TryReserve(
        MonoBehaviour agent,
        InteractionDefinition definition)
    {
        if (agent == null || definition == null ||
            !InteractionsEnabled ||
            interactions == null ||
            !interactions.Contains(definition))
        {
            return false;
        }

        // Queue promotion must be able to reserve the active slot even when
        // the waiting queue itself has reached its configured capacity.
        return InteractionReservation.TryReserve(this, definition, agent);
    }

    public void ReleaseReservation(
        MonoBehaviour agent,
        InteractionDefinition definition)
    {
        InteractionReservation.Release(this, definition, agent);
    }

    public void ReleaseAllReservations(MonoBehaviour agent)
    {
        InteractionReservation.ReleaseAll(agent, this);
    }

    private void OnDisable()
    {
        UtilityAISpatialRegistry<InteractionProvider>.Unregister(this);
        InteractionReservation.ReleaseAllForProvider(this);
    }

    private void OnDestroy()
    {
        InteractionReservation.ReleaseAllForProvider(this);
    }

    private void OnValidate()
    {
        if (interactions == null)
            interactions = new List<InteractionDefinition>();

        interactions.RemoveAll(item => item == null);
    }
}