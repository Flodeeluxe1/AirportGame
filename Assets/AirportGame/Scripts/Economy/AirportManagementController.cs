using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Central authority for airport operating permissions.
/// A valid assigned CEO Office allows new aircraft arrivals.
/// A valid assigned CFO Office unlocks custom airport fee management.
/// Existing aircraft operations are deliberately not interrupted.
/// </summary>
[DisallowMultipleComponent]
public class AirportManagementController : MonoBehaviour
{
    public static AirportManagementController Instance { get; private set; }

    [Header("References")]
    [SerializeField] private RoomPurposeController roomPurposeController;
    [SerializeField] private RoomDetectionController roomDetectionController;

    public event Action StateChanged;

    [Header("Current Office Status")]
    [SerializeField] private bool hasValidCEOOffice;
    [SerializeField] private bool hasValidCFOOffice;

    private Coroutine pendingRefresh;

    public bool IsAirportOperational => hasValidCEOOffice;
    public bool CanAdjustFees => hasValidCFOOffice;
    public bool HasValidCEOOffice => hasValidCEOOffice;
    public bool HasValidCFOOffice => hasValidCFOOffice;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        SubscribeToChanges();
        QueueStatusRefresh();
    }

    private void Start()
    {
        RefreshOfficeStatus();
    }

    private void OnDisable()
    {
        UnsubscribeFromChanges();

        if (pendingRefresh != null)
        {
            StopCoroutine(pendingRefresh);
            pendingRefresh = null;
        }

        if (Instance == this)
            Instance = null;
    }

    private void ResolveReferences()
    {
        if (roomPurposeController == null)
        {
            roomPurposeController = GetComponent<RoomPurposeController>();

            if (roomPurposeController == null)
                roomPurposeController =
                    FindFirstObjectByType<RoomPurposeController>();
        }

        if (roomDetectionController == null)
            roomDetectionController =
                FindFirstObjectByType<RoomDetectionController>();
    }

    private void SubscribeToChanges()
    {
        if (roomPurposeController != null)
            roomPurposeController.AssignmentsChanged += QueueStatusRefresh;

        if (roomDetectionController != null)
            roomDetectionController.RoomsChanged += QueueStatusRefresh;

        PlacedBuilding.BuildingChanged += HandleBuildingChanged;
    }

    private void UnsubscribeFromChanges()
    {
        if (roomPurposeController != null)
            roomPurposeController.AssignmentsChanged -= QueueStatusRefresh;

        if (roomDetectionController != null)
            roomDetectionController.RoomsChanged -= QueueStatusRefresh;

        PlacedBuilding.BuildingChanged -= HandleBuildingChanged;
    }

    private void HandleBuildingChanged(PlacedBuilding building)
    {
        // Office furniture placement and demolition both raise this event.
        QueueStatusRefresh();
    }

    private void QueueStatusRefresh()
    {
        if (!isActiveAndEnabled || pendingRefresh != null)
            return;

        pendingRefresh = StartCoroutine(RefreshAfterChanges());
    }

    private IEnumerator RefreshAfterChanges()
    {
        // Wait until placement, demolition, or room restoration has finished.
        yield return null;

        pendingRefresh = null;
        RefreshOfficeStatus();
    }

    [ContextMenu("Refresh Office Status")]
    public void RefreshOfficeStatus()
    {
        ResolveReferences();

        bool newCEOStatus =
            roomPurposeController != null &&
            roomPurposeController.HasValidAssignedOffice(RoomPurpose.CEOOffice);

        bool newCFOStatus =
            roomPurposeController != null &&
            roomPurposeController.HasValidAssignedOffice(RoomPurpose.CFOOffice);

        if (newCEOStatus == hasValidCEOOffice &&
            newCFOStatus == hasValidCFOOffice)
        {
            return;
        }

        bool previousCEOStatus = hasValidCEOOffice;
        bool previousCFOStatus = hasValidCFOOffice;

        hasValidCEOOffice = newCEOStatus;
        hasValidCFOOffice = newCFOStatus;

        if (previousCEOStatus != hasValidCEOOffice)
        {
            Debug.Log(
                hasValidCEOOffice
                    ? "AirportManagementController: Valid CEO Office found. New aircraft arrivals are enabled."
                    : "AirportManagementController: Valid CEO Office missing. New aircraft arrivals are disabled.",
                this);
        }

        if (previousCFOStatus != hasValidCFOOffice)
        {
            Debug.Log(
                hasValidCFOOffice
                    ? "AirportManagementController: Valid CFO Office found. Custom airport fees are available."
                    : "AirportManagementController: Valid CFO Office missing. Default airport fees must be used.",
                this);
        }

        StateChanged?.Invoke();
    }
}
