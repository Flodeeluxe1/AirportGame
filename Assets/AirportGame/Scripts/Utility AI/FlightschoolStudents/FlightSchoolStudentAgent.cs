using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Movement and seat reservation for one Flight School student.
/// The student uses the shared pedestrian movement and interaction-point systems.
/// </summary>
[DisallowMultipleComponent]
public class FlightSchoolStudentAgent : MonoBehaviour
{
    private PedestrianMovementController movement;
    private TrafficTestVehicle vehicle;
    private Renderer[] studentRenderers;
    private Vector3 hiddenLessonPosition;
    private Quaternion hiddenLessonRotation;
    private bool hasHiddenLessonPosition;

    private InteractionProvider reservedSeat;
    private InteractionDefinition seatInteraction;

    private bool walkingToSeat;
    private bool walkingToAircraft;
    private bool walkingToCar;
    private bool departureStarted;
    private bool groupConversationActive;
    private bool instructorConversationActive;
    private FlightSchoolStudentGroupConversationController groupConversationController;
    [Header("Diagnostics")]
    [SerializeField] private bool logMovementArrivals;

    [Header("Student Needs")]
    [Min(0.1f)]
    [SerializeField] private float needDecisionInterval = 2f;
    [Min(1f)]
    [SerializeField] private float needInteractionSearchRadius = 100f;
    [Min(0f)]
    [SerializeField] private float needInteractionTimeCost = 0.02f;
    [Min(0.05f)]
    [SerializeField] private float queuePositionRefreshInterval = 0.25f;

    private struct NeedInteractionCandidate
    {
        public InteractionProvider provider;
        public InteractionDefinition definition;
        public float score;
        public float travelTime;
    }

    private readonly List<NeedInteractionCandidate> needShortlist =
        new List<NeedInteractionCandidate>();
    private readonly List<InteractionProvider> nearbyNeedProviders =
        new List<InteractionProvider>(64);
    private readonly List<UtilityNeeds.NeedType> replenishingNeeds =
        new List<UtilityNeeds.NeedType>();

    private UtilityNeeds needs;
    private PedestrianAccessProfile accessProfile;
    private RoomPurposeController roomPurposeController;
    private InteractionProvider needProvider;
    private InteractionDefinition needInteraction;
    private InteractionProvider queuedNeedProvider;
    private InteractionDefinition queuedNeedInteraction;
    private Coroutine needInteractionRoutine;
    private bool walkingToNeedInteraction;
    private bool performingNeedInteraction;
    private bool atSeat;
    private float nextNeedDecisionTime;
    private float nextQueuePositionRefresh;
    private Vector3 lastQueueDestination;


    public event Action<FlightSchoolStudentAgent> SeatReached;
    public event Action<FlightSchoolStudentAgent> AircraftReached;
    public event Action<FlightSchoolStudentAgent> CarReached;
    public event Action<FlightSchoolStudentAgent> PathFailed;

    public TrafficTestVehicle Vehicle => vehicle;
    public InteractionProvider ReservedSeat => reservedSeat;
    public bool IsSeated => atSeat && reservedSeat != null &&
        !walkingToSeat && !walkingToAircraft && !walkingToCar &&
        !walkingToNeedInteraction && !performingNeedInteraction &&
        !instructorConversationActive;

    public bool IsInstructorConversationActive => instructorConversationActive;

    public void SetInstructorConversationActive(bool active)
    {
        instructorConversationActive = active;
    }

