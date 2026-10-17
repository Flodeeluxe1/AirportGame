
using UnityEngine;

/// <summary>
/// Initializes a manually placed hobby aircraft as a VFR flight.
/// Attach this only to aircraft intended for hobby traffic.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AircraftFlightIdentity))]
public class HobbyAircraftIdentityInitializer : MonoBehaviour
{
    [Header("Hobby Flight Identity")]
    [SerializeField] private string flightNumber = "VFR101";

    private void Start()
    {
        AircraftFlightIdentity identity =
            GetComponent<AircraftFlightIdentity>();

        if (identity == null)
        {
            Debug.LogError(
                $"Hobby aircraft {name} has no AircraftFlightIdentity.",
                this
            );
            return;
        }

        // Never overwrite an identity that has already been initialized.
        if (identity.IsInitialized)
        {
            Debug.Log(
                $"Aircraft identity already initialized: " +
                $"{identity.FlightNumber}",
                this
            );
            return;
        }

        GameTimeManager timeManager =
            FindFirstObjectByType<GameTimeManager>();

        int startMinute = timeManager != null
            ? timeManager.Hour * 60 + timeManager.Minute
            : 0;

        long dayIndex = timeManager != null
            ? timeManager.CurrentDay
            : 1;

        identity.InitializeHobbyFlight(
            flightNumber,
            startMinute,
            dayIndex
        );

        Debug.Log(
            $"Hobby aircraft initialized: {flightNumber} | " +
            $"Day: {dayIndex} | Minute: {startMinute}",
            this
        );
    }
}
