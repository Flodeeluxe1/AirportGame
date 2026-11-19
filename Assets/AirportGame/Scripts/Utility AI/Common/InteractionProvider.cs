using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

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
    [FormerlySerializedAs("interactionPoint")]
    [SerializeField] private Transform interactionPoint;
    [SerializeField] private bool interactionsEnabled = true;

    private InteractionQueue interactionQueue;
    private bool interactionPointResolved;
    private Transform animationPoint;
    private bool animationPointResolved;
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

    private Transform InteractionPointTransform
    {
        get
        {
            if (!interactionPointResolved)
            {
                interactionPointResolved = true;
                if (interactionPoint == null)
                    interactionPoint = transform.Find("InteractionPoint");

                // Support existing prefabs until their child is renamed.
                if (interactionPoint == null)
                    interactionPoint = transform.Find("interactionPoint");
            }

            return interactionPoint;
        }
    }

    private Transform AnimationPointTransform
    {
        get
        {
            if (!animationPointResolved)
            {
                animationPointResolved = true;
                animationPoint = transform.Find("AnimationPoint");
            }

            return animationPoint;
        }
    }

    /// <summary>World-space position the agent walks to before using this object.</summary>
    public Vector3 InteractionPosition
    {
        get
        {
            Transform point = InteractionPointTransform;
            if (point != null)
                return point.position;

            if (!gridManagerLookupAttempted)
            {
                cachedGridManager = FindAnyObjectByType<GridManager>();
                gridManagerLookupAttempted = true;
            }

            float gridUnit = cachedGridManager != null
                ? cachedGridManager.CellSize
                : 1f;

            return transform.position + transform.forward * gridUnit;
        }
    }

    /// <summary>
    /// World-space position where the agent performs the interaction.
    /// Falls back to the interaction point when no AnimationPoint exists.
    /// </summary>
    public Vector3 AnimationPosition
    {
        get
        {
            Transform point = AnimationPointTransform;
            if (point != null)
                return point.position;

            return InteractionPosition;
        }
    }

    /// <summary>Rotation used at the reachable interaction approach point.</summary>
    public Quaternion InteractionRotation
    {
        get
        {
            Transform point = InteractionPointTransform;
            return point != null ? point.rotation : transform.rotation;
        }
    }

    /// <summary>Rotation used when the agent reaches the animation position.</summary>
    public Quaternion AnimationRotation
    {
        get
        {
            Transform point = AnimationPointTransform;
            if (point != null)
                return point.rotation;

            Transform approachPoint = InteractionPointTransform;
            return approachPoint != null ? approachPoint.rotation : transform.rotation;
        }
    }

    private void OnEnable()
    {
        UtilityAISpatialRegistry<InteractionProvider>.Register(this);
    }

    /// <summary>
    /// Adds an interaction definition at runtime when a system configures
    /// a provider dynamically (for example, reservable classroom seats).
    /// </summary>
    public void AddInteractionDefinition(InteractionDefinition definition)
    {
        if (definition == null)
            return;

        if (interactions == null)
            interactions = new List<InteractionDefinition>();

        if (!interactions.Contains(definition))
            interactions.Add(definition);
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