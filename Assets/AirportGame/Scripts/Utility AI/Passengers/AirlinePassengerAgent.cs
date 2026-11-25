using UnityEngine;

/// <summary>
/// Controls an airline passenger's scheduled-flight journey while reusing
/// the sightseeing passenger prefab's visitor, environment and needs systems.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PedestrianMovementController))]
[RequireComponent(typeof(UtilityNeeds))]
[RequireComponent(typeof(PassengerIdentity))]
public class AirlinePassengerAgent : MonoBehaviour
{
    public enum JourneyState
    {
        Uninitialized,
        WalkingToBusStop,
        WaitingForBus,
        Boarding,
        OnBus,
        WalkingToTerminal,
        AtTerminal,
        Completed,
        Abandoned
    }

    [Header("Runtime")]
    [SerializeField] private string flightOperationId;
    [SerializeField] private JourneyState state;

    private PedestrianMovementController movement;
    private UtilityNeeds needs;
    private PassengerIdentity identity;
    private InteractionController interactions;
    private EnvironmentScoreMonitor environmentMonitor;
    private AirportVisitorExperience visitorExperience;

    public event System.Action<AirlinePassengerAgent> ReachedBusStop;

    public string FlightOperationId => flightOperationId;
    public JourneyState State => state;
    public UtilityNeeds Needs => needs;
    public PedestrianMovementController Movement => movement;

    private void Awake()
    {
        CacheComponents();
    }

    private void OnEnable()
    {
        CacheComponents();
        if (movement != null)
            movement.Arrived += HandleMovementArrived;
    }

    private void OnDisable()
    {
        if (movement != null)
            movement.Arrived -= HandleMovementArrived;
    }

    private void HandleMovementArrived()
    {
        if (state == JourneyState.WalkingToBusStop)
        {
            BeginWaitingForBus();
            ReachedBusStop?.Invoke(this);
            return;
        }

        if (state == JourneyState.WalkingToTerminal)
        {
            state = JourneyState.AtTerminal;
            if (interactions != null)
                interactions.SetExternallyControlled(false);
        }
    }

    private void CacheComponents()
    {
        if (movement == null)
            movement = GetComponent<PedestrianMovementController>();
        if (needs == null)
            needs = GetComponent<UtilityNeeds>();
        if (identity == null)
            identity = GetComponent<PassengerIdentity>();
        if (interactions == null)
            interactions = GetComponent<InteractionController>();
        if (environmentMonitor == null)
            environmentMonitor = GetComponent<EnvironmentScoreMonitor>();
        if (visitorExperience == null)
            visitorExperience = GetComponent<AirportVisitorExperience>();
    }

    /// <summary>
    /// Initializes a passenger for one scheduled flight. The operation ID
    /// prevents passengers from different flights being mixed in a bus group.
    /// </summary>
    public bool Initialize(string operationId, PassengerIdentity.TransportMode transportMode)
    {
        CacheComponents();

        if (string.IsNullOrWhiteSpace(operationId) ||
            movement == null || needs == null || identity == null)
        {
            return false;
        }

        flightOperationId = operationId;
        identity.Initialize(transportMode);
        needs.ResetForNewVisit();

        // These visitor systems must start with the passenger, just as they do
        // for other airport visitors. Enabling invokes their normal OnEnable
        // registration and experience-tracking setup.
        if (interactions != null)
            interactions.enabled = true;
        if (environmentMonitor != null)
            environmentMonitor.enabled = true;
        if (visitorExperience != null)
            visitorExperience.enabled = true;

        state = JourneyState.Uninitialized;
        return true;
    }

    public bool WalkToBusStop(Vector3 stopPosition)
    {
        CacheComponents();

        if (movement == null || state == JourneyState.OnBus ||
            state == JourneyState.Completed || state == JourneyState.Abandoned)
        {
            return false;
        }

        if (interactions != null)
            interactions.SetExternallyControlled(true);

        state = JourneyState.WalkingToBusStop;
        if (!movement.MoveTo(stopPosition))
        {
            state = JourneyState.Uninitialized;
            if (interactions != null)
                interactions.SetExternallyControlled(false);
            return false;
        }

        return true;
    }

    public void BeginWaitingForBus()
    {
        if (state == JourneyState.WalkingToBusStop ||
            state == JourneyState.Uninitialized)
        {
            if (movement != null && movement.IsMoving)
                movement.Stop();

            state = JourneyState.WaitingForBus;
            if (interactions != null)
                interactions.SetExternallyControlled(true);
        }
    }

