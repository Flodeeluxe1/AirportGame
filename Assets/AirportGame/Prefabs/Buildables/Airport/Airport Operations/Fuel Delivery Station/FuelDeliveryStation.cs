using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Queue and stop-point controller for one physical Fuel Delivery Station.
/// </summary>
[DisallowMultipleComponent]
public class FuelDeliveryStation : MonoBehaviour
{
    [Header("Truck Stop")]
    [SerializeField] private Transform fuelTruckStopPoint;

    [Header("Road Approach")]
    [Min(0.1f)]
    [SerializeField] private float maximumRoadApproachDistance = 8f;

    private readonly List<FuelTruckController> queuedTrucks =
        new List<FuelTruckController>();

    private FuelTruckController occupyingTruck;
    private FuelConnector fuelConnector;
    private PipelineNetworkManager pipelineManager;
    private PipelineNetworkManager.PipelineNetwork cachedFuelNetwork;
    private int cachedNetworkTopologyVersion = -1;

    public FuelConnector Connector
    {
        get
        {
            if (fuelConnector == null)
                fuelConnector = GetComponentInChildren<FuelConnector>(true);
            return fuelConnector;
        }
    }

    public PipelineNetworkManager.PipelineNetwork FuelNetwork
    {
        get
        {
            if (pipelineManager == null)
                pipelineManager = FindAnyObjectByType<PipelineNetworkManager>();

            if (pipelineManager == null || Connector == null)
                return null;

            if (cachedNetworkTopologyVersion != pipelineManager.TopologyVersion)
            {
                cachedFuelNetwork = pipelineManager.GetNetworkFor(Connector);
                cachedNetworkTopologyVersion = pipelineManager.TopologyVersion;
            }

            return cachedFuelNetwork;
        }
    }

    public Transform FuelTruckStopPoint
    {
        get
        {
            if (fuelTruckStopPoint == null)
            {
                Transform[] children = GetComponentsInChildren<Transform>(true);
                foreach (Transform child in children)
                {
                    if (child != transform && child.name == "FuelTruckStopPoint")
                    {
                        fuelTruckStopPoint = child;
                        break;
                    }
                }
            }

            return fuelTruckStopPoint;
        }
    }

    public float MaximumRoadApproachDistance =>
        Mathf.Max(0.1f, maximumRoadApproachDistance);

    public bool IsOccupied => occupyingTruck != null;

    public int QueuedVehicleCount
    {
        get
        {
            return queuedTrucks.Count;
        }
    }

    public void RefreshQueueState()
    {
        RemoveInvalidQueuedTrucks();
    }

    public void RegisterTruck(FuelTruckController truck)
    {
        if (truck == null)
            return;

        RemoveInvalidQueuedTrucks();
        if (queuedTrucks.Contains(truck) || occupyingTruck == truck)
            return;

        queuedTrucks.Add(truck);
    }

    public bool TryOccupyStop(FuelTruckController truck)
    {
        if (truck == null)
            return false;

        RemoveInvalidQueuedTrucks();

        if (occupyingTruck == truck)
            return true;

        if (occupyingTruck != null || queuedTrucks.Count == 0 ||
            queuedTrucks[0] != truck)
            return false;

        queuedTrucks.RemoveAt(0);
        occupyingTruck = truck;
        return true;
    }

    private void RemoveInvalidQueuedTrucks()
    {
        for (int i = queuedTrucks.Count - 1; i >= 0; i--)
        {
            if (queuedTrucks[i] == null)
                queuedTrucks.RemoveAt(i);
        }
    }

    public void ReleaseTruck(FuelTruckController truck)
    {
        if (truck == null)
            return;

        queuedTrucks.Remove(truck);

        if (occupyingTruck == truck)
            occupyingTruck = null;
    }
}
