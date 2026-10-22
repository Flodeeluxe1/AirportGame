
using System.Collections.Generic;
using UnityEngine;

public class ParkingSpace : MonoBehaviour
{
    [Header("Debug")]
    [Tooltip("Live status of the parking space and its waiting queue.")]
    [TextArea(3, 10)]
    public string debugQueueStatus = "Not checked";

    public bool IsReserved
    {
        get
        {
            CleanWaitingVehicles();
            return waitingVehicles.Count > 0;
        }
    }

    public bool IsOccupied
    {
        get
        {
            return parkedVehicle != null;
        }
    }

    public GameObject ReservedBy
    {
        get
        {
            CleanWaitingVehicles();

            if (waitingVehicles.Count == 0)
                return null;

            return waitingVehicles[0];
        }
    }

    public GameObject ParkedVehicle
    {
        get
        {
            return parkedVehicle;
        }
    }

    private GameObject parkedVehicle;

    private readonly List<GameObject> waitingVehicles =
        new List<GameObject>();

    private float occupiedUntil;

    private void Update()
    {
        // Remove destroyed vehicles from the waiting queue.
        CleanWaitingVehicles();

        // Keep the Inspector debug text current.
        RefreshDebugQueueStatus("Monitoring parking space.");
    }

    public bool Reserve(GameObject vehicle)
    {
        CleanWaitingVehicles();

        if (vehicle == null)
        {
            Debug.LogWarning(
                "ParkingSpace: Cannot reserve with a null vehicle.",
                this);

            RefreshDebugQueueStatus(
                "RESERVATION DENIED: Vehicle is null.");

            return false;
        }

        if (parkedVehicle == vehicle)
        {
            RefreshDebugQueueStatus(
                $"ALREADY PARKED: {vehicle.name}.");

            return true;
        }

        if (waitingVehicles.Contains(vehicle))
        {
            RefreshDebugQueueStatus(
                $"ALREADY QUEUED: {vehicle.name}.");

            return true;
        }

        waitingVehicles.Add(vehicle);

        RefreshDebugQueueStatus(
            $"RESERVED: {vehicle.name} joined the queue.");

        Debug.Log(
            $"PARKING RESERVED | Space: {name} | " +
            $"Vehicle: {vehicle.name} | " +
            $"Queue Position: {waitingVehicles.Count}",
            this);

        return true;
    }

    public bool CanVehicleEnter(GameObject vehicle)
    {
        CleanWaitingVehicles();

        if (vehicle == null)
        {
            RefreshDebugQueueStatus(
                "ENTRY DENIED: Vehicle is null.");

            return false;
        }

        if (parkedVehicle != null)
        {
            RefreshDebugQueueStatus(
                $"ENTRY DENIED: Occupied by {parkedVehicle.name}.");

            return false;
        }

        if (waitingVehicles.Count == 0)
        {
            RefreshDebugQueueStatus(
                $"ENTRY DENIED: {vehicle.name} is not in the queue.");

            return false;
        }

        if (waitingVehicles[0] != vehicle)
        {
            RefreshDebugQueueStatus(
                $"WAITING: {vehicle.name} is not first in queue. " +
                $"Queue leader: {waitingVehicles[0].name}.");

            return false;
        }

        RefreshDebugQueueStatus(
            $"ENTRY GRANTED: {vehicle.name} is first in queue.");

        return true;
    }

    public bool ParkVehicle(
        GameObject vehicle,
        float parkingDuration)
    {
        CleanWaitingVehicles();

        if (!CanVehicleEnter(vehicle))
        {
            Debug.LogWarning(
                $"PARKING FAILED | Space: {name} | " +
                $"Vehicle: {(vehicle != null ? vehicle.name : "null")} | " +
                "Vehicle is not allowed to enter.",
                this);

            return false;
        }

        waitingVehicles.Remove(vehicle);

        parkedVehicle = vehicle;

        occupiedUntil =
            Time.time +
            Mathf.Max(0f, parkingDuration);

        RefreshDebugQueueStatus(
            $"PARKED: {vehicle.name}.");

        Debug.Log(
            $"PARKING OCCUPIED | Space: {name} | " +
            $"Vehicle: {vehicle.name} | " +
            $"Duration: {parkingDuration:F1}s",
            this);

        return true;
    }

    public void ReleaseVehicle(GameObject vehicle)
    {
        if (vehicle == null)
            return;

        if (parkedVehicle != vehicle)
            return;

        parkedVehicle = null;
        occupiedUntil = 0f;

        RefreshDebugQueueStatus(
            $"PARKING RELEASED by {vehicle.name}.");

        Debug.Log(
            $"PARKING RELEASED | Space: {name} | " +
            $"Vehicle: {vehicle.name}",
            this);
    }