    public void BeginBoarding()
    {
        if (state == JourneyState.WaitingForBus)
            state = JourneyState.Boarding;
    }

    public void SetOnBus()
    {
        if (state == JourneyState.Boarding)
        {
            if (movement != null && movement.IsMoving)
                movement.Stop();

            state = JourneyState.OnBus;
        }
    }

    /// <summary>Boards a bus, including passengers spawned directly into its departing manifest.</summary>
    public bool BoardBus(Transform busTransform)
    {
        CacheComponents();
        if (busTransform == null || state == JourneyState.Completed ||
            state == JourneyState.Abandoned || state == JourneyState.OnBus)
            return false;

        if (movement != null && movement.IsMoving)
            movement.Stop();

        if (interactions != null)
            interactions.SetExternallyControlled(true);

        transform.SetParent(busTransform, true);
        state = JourneyState.OnBus;
        return true;
    }

    /// <summary>Leaves the bus and resumes normal visitor behaviour while walking to the flight terminal.</summary>
    public bool DisembarkToTerminal(Vector3 terminalPosition)
    {
        return DisembarkToTerminal(transform.position, terminalPosition);
    }

    public bool DisembarkToTerminal(
        Vector3 disembarkPosition,
        Vector3 terminalPosition)
    {
        CacheComponents();
        if (state != JourneyState.OnBus || movement == null)
            return false;

        // Detach before doing anything that can fail. A passenger who has
        // left the bus must never be re-parented to it: otherwise the bus
        // carries the passenger to its road exit and destroys the passenger.
        transform.SetParent(null, true);

        // Pedestrian pathfinding starts from walkable pedestrian space, not
        // the road cell at the bus's transform. Place the passenger at the
        // stop's pedestrian approach area before requesting the gate route.
        transform.position = disembarkPosition;

        // Match Flight School student spawning: the pathfinder supplies the
        // walkable ground point, then the body collider half-height raises
        // the passenger pivot clear of the floor.
        Collider passengerCollider = GetComponent<Collider>();
        if (passengerCollider == null)
            passengerCollider = GetComponentInChildren<Collider>();

        if (passengerCollider != null)
            transform.position += Vector3.up * passengerCollider.bounds.extents.y;

        if (interactions != null)
            interactions.SetExternallyControlled(true);
        state = JourneyState.WalkingToTerminal;

        // Prefer a direct route. If the exact gate point is blocked, let the
        // pathfinder search for a nearby walkable approach instead.
        if (!movement.MoveTo(terminalPosition) &&
            !movement.TryMoveToNearby(terminalPosition, disembarkPosition))
        {
            Debug.LogWarning(
                $"AirlinePassengerAgent: Passenger {flightOperationId} was detached at the bus stop but no pedestrian route to the terminal could be found.",
                this);
        }

        // Disembarking succeeds even if pathfinding fails: the passenger is
        // no longer onboard and must not be carried away with the bus.
        return true;
    }

    public bool RedirectToTerminal(Vector3 terminalPosition)
    {
        CacheComponents();
        if (movement == null ||
            (state != JourneyState.WalkingToTerminal && state != JourneyState.AtTerminal))
            return false;

        JourneyState previousState = state;
        state = JourneyState.WalkingToTerminal;
        if (interactions != null)
            interactions.SetExternallyControlled(true);

        if (!movement.MoveTo(terminalPosition))
        {
            state = previousState;
            if (interactions != null && previousState == JourneyState.AtTerminal)
                interactions.SetExternallyControlled(false);
            return false;
        }

        return true;
    }

    public void BeginWalkingToTerminal(Vector3 terminalPosition)
    {
        if (state == JourneyState.OnBus)
            DisembarkToTerminal(terminalPosition);
    }

    public void CompleteJourney()
    {
        if (movement != null && movement.IsMoving)
            movement.Stop();

        state = JourneyState.Completed;
    }

    /// <summary>Completes a homebound passenger's bus trip as the bus leaves the airport road network.</summary>
    public void CompleteReturnBusJourney()
    {
        if (state != JourneyState.OnBus)
            return;

        transform.SetParent(null, true);
        if (interactions != null)
            interactions.SetExternallyControlled(false);
        CompleteJourney();
        Destroy(gameObject);
    }

    public void AbandonJourney()
    {
        if (movement != null && movement.IsMoving)
            movement.Stop();

        state = JourneyState.Abandoned;
    }
}
