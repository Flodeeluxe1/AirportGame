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
        ProcessingDepartureJourney,
        AtTerminal,
        WalkingToCarPickup,
        AtCarPickup,
        OnCar,
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
    private PassengerJourneyController passengerJourney;
    private Vector3 departureGatePosition;
    private PlacedBuilding departureGateFootprint;
    private double nextDepartureStationRetryTime;
    private Vector3 busStopPosition;
    private PlacedBuilding busStopGateFootprint;
    private PlacedBuilding waitingBusStopFootprint;
    private Renderer[] passengerRenderers;
    private bool[] rendererEnabledBeforeBoarding;
    private Collider[] passengerColliders;
    private bool[] colliderEnabledBeforeBoarding;

    public event System.Action<AirlinePassengerAgent> ReachedBusStop;
    public event System.Action<AirlinePassengerAgent> ReachedCarPickup;

    public string FlightOperationId => flightOperationId;
    public JourneyState State => state;
    public UtilityNeeds Needs => needs;
    public PedestrianMovementController Movement => movement;
    public bool CanBoardBus
    {
        get
        {
            if (state != JourneyState.WaitingForBus || movement == null ||
                movement.IsMoving || (interactions != null && interactions.IsHandlingInteraction))
                return false;

            Vector3 current = transform.position;
            Vector3 stop = busStopPosition;
            current.y = 0f;
            stop.y = 0f;
            return (current - stop).sqrMagnitude <= 0.25f;
        }
    }

    private void Awake()
    {
        CacheComponents();
    }

    private void OnEnable()
    {
        CacheComponents();
        if (movement != null)
            movement.Arrived += HandleMovementArrived;
        if (interactions != null)
        {
            interactions.MandatoryInteractionCompleted += HandleMandatoryInteractionCompleted;
            interactions.MandatoryInteractionFailed += HandleMandatoryInteractionFailed;
        }
    }

    private void OnDisable()
    {
        if (movement != null)
            movement.Arrived -= HandleMovementArrived;
        if (interactions != null)
        {
            interactions.MandatoryInteractionCompleted -= HandleMandatoryInteractionCompleted;
            interactions.MandatoryInteractionFailed -= HandleMandatoryInteractionFailed;
        }
    }

    private void Update()
    {
        if (state == JourneyState.ProcessingDepartureJourney &&
            (interactions == null || !interactions.IsHandlingInteraction) &&
            GameTimeManager.SimulationTime >= nextDepartureStationRetryTime)
        {
            BeginNextDepartureStage();
        }
    }

    private void HandleMovementArrived()
    {
        if (state == JourneyState.WalkingToBusStop)
        {
            if (interactions != null && interactions.IsHandlingInteraction)
                return;

            BeginWaitingForBus();
            ReachedBusStop?.Invoke(this);
            return;
        }

        if (state == JourneyState.WalkingToTerminal)
        {
            if (interactions != null && interactions.IsHandlingInteraction)
                return;

            if (passengerJourney != null &&
                passengerJourney.Direction == PassengerJourneyController.JourneyDirection.Departure)
            {
                if (passengerJourney.CurrentStage ==
                    PassengerJourneyController.JourneyStage.TravellingToBoardingGate)
                    passengerJourney.ReachBoardingGate();
            }

            state = JourneyState.AtTerminal;
            if (interactions != null)
            {
                interactions.StopJourneyInteractions();
                interactions.SetExternallyControlled(false);
            }
            return;
        }

        if (state == JourneyState.WalkingToCarPickup)
        {
            if (interactions != null && interactions.IsHandlingInteraction)
                return;

            state = JourneyState.AtCarPickup;
            if (interactions != null)
            {
                interactions.StopJourneyInteractions();
                interactions.SetExternallyControlled(true);
            }
            if (passengerJourney != null)
                passengerJourney.ReachTransport();
            ReachedCarPickup?.Invoke(this);
        }
    }

    private void HandleMandatoryInteractionCompleted(
        InteractionProvider provider,
        InteractionDefinition definition)
    {
        if (passengerJourney == null ||
            passengerJourney.Direction != PassengerJourneyController.JourneyDirection.Departure)
            return;

        switch (passengerJourney.CurrentStage)
        {
            case PassengerJourneyController.JourneyStage.CheckIn:
                passengerJourney.CompleteCheckIn();
                break;
            case PassengerJourneyController.JourneyStage.BaggageDrop:
                passengerJourney.CompleteBaggageDrop();
                break;
            case PassengerJourneyController.JourneyStage.BodySecurity:
                passengerJourney.CompleteBodySecurity();
                break;
            case PassengerJourneyController.JourneyStage.LuggageSecurity:
                passengerJourney.CompleteLuggageSecurity();
                break;
            default:
                return;
        }

        BeginNextDepartureStage();
    }

    private void HandleMandatoryInteractionFailed(
        InteractionProvider provider,
        InteractionDefinition definition,
        string reason)
    {
        if (passengerJourney == null ||
            passengerJourney.Direction != PassengerJourneyController.JourneyDirection.Departure)
            return;

        nextDepartureStationRetryTime = GameTimeManager.SimulationTime + 10d;
        Debug.LogWarning(
            $"AirlinePassengerAgent: Could not complete departure stage " +
            $"{passengerJourney.CurrentStage} for {flightOperationId}: {reason}",
            this);
    }

    private void BeginNextDepartureStage()
    {
        if (passengerJourney == null || interactions == null ||
            movement == null || state == JourneyState.Completed ||
            state == JourneyState.Abandoned)
            return;

        PassengerJourneyStationRole requiredRole;

        switch (passengerJourney.CurrentStage)
        {
            case PassengerJourneyController.JourneyStage.CheckIn:
                requiredRole = PassengerJourneyStationRole.AirlineCheckIn;
                break;
            case PassengerJourneyController.JourneyStage.BaggageDrop:
                requiredRole = PassengerJourneyStationRole.BaggageDrop;
                break;
            case PassengerJourneyController.JourneyStage.BodySecurity:
                requiredRole = PassengerJourneyStationRole.BodySecurity;
                break;
            case PassengerJourneyController.JourneyStage.LuggageSecurity:
                requiredRole = PassengerJourneyStationRole.LuggageSecurity;
                break;
            case PassengerJourneyController.JourneyStage.DeparturesArea:
                if (!passengerJourney.BeginBoardingGateJourney())
                    return;

                state = JourneyState.WalkingToTerminal;
                if (!movement.MoveToAllowingOccupiedEndpoints(
                        departureGatePosition, null, departureGateFootprint))
                {
                    passengerJourney.ReachBoardingGate();
                    state = JourneyState.AtTerminal;
                    Debug.LogWarning(
                        $"AirlinePassengerAgent: No route to boarding gate for {flightOperationId}.",
                        this);
                }
                return;
            default:
                return;
        }

        if (!PassengerJourneyController.TryFindNearestStation(
                requiredRole, transform.position,
                out InteractionProvider provider,
                out PlacedBuilding stationBuilding))
        {
            nextDepartureStationRetryTime = GameTimeManager.SimulationTime + 10d;
            Debug.LogWarning(
                $"AirlinePassengerAgent: No available {requiredRole} station for {flightOperationId}.",
                this);
            return;
        }

        InteractionDefinition selectedDefinition = null;
        IReadOnlyList<InteractionDefinition> definitions = provider.Interactions;
        for (int i = 0; i < definitions.Count; i++)
        {
            if (provider.CanOffer(definitions[i]))
            {
                selectedDefinition = definitions[i];
                break;
            }
        }

        if (selectedDefinition == null ||
            !interactions.BeginMandatoryInteraction(
                provider, selectedDefinition, null, stationBuilding))
        {
            nextDepartureStationRetryTime = GameTimeManager.SimulationTime + 10d;
            Debug.LogWarning(
                $"AirlinePassengerAgent: Could not start {requiredRole} interaction for {flightOperationId}.",
                this);
            return;
        }

        state = JourneyState.ProcessingDepartureJourney;
        nextDepartureStationRetryTime = double.PositiveInfinity;
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
        if (passengerJourney == null)
            passengerJourney = GetComponent<PassengerJourneyController>();

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
    public bool Initialize(
        string operationId,
        PassengerIdentity.TransportMode transportMode,
        bool isArrivingPassenger = false)
    {
        CacheComponents();

        if (string.IsNullOrWhiteSpace(operationId) ||
            movement == null || needs == null || identity == null)
        {
            return false;
        }

        flightOperationId = operationId;
        identity.Initialize(transportMode);
        if (passengerJourney == null)
            passengerJourney = gameObject.AddComponent<PassengerJourneyController>();

        if (isArrivingPassenger)
            passengerJourney.InitializeArrival(false);
        else
            passengerJourney.InitializeDeparture(false);

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

        if (passengerJourney != null &&
            !passengerJourney.BeginTransport(
                PassengerJourneyController.TransportDestination.BusStop))
            return false;

        busStopPosition = stopPosition;
        busStopGateFootprint = gateFootprint;
        waitingBusStopFootprint = busStopFootprint;

        if (interactions != null)
            interactions.SetJourneyInteractions(
                true, stopPosition, gateFootprint, busStopFootprint);

        state = JourneyState.WalkingToBusStop;
        if (!movement.MoveToAllowingOccupiedEndpoints(
                stopPosition, gateFootprint, busStopFootprint))
        {
            state = JourneyState.Uninitialized;
            if (passengerJourney != null)
                passengerJourney.CancelTransport();
            if (interactions != null)
            {
                interactions.StopJourneyInteractions();
                interactions.SetExternallyControlled(false);
            }
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
            if (passengerJourney != null)
                passengerJourney.ReachTransport();

            // Keep need-driven interactions active while the passenger waits.
            // Their mandatory destination remains this exact bus-stop position.
            ResumeWaitingForBusNeeds();
        }
    }

    public void ResumeWaitingForBusNeeds()
    {
        if (state != JourneyState.WaitingForBus || interactions == null)
            return;

        interactions.SetJourneyInteractions(
            true,
            busStopPosition,
            busStopGateFootprint,
            waitingBusStopFootprint);
    }

    public void PauseWaitingForBusNeedsForBoarding()
    {
        if (state != JourneyState.WaitingForBus || interactions == null)
            return;

        interactions.StopJourneyInteractions();
        interactions.SetExternallyControlled(true);
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
            if (passengerJourney != null)
                passengerJourney.CompleteTransportJourney();
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
        if (passengerJourney != null)
            passengerJourney.CompleteTransportJourney();
        return true;
    }

    /// <summary>
    /// Starts an outbound passenger at a car drop-off point and walks them
    /// towards their assigned aircraft gate/terminal destination.
    /// </summary>
    public bool WalkToTerminalFromCar(
        Vector3 terminalPosition,
        PlacedBuilding carFootprint = null,
        PlacedBuilding gateFootprint = null)
    {
        CacheComponents();
        if (movement == null || state == JourneyState.OnCar ||
            state == JourneyState.Completed || state == JourneyState.Abandoned)
            return false;

        departureGatePosition = terminalPosition;
        departureGateFootprint = gateFootprint;

        if (passengerJourney != null &&
            passengerJourney.Direction == PassengerJourneyController.JourneyDirection.Departure)
        {
            if (interactions != null)
                interactions.SetExternallyControlled(false);

            state = JourneyState.ProcessingDepartureJourney;
            nextDepartureStationRetryTime = GameTimeManager.SimulationTime;
            BeginNextDepartureStage();
            return state == JourneyState.ProcessingDepartureJourney;
        }

        if (interactions != null)
            interactions.SetJourneyInteractions(
                true, terminalPosition, carFootprint, gateFootprint);

        state = JourneyState.WalkingToTerminal;
        if (!movement.MoveToAllowingOccupiedEndpoints(
                terminalPosition, carFootprint, gateFootprint))
        {
            state = JourneyState.Uninitialized;
            if (interactions != null)
            {
                interactions.StopJourneyInteractions();
                interactions.SetExternallyControlled(false);
            }
            return false;
        }

        return true;
    }

    /// <summary>Walks from the arrival gate to the assigned parked pickup car.</summary>
    public bool WalkToCarPickup(
        Vector3 pickupPosition,
        PlacedBuilding gateFootprint = null,
        PlacedBuilding parkingFootprint = null)
    {
        CacheComponents();
        if (movement == null || state == JourneyState.OnCar ||
            state == JourneyState.Completed || state == JourneyState.Abandoned)
            return false;

        if (passengerJourney != null &&
            !passengerJourney.BeginTransport(
                PassengerJourneyController.TransportDestination.PickupCar))
            return false;

        if (interactions != null)
            interactions.SetJourneyInteractions(
                true, pickupPosition, gateFootprint, parkingFootprint);

        state = JourneyState.WalkingToCarPickup;
        if (!movement.MoveToAllowingOccupiedEndpoints(
                pickupPosition, gateFootprint, parkingFootprint))
        {
            state = JourneyState.Uninitialized;
            if (passengerJourney != null)
                passengerJourney.CancelTransport();
            if (interactions != null)
            {
                interactions.StopJourneyInteractions();
                interactions.SetExternallyControlled(false);
            }
            return false;
        }
        return true;
    }

    /// <summary>Boards a parked pickup car and is hidden while travelling home.</summary>
    public bool BoardCar(Transform carTransform)
    {
        CacheComponents();
        if (carTransform == null || state != JourneyState.AtCarPickup)
            return false;

        if (movement != null && movement.IsMoving)
            movement.Stop();
        if (interactions != null)
            interactions.SetExternallyControlled(true);

        transform.SetParent(carTransform, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        HideWhileOnBus();
        state = JourneyState.OnCar;
        if (passengerJourney != null)
            passengerJourney.CompleteTransportJourney();
        return true;
    }

    /// <summary>Completes the passenger's car journey when the pickup car exits.</summary>
    public void CompleteReturnCarJourney()
    {
        if (state != JourneyState.OnCar)
            return;

        transform.SetParent(null, true);
        state = JourneyState.Completed;
        Destroy(gameObject);
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

        departureGatePosition = terminalPosition;
        departureGateFootprint = gateFootprint;

        if (passengerJourney != null &&
            passengerJourney.Direction == PassengerJourneyController.JourneyDirection.Departure)
        {
            if (interactions != null)
                interactions.SetExternallyControlled(false);

            state = JourneyState.ProcessingDepartureJourney;
            nextDepartureStationRetryTime = GameTimeManager.SimulationTime;
            BeginNextDepartureStage();
            return true;
        }

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
