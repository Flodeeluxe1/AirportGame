
using UnityEngine;

/// <summary>
/// Initializes a manually placed hobby aircraft as a VFR flight.
/// Attach this only to aircraft intended for hobby traffic.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AircraftFlightIdentity))]
public class HobbyAircraftIdentityInitializer : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool logToConsole = false;

    [Header("Hobby Flight Identity")]
    [SerializeField] private string flightNumber = "VFR101";

    private void Start()
    {
        AircraftFlightIdentity identity =
            GetComponent<AircraftFlightIdentity>();

        if (identity == null)
        {
            LogError(
                $"Hobby aircraft {name} has no AircraftFlightIdentity.",
                this
            );
            return;
        }

        // Never overwrite an identity that has already been initialized.
        if (identity.IsInitialized)
        {
            Log(
                $"Aircraft identity already initialized: " +
                $"{identity.FlightNumber}",
                this
            );
            return;
        }

        GameTimeManager timeManager =
            FindAnyObjectByType<GameTimeManager>();

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

        Log(
            $"Hobby aircraft initialized: {flightNumber} | " +
            $"Day: {dayIndex} | Minute: {startMinute}",
            this
        );
    }

    private void Log(object message, UnityEngine.Object context = null)
    {
        if (!logToConsole) return;
        if (context != null) Debug.Log(message, context); else Debug.Log(message);
    }
    private void LogWarning(object message, UnityEngine.Object context = null)
    {
        if (!logToConsole) return;
        if (context != null) Debug.LogWarning(message, context); else Debug.LogWarning(message);
    }
    private void LogError(object message, UnityEngine.Object context = null)
    {
        if (!logToConsole) return;
        if (context != null) Debug.LogError(message, context); else Debug.LogError(message);
    }

}