    public void Initialize(TrafficTestVehicle assignedVehicle, Vector2Int classroomAnchor)
    {
        vehicle = assignedVehicle;
        instructorConversationActive = false;
        needs = GetComponent<UtilityNeeds>();
        accessProfile = GetComponent<PedestrianAccessProfile>();
        roomPurposeController = FindAnyObjectByType<RoomPurposeController>();
        studentRenderers = GetComponentsInChildren<Renderer>(true);

        // Students follow the school timetable, not the hobby-pilot Utility AI.
        DisablePilotAutonomy<PilotInteractionController>();
        DisablePilotAutonomy<PilotWanderingController>();
        DisablePilotAutonomy<PilotSocialInteractionController>();
        DisablePilotAutonomy<PilotSocialSeatingController>();
        DisablePilotAutonomy<PilotGroupConversationController>();
        DisablePilotAutonomy<PilotDepartureController>();

        movement = GetComponent<PedestrianMovementController>();
        if (movement == null)
            movement = gameObject.AddComponent<PedestrianMovementController>();

        movement.Arrived -= HandleMovementArrived;
        movement.PathFailed -= HandlePathFailed;
        movement.Arrived += HandleMovementArrived;
        movement.PathFailed += HandlePathFailed;

        groupConversationController = GetComponent<FlightSchoolStudentGroupConversationController>();
        if (groupConversationController == null)
            groupConversationController = gameObject.AddComponent<FlightSchoolStudentGroupConversationController>();
        groupConversationController.Initialize(classroomAnchor);
    }

    /// <summary>Hides or restores the student model while the lesson is in progress.</summary>
    public void SetStudentVisible(bool visible)
    {
        if (studentRenderers == null)
            studentRenderers = GetComponentsInChildren<Renderer>(true);

        if (!visible)
        {
            // Keep the student at the aircraft boarding point throughout the
            // lesson. Capture the pose only once in case the hide call repeats.
            if (!hasHiddenLessonPosition)
            {
                hiddenLessonPosition = transform.position;
                hiddenLessonRotation = transform.rotation;
                hasHiddenLessonPosition = true;
            }
        }
        else if (hasHiddenLessonPosition)
        {
            // Restore the boarding point before showing the student again.
            // ReturnToSeat can then pathfind from the aircraft to the classroom.
            transform.SetPositionAndRotation(
                hiddenLessonPosition,
                hiddenLessonRotation);
            hasHiddenLessonPosition = false;
        }

        foreach (Renderer studentRenderer in studentRenderers)
        {
            if (studentRenderer != null)
                studentRenderer.enabled = visible;
        }
    }

    private void DisablePilotAutonomy<T>() where T : Behaviour
    {
        T behaviour = GetComponent<T>();

        if (behaviour != null)
            behaviour.enabled = false;
    }

    public bool AssignSeat(
        InteractionProvider provider,
        InteractionDefinition interaction)
    {
        if (provider == null || interaction == null || movement == null)
        {
            Debug.LogError(
                $"FlightSchoolStudentAgent {name}: Cannot assign chair. " +
                $"ProviderMissing={provider == null}, InteractionMissing={interaction == null}, " +
                $"MovementControllerMissing={movement == null}.",
                this);
            return false;
        }

        if (!provider.TryReserve(this, interaction))
        {
            Debug.LogWarning(
                $"FlightSchoolStudentAgent {name}: Chair reservation failed. " +
                $"Provider={provider.name}, Interaction={interaction.InteractionId}, " +
                $"Enabled={provider.InteractionsEnabled}, CanOffer={provider.CanOffer(interaction)}.",
                provider);
            return false;
        }

        reservedSeat = provider;
        seatInteraction = interaction;
        walkingToSeat = true;
        walkingToAircraft = false;
        walkingToCar = false;
        walkingToNeedInteraction = false;
        performingNeedInteraction = false;
        atSeat = false;

        if (!movement.TryMoveTo(provider))
        {
            Debug.LogError(
                $"FlightSchoolStudentAgent {name}: Reserved chair {provider.name}, " +
                $"but no reachable approach cell was found. Preferred target={provider.InteractionPosition}.",
                this);
            HandlePathFailed();
            return false;
        }

        Debug.Log(
            $"FlightSchoolStudentAgent {name}: Reserved chair {provider.name}; " +
            $"walking to selected approach point at {movement.Destination}.",
            this);

        return true;
    }

