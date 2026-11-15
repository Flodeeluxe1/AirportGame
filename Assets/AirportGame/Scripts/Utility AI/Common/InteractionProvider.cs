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

    public IReadOnlyList<InteractionDefinition> Interactions => interactions;
    /// <summary>
    /// World-space position where an agent should approach this object.
    /// Uses the assigned point when present; otherwise one grid cell forward
    /// from the object's origin.
    /// </summary>
    public Vector3 InteractionPosition
    {
        get
        {
            if (interactionPoint != null)
                return interactionPoint.position;

            GridManager gridManager = FindFirstObjectByType<GridManager>();
            float gridUnit = gridManager != null
                ? gridManager.CellSize
                : 1f;

            return transform.position + transform.forward * gridUnit;
        }
    }
    public bool InteractionsEnabled =>
        interactionsEnabled && isActiveAndEnabled && gameObject.activeInHierarchy;

    /// <summary>
    /// Returns whether this provider offers the definition and has a free slot.
    /// Range and agent-specific requirements are checked by the AI decision system.
    /// </summary>
    public bool CanOffer(InteractionDefinition definition)
    {
        return InteractionsEnabled &&
               definition != null &&
               interactions != null &&
               interactions.Contains(definition) &&
               InteractionReservation.HasAvailableSlot(this, definition);
    }

    /// <summary>
    /// Claims one usage slot for the supplied agent.
    /// The agent should release the reservation when the interaction ends or is cancelled.
    /// </summary>
    public bool TryReserve(
        MonoBehaviour agent,
        InteractionDefinition definition)
    {
        if (agent == null || !CanOffer(definition))
            return false;

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