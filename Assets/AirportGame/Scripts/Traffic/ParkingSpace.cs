using System.Collections.Generic;
using UnityEngine;

public class ParkingSpace : MonoBehaviour
{
    public bool IsReserved
    {
        get
        {
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

    public bool Reserve(
        GameObject vehicle)
    {
        if (vehicle == null)
        {
            Debug.LogWarning(
                "ParkingSpace: Cannot reserve " +
                "with a null vehicle."
            );

            return false;
        }

        if (parkedVehicle == vehicle)
            return true;

        if (waitingVehicles.Contains(vehicle))
            return true;

        waitingVehicles.Add(
            vehicle
        );

        return true;
    }

    public bool CanVehicleEnter(
        GameObject vehicle)
    {
        if (vehicle == null)
            return false;

        if (parkedVehicle != null)
            return false;

        if (waitingVehicles.Count == 0)
            return false;

        return waitingVehicles[0] == vehicle;
    }

    public bool ParkVehicle(
        GameObject vehicle,
        float parkingDuration)
    {
        if (!CanVehicleEnter(vehicle))
            return false;

        waitingVehicles.RemoveAt(0);

        parkedVehicle =
            vehicle;

        occupiedUntil =
            Time.time +
            Mathf.Max(
                0f,
                parkingDuration
            );

        return true;
    }

    public void ReleaseVehicle(
        GameObject vehicle)
    {
        if (vehicle == null)
            return;

        if (parkedVehicle != vehicle)
            return;

        parkedVehicle = null;
    }

    public void CancelReservation(
        GameObject vehicle)
    {
        if (vehicle == null)
            return;

        waitingVehicles.Remove(
            vehicle
        );
    }

    public float GetEstimatedAvailableTime(
        float parkingDuration)
    {
        float availableTime =
            Time.time;

        if (parkedVehicle != null)
        {
            availableTime =
                Mathf.Max(
                    availableTime,
                    occupiedUntil
                );
        }

        availableTime +=
            waitingVehicles.Count *
            Mathf.Max(
                0f,
                parkingDuration
            );

        return availableTime;
    }

    public int GetQueuePosition(
        GameObject vehicle)
    {
        if (vehicle == null)
            return -1;

        int index =
            waitingVehicles.IndexOf(
                vehicle
            );

        if (index < 0)
            return -1;

        return index + 1;
    }

    [ContextMenu("Debug Parking Space")]
    private void DebugParkingSpace()
    {
        PlacedBuilding placedBuilding =
            GetComponent<PlacedBuilding>();

        string positionText =
            "Unknown";

        string sizeText =
            "Unknown";

        string rotationText =
            "Unknown";

        if (placedBuilding != null)
        {
            positionText =
                placedBuilding.Origin.ToString();

            sizeText =
                $"{placedBuilding.Width}x" +
                $"{placedBuilding.Depth}";

            rotationText =
                placedBuilding.Rotation.ToString();
        }

        Debug.Log(
            $"PARKING SPACE | " +
            $"Origin: {positionText} | " +
            $"Size: {sizeText} | " +
            $"Rotation: {rotationText} | " +
            $"Occupied: {IsOccupied} | " +
            $"Waiting: {waitingVehicles.Count} | " +
            $"Estimated Available In: " +
            $"{Mathf.Max(0f, GetEstimatedAvailableTime(5f) - Time.time):F1}s"
        );
    }
}