    /// <summary>
    /// Walks from the reserved chair to the training aircraft.
    /// The chair reservation is retained so the student can return to it.
    /// </summary>
    public bool BeginFlightLesson(GameObject aircraft)
    {
        if (aircraft == null || reservedSeat == null || movement == null)
            return false;

        // Sitting places the student on the chair's animation anchor, which
        // may be inside a blocked building footprint. Restore the reserved
        // chair's reachable approach position before calculating a route.
        if (walkingToSeat == false && IsSeated)
            movement.AlignToInteractionPoint(reservedSeat);

        walkingToSeat = false;
        CancelNeedInteraction();
        atSeat = false;
        walkingToAircraft = true;
        walkingToCar = false;

        Vector3 aircraftApproach =
            aircraft.transform.position +
            aircraft.transform.right * 2f;

        PlacedBuilding aircraftBuilding =
            aircraft.GetComponentInParent<PlacedBuilding>();

        if (movement.TryMoveToNearby(
                aircraftApproach,
                aircraft.transform.position,
                aircraftBuilding))
        {
            Debug.Log(
                $"FlightSchoolStudentAgent {name}: Walking to aircraft {aircraft.name}. " +
                $"Start={transform.position}, preferred approach={aircraftApproach}, " +
                $"selected approach={movement.Destination}.",
                this);
            return true;
        }

        Debug.LogError(
            $"FlightSchoolStudentAgent {name}: No reachable aircraft approach found. " +
            $"Aircraft={aircraft.name}, Start={transform.position}, " +
            $"StartCell={GridCellForDiagnostics(transform.position)}, " +
            $"Preferred={aircraftApproach}, " +
            $"PreferredCell={GridCellForDiagnostics(aircraftApproach)}, " +
            $"MovementStatus={movement.MovementStatus}.",
            this);

        HandlePathFailed();
        return false;
    }

    /// <summary>
    /// Returns the student to their original reserved chair after landing.
    /// </summary>
    public bool ReturnToSeat()
    {
        if (reservedSeat == null || movement == null)
            return false;

        CancelNeedInteraction();
        atSeat = false;
        walkingToSeat = true;
        walkingToAircraft = false;
        walkingToCar = false;

        if (movement.TryMoveTo(reservedSeat))
            return true;

        HandlePathFailed();
        return false;
    }

    /// <summary>
    /// Temporarily leaves the reserved chair to join a standing student conversation.
    /// </summary>
    public bool PrepareForGroupConversation()
    {
        if (!IsSeated || reservedSeat == null || movement == null)
            return false;

        movement.AlignToInteractionPoint(reservedSeat);
        atSeat = false;
        groupConversationActive = true;
        return true;
    }

    /// <summary>
    /// Ends a group conversation and returns the student to their reserved chair.
    /// </summary>
    public void EndGroupConversationAndReturnToSeat()
    {
        if (!groupConversationActive)
            return;

        groupConversationActive = false;
        if (departureStarted || walkingToCar)
            return;

        ReturnToSeat();
    }

    private void InterruptGroupConversationForDeparture()
    {
        groupConversationActive = false;
        if (groupConversationController != null)
            groupConversationController.InterruptForDeparture();
    }

    /// <summary>
    /// Leaves the classroom seat and walks back to this student's own parked car.
    /// </summary>
    public bool BeginDeparture()
    {
        if (departureStarted)
            return walkingToCar;

        if (vehicle == null || movement == null)
            return false;

        InterruptGroupConversationForDeparture();
        instructorConversationActive = false;

        // A seated student is aligned to the chair animation point, which
        // can be on a non-walkable cell. Move back to the saved approach point
        // before releasing the reservation and walking to the car.
        if (IsSeated && reservedSeat != null)
            movement.AlignToInteractionPoint(reservedSeat);

        CancelNeedInteraction();
        atSeat = false;
        ReleaseSeat();

        walkingToSeat = false;
        walkingToAircraft = false;
        walkingToCar = true;

        Vector3 carSidePosition =
            vehicle.ParkingPosition +
            vehicle.transform.right * 1.5f;

        // The exact point beside a parked vehicle can be blocked by the
        // vehicle's footprint or nearby airport objects. Search the cells
        // immediately around the car and choose the nearest reachable one.
        if (!movement.TryMoveToNearby(
                carSidePosition,
                vehicle.ParkingPosition))
        {
            walkingToCar = false;
            Debug.LogWarning(
                $"FlightSchoolStudentAgent {name}: No reachable approach cell " +
                $"beside car {vehicle.name}. Preferred={carSidePosition}, " +
                $"car position={vehicle.ParkingPosition}.",
                this);
            return false;
        }

        departureStarted = true;
        return true;
    }

