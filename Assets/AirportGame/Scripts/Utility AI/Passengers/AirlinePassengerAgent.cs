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
        if (state != JourneyState.WalkingToBusStop)
            return;

        BeginWaitingForBus();
        ReachedBusStop?.Invoke(this);
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

        state = JourneyState.WalkingToBusStop;
        if (!movement.MoveTo(stopPosition))
        {
            state = JourneyState.Uninitialized;
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
        CacheComponents();
        if (state != JourneyState.OnBus || movement == null)
            return false;

        Transform busTransform = transform.parent;
        transform.SetParent(null, true);
        if (interactions != null)
            interactions.SetExternallyControlled(false);

        if (!movement.MoveTo(terminalPosition))
        {
            transform.SetParent(busTransform, true);
            if (interactions != null)
                interactions.SetExternallyControlled(true);
            return false;
        }

        state = JourneyState.WalkingToTerminal;
        return true;
    }

    public void BeginWalkingToTerminal(Vector3 terminalPosition)
    {
        CacheComponents();

        if (movement == null || state != JourneyState.OnBus)
            return;

        gameObject.SetActive(true);
        if (movement.MoveTo(terminalPosition))
            state = JourneyState.WalkingToTerminal;
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