    public void CancelReservation(GameObject vehicle)
    {
        CleanWaitingVehicles();

        if (vehicle == null)
            return;

        bool wasReserved =
            waitingVehicles.Remove(vehicle);

        if (wasReserved)
        {
            RefreshDebugQueueStatus(
                $"RESERVATION CANCELLED for {vehicle.name}.");

            Debug.Log(
                $"PARKING RESERVATION CANCELLED | " +
                $"Space: {name} | Vehicle: {vehicle.name}",
                this);
        }
    }

    public float GetEstimatedAvailableTime(
        float parkingDuration)
    {
        CleanWaitingVehicles();

        float availableTime = Time.time;

        if (parkedVehicle != null)
        {
            availableTime = Mathf.Max(
                availableTime,
                occupiedUntil);
        }

        availableTime +=
            waitingVehicles.Count *
            Mathf.Max(0f, parkingDuration);

        return availableTime;
    }

    public int GetQueuePosition(GameObject vehicle)
    {
        CleanWaitingVehicles();

        if (vehicle == null)
            return -1;

        int index = waitingVehicles.IndexOf(vehicle);

        if (index < 0)
            return -1;

        return index + 1;
    }

    private void CleanWaitingVehicles()
    {
        int previousCount = waitingVehicles.Count;

        waitingVehicles.RemoveAll(
            vehicle => vehicle == null);

        if (waitingVehicles.Count != previousCount)
        {
            RefreshDebugQueueStatus(
                "CLEANUP: Removed destroyed vehicles from queue.");

            Debug.LogWarning(
                $"PARKING QUEUE CLEANUP | Space: {name} | " +
                $"Removed {previousCount - waitingVehicles.Count} " +
                "destroyed vehicle(s).",
                this);
        }

        // Unity's destroyed-object null check also detects
        // destroyed parked vehicles.
        if (parkedVehicle == null)
        {
            parkedVehicle = null;
        }
    }

    private void RefreshDebugQueueStatus(string reason)
    {
        string queue = "";

        for (int i = 0; i < waitingVehicles.Count; i++)
        {
            GameObject vehicle = waitingVehicles[i];

            if (vehicle == null)
                continue;

            TrafficTestVehicle traffic =
                vehicle.GetComponent<TrafficTestVehicle>();

            string vehicleState = traffic != null
                ? traffic.CurrentState.ToString()
                : "No TrafficTestVehicle";

            queue +=
                $"#{i + 1}: {vehicle.name} " +
                $"[{vehicleState}]\n";
        }

        if (string.IsNullOrEmpty(queue))
            queue = "Queue is empty.";

        string parkedVehicleName =
            parkedVehicle != null
                ? parkedVehicle.name
                : "None";

        float estimatedWait = Mathf.Max(
            0f,
            GetEstimatedAvailableTime(5f) - Time.time);

        debugQueueStatus =
            $"{reason}\n" +
            $"Space: {name}\n" +
            $"Occupied: {IsOccupied}\n" +
            $"Parked vehicle: {parkedVehicleName}\n" +
            $"Reserved: {IsReserved}\n" +
            $"Queue count: {waitingVehicles.Count}\n" +
            $"Estimated available in: {estimatedWait:F1}s\n" +
            $"Queue:\n{queue}";
    }

    [ContextMenu("Debug Parking Space")]
    private void DebugParkingSpace()
    {
        CleanWaitingVehicles();

        PlacedBuilding placedBuilding =
            GetComponent<PlacedBuilding>();

        string positionText = "Unknown";
        string sizeText = "Unknown";
        string rotationText = "Unknown";

        if (placedBuilding != null)
        {
            positionText = placedBuilding.Origin.ToString();

            sizeText =
                $"{placedBuilding.Width}x{placedBuilding.Depth}";

            rotationText = placedBuilding.Rotation.ToString();
        }

        RefreshDebugQueueStatus("MANUAL DEBUG CHECK");

        Debug.Log(
            $"PARKING SPACE | {name} | " +
            $"Origin: {positionText} | " +
            $"Size: {sizeText} | " +
            $"Rotation: {rotationText} | " +
            $"Occupied: {IsOccupied} | " +
            $"Reserved: {IsReserved} | " +
            $"Queue Count: {waitingVehicles.Count} | " +
            $"Estimated Available In: " +
            $"{Mathf.Max(0f, GetEstimatedAvailableTime(5f) - Time.time):F1}s\n" +
            debugQueueStatus,
            this);
    }
}