    private string GridCellForDiagnostics(Vector3 worldPosition)
    {
        GridManager grid = FindAnyObjectByType<GridManager>();
        return grid != null
            ? grid.WorldToGrid(worldPosition).ToString()
            : "unknown";
    }

    private void HandleMovementArrived()
    {
        if (logMovementArrivals)
        {
            Debug.Log(
                $"FlightSchoolStudentAgent {name}: Movement arrived. " +
                $"WalkingToSeat={walkingToSeat}, WalkingToAircraft={walkingToAircraft}, " +
                $"WalkingToCar={walkingToCar}.",
                this);
        }

        if (walkingToSeat)
        {
            walkingToSeat = false;

            if (reservedSeat != null)
                movement.AlignToAnimationPoint(reservedSeat);

            atSeat = true;
            SeatReached?.Invoke(this);
            return;
        }

        if (walkingToNeedInteraction)
        {
            walkingToNeedInteraction = false;

            if (queuedNeedProvider != null)
            {
                if (queuedNeedProvider.Queue == null ||
                    !queuedNeedProvider.Queue.IsActive(this))
                    return;

                needProvider = queuedNeedProvider;
                needInteraction = queuedNeedInteraction;
            }

            if (needProvider == null || needInteraction == null)
                return;

            StartNeedInteractionAtProvider();
            return;
        }

        if (walkingToAircraft)
        {
            walkingToAircraft = false;
            AircraftReached?.Invoke(this);
            return;
        }

        if (walkingToCar)
        {
            walkingToCar = false;
            CarReached?.Invoke(this);

            if (vehicle != null)
                vehicle.RequestDeparture();

            Destroy(gameObject);
        }
    }

    private void StartNeedInteractionAtProvider()
    {
        if (performingNeedInteraction || needInteractionRoutine != null ||
            needProvider == null || needInteraction == null ||
            !needProvider.isActiveAndEnabled)
            return;

        walkingToNeedInteraction = false;
        movement.AlignToAnimationPoint(needProvider);
        performingNeedInteraction = true;
        needInteractionRoutine = StartCoroutine(PerformNeedInteraction());
    }

    private void HandlePathFailed()
    {
        // The group coordinator handles conversation route failures and sends
        // every affected student back to their reserved classroom chair.
        if (groupConversationActive)
            return;

        Debug.LogError(
            $"FlightSchoolStudentAgent {name}: Pedestrian path failed. " +
            $"WalkingToSeat={walkingToSeat}, WalkingToAircraft={walkingToAircraft}, " +
            $"WalkingToCar={walkingToCar}, ReservedSeat={(reservedSeat != null ? reservedSeat.name : "none")}.",
            this);

        bool needRouteFailed =
            walkingToNeedInteraction || queuedNeedProvider != null ||
            (needProvider != null && !performingNeedInteraction);

        if (needRouteFailed)
        {
            CancelNeedInteraction();
            nextNeedDecisionTime = Time.time + needDecisionInterval;

            // A failed need destination should not send the student home.
            // Keep the classroom reservation and return to the chair instead.
            if (reservedSeat != null && movement != null)
            {
                atSeat = false;
                walkingToSeat = true;
                if (movement.TryMoveTo(reservedSeat))
                    return;

                walkingToSeat = false;
            }
        }
        else if (performingNeedInteraction)
        {
            CancelNeedInteraction();
            nextNeedDecisionTime = Time.time + needDecisionInterval;
        }

        if (walkingToSeat && !walkingToAircraft)
        {
            atSeat = false;
            ReleaseSeat();
        }

        walkingToSeat = false;
        walkingToAircraft = false;
        walkingToCar = false;
        atSeat = false;
        PathFailed?.Invoke(this);
    }

