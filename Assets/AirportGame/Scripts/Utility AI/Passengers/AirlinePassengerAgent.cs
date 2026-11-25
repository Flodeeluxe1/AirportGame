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

    public string FlightOperationId => flightOperationId;
    public JourneyState State => state;
    public UtilityNeeds Needs => needs;
    public PedestrianMovementController Movement => movement;

    private void Awake()
    {
        CacheComponents();
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

        if (!movement.MoveTo(stopPosition))
            return false;

        state = JourneyState.WalkingToBusStop;
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

    public void AbandonJourney()
    {
        if (movement != null && movement.IsMoving)
            movement.Stop();

        state = JourneyState.Abandoned;
    }
}
