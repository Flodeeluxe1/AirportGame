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
        OnAircraft,
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
    private Renderer[] passengerRenderers;
    private bool[] rendererEnabledBeforeBoarding;
    private Collider[] passengerColliders;
    private bool[] colliderEnabledBeforeBoarding;

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

        if (passengerRenderers == null)
        {
            passengerRenderers = GetComponentsInChildren<Renderer>(true);
            rendererEnabledBeforeBoarding = new bool[passengerRenderers.Length];
        }

        if (passengerColliders == null)
        {
            passengerColliders = GetComponentsInChildren<Collider>(true);
            colliderEnabledBeforeBoarding = new bool[passengerColliders.Length];
        }
    }

    private void HideWhileOnBus()
    {
        for (int i = 0; i < passengerRenderers.Length; i++)
        {
            Renderer passengerRenderer = passengerRenderers[i];
            if (passengerRenderer == null)
                continue;

            rendererEnabledBeforeBoarding[i] = passengerRenderer.enabled;
            passengerRenderer.enabled = false;
        }

        for (int i = 0; i < passengerColliders.Length; i++)
        {
            Collider passengerCollider = passengerColliders[i];
            if (passengerCollider == null)
                continue;

            colliderEnabledBeforeBoarding[i] = passengerCollider.enabled;
            passengerCollider.enabled = false;
        }
    }

    private void RestoreAfterBus()
    {
        for (int i = 0; i < passengerRenderers.Length; i++)
            if (passengerRenderers[i] != null)
                passengerRenderers[i].enabled = rendererEnabledBeforeBoarding[i];

        for (int i = 0; i < passengerColliders.Length; i++)
            if (passengerColliders[i] != null)
                passengerColliders[i].enabled = colliderEnabledBeforeBoarding[i];
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

    public bool WalkToBusStop(
        Vector3 stopPosition,
        PlacedBuilding gateFootprint = null,
        PlacedBuilding busStopFootprint = null)
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
        if (!movement.MoveToAllowingOccupiedEndpoints(
                stopPosition, gateFootprint, busStopFootprint))
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

        // Passengers are represented by the bus while travelling. Parent at
        // the bus prefab root pivot instead of preserving their queue positions.
        transform.SetParent(busTransform, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        HideWhileOnBus();

        state = JourneyState.OnBus;
        return true;
    }

    /// <summary>Boards the assigned aircraft and becomes invisible for the flight.</summary>
    public bool BoardAircraft(Transform aircraftTransform)
    {
        CacheComponents();
        if (aircraftTransform == null ||
            state != JourneyState.AtTerminal)
            return false;

        if (movement != null && movement.IsMoving)
            movement.Stop();

        if (interactions != null)
            interactions.SetExternallyControlled(true);

        transform.SetParent(aircraftTransform, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        HideWhileOnBus();

        state = JourneyState.OnAircraft;
        return true;
    }

    /// <summary>Leaves the aircraft, becomes visible, and returns to pedestrian control.</summary>
    public bool DeboardAircraft(Vector3 disembarkPosition)
    {
        CacheComponents();
        if (state != JourneyState.OnAircraft)
            return false;

        transform.SetParent(null, true);
        RestoreAfterBus();
        transform.position = disembarkPosition;

        if (movement != null)
            movement.AlignFeetToGround(disembarkPosition);

        state = JourneyState.Uninitialized;
        if (interactions != null)
            interactions.SetExternallyControlled(false);

        return true;
    }

    /// <summary>Leaves the bus and resumes normal visitor behaviour while walking to the flight terminal.</summary>
    public bool DisembarkToTerminal(Vector3 terminalPosition)
    {
        return DisembarkToTerminal(transform.position, terminalPosition);
    }

    public bool DisembarkToTerminal(
        Vector3 disembarkPosition,
        Vector3 terminalPosition,
        PlacedBuilding busStopFootprint = null,
        PlacedBuilding gateFootprint = null)
    {
        CacheComponents();
        if (state != JourneyState.OnBus || movement == null)
            return false;

        // Detach before doing anything that can fail. A passenger who has
        // left the bus must never be re-parented to it: otherwise the bus
        // carries the passenger to its road exit and destroys the passenger.
        transform.SetParent(null, true);
        RestoreAfterBus();

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
        if (!movement.MoveToAllowingOccupiedEndpoints(
                terminalPosition, busStopFootprint, gateFootprint) &&
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
