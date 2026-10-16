
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class AirportBaggageDispatcher : MonoBehaviour
{
    public static AirportBaggageDispatcher Instance { get; private set; }

    public enum RequestStatus
    {
        Queued,
        Assigned,
        AtPickup,
        Completed,
        Cancelled
    }

    private enum RequestType
    {
        Incoming,
        Outgoing
    }

    private sealed class BaggageRequest
    {
        public string RequestKey;
        public string OperationId;
        public RequestType Type;
        public AircraftTurnaroundController Aircraft;
        public AircraftFlightIdentity FlightIdentity;
        public AircraftGate ActualGate;
        public int RequestedPackageCount;
        public RequestStatus Status;
        public AirportCargoVehicleController AssignedVehicle;
    }

    [Header("Dispatcher Settings")]
    [Min(1)]
    [SerializeField] private int defaultVehicleCapacity = 10;

    [Header("Runtime Status")]
    [SerializeField] private int queuedRequestCount;
    [SerializeField] private int activeRequestCount;
    [SerializeField] private string dispatcherStatus = "Initializing";

    private readonly Queue<BaggageRequest> requestQueue =
        new Queue<BaggageRequest>();

    private readonly Dictionary<string, BaggageRequest> requestsByOperationId =
        new Dictionary<string, BaggageRequest>();

    private readonly HashSet<AirportCargoVehicleController> availableVehicles =
        new HashSet<AirportCargoVehicleController>();

    public int QueuedRequestCount => queuedRequestCount;
    public int ActiveRequestCount => activeRequestCount;
    public string DispatcherStatus => dispatcherStatus;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError(
                $"{name}: Another AirportBaggageDispatcher already exists. " +
                "Only one dispatcher is allowed.",
                this
            );

            enabled = false;
            return;
        }

        Instance = this;
        dispatcherStatus = "Ready";

        Debug.Log($"{name}: Baggage dispatcher initialized.", this);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void RegisterVehicle(AirportCargoVehicleController vehicle)
    {
        if (vehicle == null)
            return;

        availableVehicles.Add(vehicle);

        Debug.Log(
            $"{name}: Registered cargo vehicle '{vehicle.name}'.",
            this
        );

        ProcessQueue();
    }

    public void UnregisterVehicle(AirportCargoVehicleController vehicle)
    {
        if (vehicle == null)
            return;

        availableVehicles.Remove(vehicle);
    }

    public bool RequestBaggageService(
        AircraftTurnaroundController aircraft,
        AircraftGate actualGate)
    {
        if (aircraft == null || actualGate == null)
            return false;

        AircraftFlightIdentity identity =
            aircraft.GetComponent<AircraftFlightIdentity>();

        if (identity == null ||
            !identity.IsInitialized ||
            !identity.HasActualGate ||
            identity.ActualGateStableId != actualGate.StableId)
        {
            Debug.LogWarning(
                $"{name}: The aircraft's actual gate assignment is invalid.",
                aircraft
            );

            return false;
        }

        if (!aircraft.TurnaroundStarted)
            return false;

        AircraftLoadingManifest manifest = aircraft.LoadingManifest;

        if (manifest == null || !manifest.IsInitialized)
            return false;

        string operationId = identity.OperationId;
        int packageCount = manifest.RemainingIncomingBaggage;

        if (packageCount <= 0)
            return false;

        return QueueRequest(
            aircraft,
            identity,
            actualGate,
            operationId,
            RequestType.Incoming,
            packageCount
        );
    }

    private bool QueueRequest(
        AircraftTurnaroundController aircraft,
        AircraftFlightIdentity identity,
        AircraftGate actualGate,
        string operationId,
        RequestType type,
        int packageCount)
    {
        string requestKey = GetRequestKey(operationId, type);

        if (requestsByOperationId.TryGetValue(
                requestKey,
                out BaggageRequest existingRequest))
        {
            if (existingRequest.Status != RequestStatus.Cancelled)
            {
                Debug.Log(
                    $"{name}: Duplicate {type} baggage request ignored " +
                    $"for {operationId}. Status: {existingRequest.Status}.",
                    aircraft
                );

                return true;
            }

            requestsByOperationId.Remove(requestKey);
        }

        BaggageRequest request = new BaggageRequest
        {
            RequestKey = requestKey,
            OperationId = operationId,
            Type = type,
            Aircraft = aircraft,
            FlightIdentity = identity,
            ActualGate = actualGate,
            RequestedPackageCount = Mathf.Min(
                packageCount,
                Mathf.Max(1, defaultVehicleCapacity)
            ),
            Status = RequestStatus.Queued
        };

        requestsByOperationId.Add(requestKey, request);
        requestQueue.Enqueue(request);

        Debug.Log(
            $"{name}: {type} baggage request queued for " +
            $"{identity.FlightNumber} ({operationId}). " +
            $"Actual gate: {actualGate.StableId}. " +
            $"Packages requested: {request.RequestedPackageCount}.",
            aircraft
        );

        ProcessQueue();
        RefreshRuntimeStatus();

        return true;
    }

    private string GetRequestKey(string operationId, RequestType type)
    {
        return type == RequestType.Incoming
            ? operationId
            : operationId + ":OUTGOING";
    }

    public void NotifyVehicleArrivedAtPickup(
        AirportCargoVehicleController vehicle,
        string requestKey)
    {
        if (vehicle == null || string.IsNullOrWhiteSpace(requestKey))
            return;

        if (!requestsByOperationId.TryGetValue(
                requestKey,
                out BaggageRequest request))
        {
            Debug.LogWarning(
                $"{name}: Arrival received for unknown request " +
                $"'{requestKey}'.",
                vehicle
            );

            return;
        }

        if (request.Status != RequestStatus.Assigned ||
            request.AssignedVehicle != vehicle)
        {
            Debug.LogWarning(
                $"{name}: Vehicle '{vehicle.name}' reported an arrival " +
                $"for an assignment it does not own: {requestKey}.",
                vehicle
            );

            return;
        }

        request.Status = RequestStatus.AtPickup;

        Debug.Log(
            $"{name}: Vehicle '{vehicle.name}' arrived at gate " +
            $"{request.ActualGate.StableId} for {request.Type} task " +
            $"{request.OperationId}.",
            vehicle
        );

        bool jobStarted;

        if (request.Type == RequestType.Incoming)
        {
            AirportCargoIncomingBaggageJob baggageJob =
                vehicle.GetComponent<AirportCargoIncomingBaggageJob>();

            if (baggageJob == null)
            {
                NotifyAssignmentFailed(
                    vehicle,
                    requestKey,
                    "AirportCargoIncomingBaggageJob is missing from the vehicle."
                );

                return;
            }

            jobStarted = baggageJob.BeginIncomingBaggageJob(
                request.Aircraft,
                request.OperationId,
                Mathf.Min(
                    request.RequestedPackageCount,
                    vehicle.AvailableCargoCapacity
                )
            );
        }
        else
        {
            AirportCargoOutgoingBaggageJob outgoingJob =
                vehicle.GetComponent<AirportCargoOutgoingBaggageJob>();

            if (outgoingJob == null)
            {
                NotifyAssignmentFailed(
                    vehicle,
                    requestKey,
                    "AirportCargoOutgoingBaggageJob is missing from the vehicle."
                );

                return;
            }

            jobStarted = outgoingJob.BeginOutgoingBaggageJob(
                request.Aircraft,
                request.ActualGate,
                request.OperationId
            );
        }

        if (!jobStarted)
        {
            NotifyAssignmentFailed(
                vehicle,
                requestKey,
                $"The {request.Type.ToString().ToLowerInvariant()} " +
                "baggage job could not be started."
            );
        }

        RefreshRuntimeStatus();
    }

    public void NotifyAssignmentFailed(
        AirportCargoVehicleController vehicle,
        string requestKey,
        string reason)
    {
        if (vehicle == null || string.IsNullOrWhiteSpace(requestKey))
            return;

        if (!requestsByOperationId.TryGetValue(
                requestKey,
                out BaggageRequest request))
        {
            return;
        }

        if ((request.Status != RequestStatus.Assigned &&
             request.Status != RequestStatus.AtPickup) ||
            request.AssignedVehicle != vehicle)
        {
            return;
        }

        request.Status = RequestStatus.Cancelled;
        request.AssignedVehicle = null;

        availableVehicles.Add(vehicle);

        Debug.LogWarning(
            $"{name}: {request.Type} assignment failed for " +
            $"{request.OperationId}. Reason: {reason}",
            vehicle
        );

        ProcessQueue();
        RefreshRuntimeStatus();
    }

    public void NotifyRequestCompleted(
        AirportCargoVehicleController vehicle,
        string operationId)
    {
        CompleteRequest(
            vehicle,
            GetRequestKey(operationId, RequestType.Incoming),
            RequestType.Incoming
        );
    }

    // Called when incoming baggage has been delivered to a known cargo zone.
    // If outgoing baggage remains, the same vehicle immediately starts that
    // job at the supplied zone without becoming available to other requests.
    public void NotifyIncomingRequestCompletedAtCargoZone(
        AirportCargoVehicleController vehicle,
        string operationId,
        CargoZone completedCargoZone)
    {
        if (vehicle == null ||
            string.IsNullOrWhiteSpace(operationId) ||
            completedCargoZone == null)
        {
            Debug.LogWarning(
                $"{name}: Incoming completion could not be processed because " +
                "the vehicle, operation ID, or cargo zone is missing.",
                this
            );

            return;
        }

        string incomingKey =
            GetRequestKey(operationId, RequestType.Incoming);

        if (!requestsByOperationId.TryGetValue(
                incomingKey,
                out BaggageRequest incomingRequest))
        {
            Debug.LogWarning(
                $"{name}: Completion received for unknown incoming request " +
                $"'{incomingKey}'.",
                vehicle
            );

            return;
        }

        if (incomingRequest.Status != RequestStatus.AtPickup ||
            incomingRequest.AssignedVehicle != vehicle ||
            incomingRequest.Type != RequestType.Incoming)
        {
            Debug.LogWarning(
                $"{name}: Vehicle '{vehicle.name}' attempted to complete " +
                $"an incoming assignment it does not own: {incomingKey}.",
                vehicle
            );

            return;
        }

        incomingRequest.Status = RequestStatus.Completed;
        incomingRequest.AssignedVehicle = null;

        AircraftLoadingManifest manifest =
            incomingRequest.Aircraft != null
                ? incomingRequest.Aircraft.LoadingManifest
                : null;

        Debug.Log(
            $"{name}: Incoming baggage request completed for " +
            $"{operationId} at cargo zone '{completedCargoZone.name}'.",
            vehicle
        );

        if (manifest == null ||
            !manifest.IsInitialized ||
            !manifest.HasOutgoingBaggageRemaining)
        {
            availableVehicles.Add(vehicle);
            Debug.Log(
                $"{name}: Vehicle '{vehicle.name}' is available.",
                vehicle
            );

            ProcessQueue();
            RefreshRuntimeStatus();
            return;
        }

        string outgoingKey =
            GetRequestKey(operationId, RequestType.Outgoing);

        if (requestsByOperationId.TryGetValue(
                outgoingKey,
                out BaggageRequest existingOutgoing))
        {
            if (existingOutgoing.Status != RequestStatus.Cancelled)
            {
                Debug.LogWarning(
                    $"{name}: An outgoing request already exists for " +
                    $"{operationId}. The vehicle will be released rather " +
                    "than creating a duplicate task.",
                    vehicle
                );

                availableVehicles.Add(vehicle);
                ProcessQueue();
                RefreshRuntimeStatus();
                return;
            }

            requestsByOperationId.Remove(outgoingKey);
        }

        BaggageRequest outgoingRequest = new BaggageRequest
        {
            RequestKey = outgoingKey,
            OperationId = operationId,
            Type = RequestType.Outgoing,
            Aircraft = incomingRequest.Aircraft,
            FlightIdentity = incomingRequest.FlightIdentity,
            ActualGate = incomingRequest.ActualGate,
            RequestedPackageCount = Mathf.Min(
                manifest.RemainingOutgoingBaggage,
                Mathf.Max(1, defaultVehicleCapacity)
            ),
            Status = RequestStatus.AtPickup,
            AssignedVehicle = vehicle
        };

        requestsByOperationId.Add(outgoingKey, outgoingRequest);

        // Deliberately do not add the vehicle to availableVehicles here.
        // It remains assigned while the outgoing job starts at this zone.
        AirportCargoOutgoingBaggageJob outgoingJob =
            vehicle.GetComponent<AirportCargoOutgoingBaggageJob>();

        if (outgoingJob == null)
        {
            NotifyAssignmentFailed(
                vehicle,
                outgoingKey,
                "AirportCargoOutgoingBaggageJob is missing from the vehicle."
            );

            return;
        }

        Debug.Log(
            $"{name}: Starting outgoing baggage for {operationId} directly " +
            $"from cargo zone '{completedCargoZone.name}'. " +
            $"The vehicle will drive straight to gate " +
            $"'{outgoingRequest.ActualGate.StableId}' after collection.",
            vehicle
        );

        bool jobStarted = outgoingJob.BeginOutgoingBaggageJob(
            outgoingRequest.Aircraft,
            outgoingRequest.ActualGate,
            outgoingRequest.OperationId,
            completedCargoZone
        );

        if (!jobStarted)
        {
            NotifyAssignmentFailed(
                vehicle,
                outgoingKey,
                "The outgoing baggage job could not be started at the " +
                "current cargo zone."
            );
        }

        RefreshRuntimeStatus();
    }

    public void NotifyOutgoingRequestCompleted(
        AirportCargoVehicleController vehicle,
        string operationId)
    {
        CompleteRequest(
            vehicle,
            GetRequestKey(operationId, RequestType.Outgoing),
            RequestType.Outgoing
        );
    }

    public void NotifyOutgoingAssignmentFailed(
        AirportCargoVehicleController vehicle,
        string operationId,
        string reason)
    {
        NotifyAssignmentFailed(
            vehicle,
            GetRequestKey(operationId, RequestType.Outgoing),
            reason
        );
    }

    private void CompleteRequest(
        AirportCargoVehicleController vehicle,
        string requestKey,
        RequestType expectedType)
    {
        if (vehicle == null || string.IsNullOrWhiteSpace(requestKey))
            return;

        if (!requestsByOperationId.TryGetValue(
                requestKey,
                out BaggageRequest request))
        {
            Debug.LogWarning(
                $"{name}: Completion received for unknown request " +
                $"'{requestKey}'.",
                vehicle
            );

            return;
        }

        if (request.Status != RequestStatus.AtPickup ||
            request.AssignedVehicle != vehicle ||
            request.Type != expectedType)
        {
            Debug.LogWarning(
                $"{name}: Vehicle '{vehicle.name}' attempted to complete " +
                $"an assignment it does not own: {requestKey}.",
                vehicle
            );

            return;
        }

        request.Status = RequestStatus.Completed;
        request.AssignedVehicle = null;

        availableVehicles.Add(vehicle);

        Debug.Log(
            $"{name}: {request.Type} baggage request completed for " +
            $"{request.OperationId}. Vehicle '{vehicle.name}' is available.",
            vehicle
        );

        // Retained as a fallback for any existing caller that still uses
        // NotifyRequestCompleted instead of the explicit zone handoff.
        if (request.Type == RequestType.Incoming &&
            request.Aircraft != null &&
            request.FlightIdentity != null &&
            request.ActualGate != null)
        {
            AircraftLoadingManifest manifest =
                request.Aircraft.LoadingManifest;

            if (manifest != null &&
                manifest.IsInitialized &&
                manifest.HasOutgoingBaggageRemaining)
            {
                QueueRequest(
                    request.Aircraft,
                    request.FlightIdentity,
                    request.ActualGate,
                    request.OperationId,
                    RequestType.Outgoing,
                    manifest.RemainingOutgoingBaggage
                );
            }
        }

        ProcessQueue();
        RefreshRuntimeStatus();
    }

    private void ProcessQueue()
    {
        availableVehicles.RemoveWhere(vehicle => vehicle == null);

        while (requestQueue.Count > 0)
        {
            AirportCargoVehicleController vehicle =
                FindAvailableVehicle();

            if (vehicle == null)
                break;

            BaggageRequest request = requestQueue.Dequeue();

            if (request == null ||
                request.Status != RequestStatus.Queued)
            {
                continue;
            }

            if (request.Aircraft == null ||
                request.FlightIdentity == null ||
                request.ActualGate == null)
            {
                request.Status = RequestStatus.Cancelled;
                continue;
            }

            AirportCargoVehicleDispatcherRegistration registration =
                vehicle.GetComponent<
                    AirportCargoVehicleDispatcherRegistration>();

            if (registration == null)
            {
                request.Status = RequestStatus.Cancelled;

                Debug.LogError(
                    $"{name}: Vehicle '{vehicle.name}' has no " +
                    "AirportCargoVehicleDispatcherRegistration component.",
                    vehicle
                );

                continue;
            }

            request.Status = RequestStatus.Assigned;
            request.AssignedVehicle = vehicle;
            availableVehicles.Remove(vehicle);

            Debug.Log(
                $"{name}: Assigned vehicle '{vehicle.name}' to " +
                $"{request.Type} task for flight operation " +
                $"{request.OperationId}. Gate: " +
                $"{request.ActualGate.StableId}. Packages requested: " +
                $"{request.RequestedPackageCount}.",
                vehicle
            );

            bool movementStarted = registration.MoveToGate(
                request.ActualGate,
                request.RequestKey
            );

            if (!movementStarted)
            {
                request.Status = RequestStatus.Cancelled;
                request.AssignedVehicle = null;
                availableVehicles.Add(vehicle);

                Debug.LogWarning(
                    $"{name}: Could not start vehicle movement for " +
                    $"{request.Type} task {request.OperationId}.",
                    vehicle
                );
            }
        }

        RefreshRuntimeStatus();
    }


    private AirportCargoVehicleController FindAvailableVehicle()
    {
        foreach (AirportCargoVehicleController vehicle in availableVehicles)
        {
            if (vehicle == null)
                continue;

            if (!vehicle.IsMoving)
                return vehicle;

            if (vehicle.IsReturningHome)
                return vehicle;
        }

        return null;
    }


    private void RefreshRuntimeStatus()
    {
        queuedRequestCount = 0;
        activeRequestCount = 0;

        foreach (BaggageRequest request in requestsByOperationId.Values)
        {
            if (request == null)
                continue;

            if (request.Status == RequestStatus.Queued)
                queuedRequestCount++;
            else if (request.Status == RequestStatus.Assigned ||
                     request.Status == RequestStatus.AtPickup)
                activeRequestCount++;
        }

        if (activeRequestCount > 0)
            dispatcherStatus = "Assignments active";
        else if (queuedRequestCount > 0)
            dispatcherStatus = "Requests waiting for a vehicle";
        else
            dispatcherStatus = "Ready";
    }
}