    public void ReleaseSeat()
    {
        if (reservedSeat != null)
            reservedSeat.ReleaseReservation(this, seatInteraction);

        reservedSeat = null;
        seatInteraction = null;
    }

    private void Update()
    {
        if (departureStarted || groupConversationActive || instructorConversationActive ||
            walkingToAircraft || walkingToCar || walkingToSeat || walkingToNeedInteraction ||
            performingNeedInteraction || movement == null || needs == null || movement.IsMoving)
            return;

        if (queuedNeedProvider != null)
        {
            UpdateNeedQueueState();
            return;
        }

        if (Time.time < nextNeedDecisionTime)
            return;

        nextNeedDecisionTime = Time.time + Mathf.Max(0.1f, needDecisionInterval);

        // While seated, look for a useful interaction. After each interaction,
        // continue with another need if one remains; otherwise return to the chair.
        TryStartNeedInteraction();
    }

    private bool TryStartNeedInteraction()
    {
        if (needs == null || movement == null || reservedSeat == null)
        {
            return false;
        }

        // The seated pose is aligned to the chair's AnimationPoint, which
        // can be inside its occupied grid cell. Path estimates must start from
        // the chair's saved walkable approach point, just like lesson/departure
        // routing does. Restore the seated pose if no interaction is selected.
        bool wasSeatedAtDecision = IsSeated;
        if (wasSeatedAtDecision)
            movement.AlignToInteractionPoint(reservedSeat);

        UtilityAISpatialRegistry<InteractionProvider>.GetNearby(
            transform.position, needInteractionSearchRadius, nearbyNeedProviders);

        needShortlist.Clear();

        for (int providerIndex = 0; providerIndex < nearbyNeedProviders.Count; providerIndex++)
        {
            InteractionProvider provider = nearbyNeedProviders[providerIndex];
            if (provider == null || provider == reservedSeat)
                continue;

            if (!provider.InteractionsEnabled)
            {
                continue;
            }

            if (roomPurposeController != null &&
                !roomPurposeController.IsPositionAccessibleToPedestrian(
                    provider.InteractionPosition, accessProfile))
            {
                continue;
            }

            float providerTravelTime = -1f;

            foreach (InteractionDefinition definition in provider.Interactions)
            {
                if (definition == null)
                    continue;

                float utility = CalculateNeedUtility(definition);
                if (utility <= 0f)
                    continue;

                if (!provider.CanOffer(definition))
                {
                    continue;
                }

                if (providerTravelTime < 0f)
                    providerTravelTime = movement.EstimateTravelTime(provider);

                if (float.IsInfinity(providerTravelTime) ||
                    float.IsNaN(providerTravelTime))
                {
                    continue;
                }

                float waitingTime = provider.Queue != null && provider.Queue.QueueEnabled
                    ? provider.Queue.GetEstimatedWaitTime()
                    : 0f;

                float totalTime = providerTravelTime + waitingTime +
                                  definition.DurationSeconds;
                float score = utility /
                    (1f + totalTime * Mathf.Max(0f, needInteractionTimeCost));

                if (score > 0f)
                {
                    needShortlist.Add(new NeedInteractionCandidate
                    {
                        provider = provider,
                        definition = definition,
                        score = score,
                        travelTime = providerTravelTime
                    });
                }
            }
        }

        if (needShortlist.Count == 0)
        {
            if (wasSeatedAtDecision && reservedSeat != null)
                movement.AlignToAnimationPoint(reservedSeat);
            else if (!atSeat && !walkingToSeat && !walkingToAircraft && !walkingToCar)
                ReturnToSeat();
            return false;
        }

        needShortlist.Sort((a, b) => b.score.CompareTo(a.score));
        int candidateCount = Mathf.Min(4, needShortlist.Count);
        float totalWeight = 0f;
        for (int i = 0; i < candidateCount; i++)
            totalWeight += needShortlist[i].score;

        float roll = UnityEngine.Random.value * totalWeight;
        NeedInteractionCandidate selected = needShortlist[0];
        for (int i = 0; i < candidateCount; i++)
        {
            roll -= needShortlist[i].score;
            if (roll <= 0f)
            {
                selected = needShortlist[i];
                break;
            }
        }

        if (atSeat && reservedSeat != null)
            movement.AlignToInteractionPoint(reservedSeat);

        atSeat = false;

        if (selected.provider.Queue != null &&
            selected.provider.Queue.QueueEnabled)
        {
            if (!selected.provider.Queue.TryJoin(
                    this, selected.definition, selected.travelTime))
            {
                ReturnToSeat();
                return false;
            }

            queuedNeedProvider = selected.provider;
            queuedNeedInteraction = selected.definition;
            UpdateNeedQueueState();
            return true;
        }

        if (!selected.provider.TryReserve(this, selected.definition))
        {
            ReturnToSeat();
            return false;
        }

        needProvider = selected.provider;
        needInteraction = selected.definition;
        walkingToNeedInteraction = true;

        if (!movement.MoveTo(selected.provider))
        {
            CancelNeedInteraction();
            ReturnToSeat();
            return false;
        }

        return true;
    }

    private float CalculateNeedUtility(InteractionDefinition definition)
    {
        float score = 0f;

        foreach (InteractionDefinition.NeedEffect effect in definition.NeedEffects)
        {
            if (effect == null || effect.satisfactionAmount <= 0f ||
                !needs.IsNeedConsidered(effect.need) ||
                needs.GetValue(effect.need) >= effect.maximumNeedValueToConsider)
                continue;

            float urgency = needs.GetUrgency(effect.need);
            float weight = needs.GetUtilityWeight(effect.need);
            score += urgency * weight *
                     Mathf.Min(100f, effect.satisfactionAmount) / 100f;
        }

        if (score <= 0f)
            return 0f;

        foreach (InteractionDefinition.NeedConflict conflict in definition.NeedConflicts)
        {
            if (conflict == null || conflict.penaltyAtMaximumUrgency <= 0f)
                continue;

            float urgency = needs.GetUrgency(conflict.need) / 100f;
            score *= 1f - Mathf.Clamp01(
                urgency * conflict.penaltyAtMaximumUrgency);
        }

        return score;
    }

    private void UpdateNeedQueueState()
    {
        if (queuedNeedProvider == null || queuedNeedInteraction == null ||
            !queuedNeedProvider.isActiveAndEnabled ||
            queuedNeedProvider.Queue == null ||
            !queuedNeedProvider.Queue.QueueEnabled ||
            !queuedNeedProvider.Queue.IsQueued(this))
        {
            CancelNeedInteraction();
            ReturnToSeat();
            return;
        }

        InteractionQueue queue = queuedNeedProvider.Queue;

        if (queue.IsActive(this))
        {
            needProvider = queuedNeedProvider;
            needInteraction = queuedNeedInteraction;

            if (movement.IsAtInteractionApproach(queuedNeedProvider))
            {
                // The queue may become active while the student is already
                // standing at the provider. In that case no Arrived event will
                // fire, so explicitly begin the interaction here.
                StartNeedInteractionAtProvider();
                return;
            }

            if (!movement.IsMoving &&
                Time.time >= nextQueuePositionRefresh)
            {
                nextQueuePositionRefresh = Time.time + queuePositionRefreshInterval;
                lastQueueDestination = queuedNeedProvider.InteractionPosition;
                walkingToNeedInteraction = true;

                if (!movement.MoveTo(queuedNeedProvider))
                {
                    walkingToNeedInteraction = false;
                    CancelNeedInteraction();
                    ReturnToSeat();
                }
            }

            return;
        }

        if (Time.time < nextQueuePositionRefresh)
            return;

        nextQueuePositionRefresh = Time.time + queuePositionRefreshInterval;
        Vector3 queuePosition = queue.GetQueuePosition(this);
        bool destinationChanged =
            Vector3.Distance(lastQueueDestination, queuePosition) > 0.15f;

        if (!movement.IsMoving && destinationChanged)
        {
            lastQueueDestination = queuePosition;
            movement.MoveTo(queuePosition);
        }
        else if (movement.IsMoving && destinationChanged &&
                 Vector3.Distance(movement.Destination, queuePosition) > 0.15f)
        {
            lastQueueDestination = queuePosition;
            movement.MoveTo(queuePosition);
        }
    }

    private System.Collections.IEnumerator PerformNeedInteraction()
    {
        PauseNeedDecayForInteraction();
        float duration = needInteraction.DurationSeconds;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (needProvider == null || !needProvider.isActiveAndEnabled ||
                needInteraction == null)
            {
                CancelNeedInteraction();
                yield break;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        InteractionProvider completedProvider = needProvider;
        InteractionDefinition completedInteraction = needInteraction;
        ResumeNeedDecayForInteraction();

        if (completedInteraction != null && needs != null)
        {
            foreach (InteractionDefinition.NeedEffect effect in completedInteraction.NeedEffects)
            {
                if (effect != null && effect.satisfactionAmount > 0f)
                    needs.SatisfyNeed(effect.need, effect.satisfactionAmount);
            }
        }

        if (completedProvider != null)
            movement.AlignToInteractionPoint(completedProvider);

        if (queuedNeedProvider != null && queuedNeedProvider.Queue != null)
            queuedNeedProvider.Queue.Complete(this);
        else if (completedProvider != null && completedInteraction != null)
            completedProvider.ReleaseReservation(this, completedInteraction);

        queuedNeedProvider = null;
        queuedNeedInteraction = null;
        needProvider = null;
        needInteraction = null;
        performingNeedInteraction = false;
        needInteractionRoutine = null;
        nextNeedDecisionTime = Time.time + needDecisionInterval;
    }

    private void PauseNeedDecayForInteraction()
    {
        ResumeNeedDecayForInteraction();

        if (needs == null || needInteraction == null)
            return;

        foreach (InteractionDefinition.NeedEffect effect in needInteraction.NeedEffects)
        {
            if (effect == null || effect.satisfactionAmount <= 0f ||
                replenishingNeeds.Contains(effect.need))
                continue;

            needs.PauseNeedDecay(effect.need);
            replenishingNeeds.Add(effect.need);
        }
    }

    private void ResumeNeedDecayForInteraction()
    {
        if (needs != null)
        {
            foreach (UtilityNeeds.NeedType need in replenishingNeeds)
                needs.ResumeNeedDecay(need);
        }

        replenishingNeeds.Clear();
    }

    private void CancelNeedInteraction()
    {
        ResumeNeedDecayForInteraction();

        if (needInteractionRoutine != null)
        {
            StopCoroutine(needInteractionRoutine);
            needInteractionRoutine = null;
        }

        if (queuedNeedProvider != null && queuedNeedProvider.Queue != null)
            queuedNeedProvider.Queue.Leave(this);
        else if (needProvider != null && needInteraction != null)
            needProvider.ReleaseReservation(this, needInteraction);

        queuedNeedProvider = null;
        queuedNeedInteraction = null;
        needProvider = null;
        needInteraction = null;
        walkingToNeedInteraction = false;
        performingNeedInteraction = false;
    }

    private void OnDestroy()
    {
        CancelNeedInteraction();

        if (movement != null)
        {
            movement.Arrived -= HandleMovementArrived;
            movement.PathFailed -= HandlePathFailed;
        }

        ReleaseSeat();
    }
}
