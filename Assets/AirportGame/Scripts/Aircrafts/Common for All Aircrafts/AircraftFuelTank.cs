using UnityEngine;

/// <summary>
/// Fuel capacity and in-flight fuel consumption for an aircraft.
/// Attach to the aircraft root.
/// </summary>
[DisallowMultipleComponent]
public class AircraftFuelTank : MonoBehaviour
{
    [Header("Fuel Capacity")]
    [Min(1f)]
    [SerializeField] private float maximumFuelLitres = 200f;

    [Min(0f)]
    [SerializeField] private float currentFuelLitres = 200f;

    [Header("Fuel Consumption")]
    [Tooltip("Fuel consumed per in-game flight hour.")]
    [Min(0f)]
    [SerializeField] private float consumptionLitresPerHour = 40f;

    public float MaximumFuelLitres => maximumFuelLitres;
    public float CurrentFuelLitres => currentFuelLitres;
    public bool IsFull => currentFuelLitres >= maximumFuelLitres;
    public float FuelPercentage =>
        maximumFuelLitres > 0f ? currentFuelLitres / maximumFuelLitres : 0f;

    private AircraftStatus aircraftStatus;
    private AircraftFlightIdentity flightIdentity;

    private void Awake()
    {
        maximumFuelLitres = Mathf.Max(1f, maximumFuelLitres);
        currentFuelLitres = Mathf.Clamp(
            currentFuelLitres,
            0f,
            maximumFuelLitres);

        aircraftStatus = GetComponent<AircraftStatus>();
        flightIdentity = GetComponent<AircraftFlightIdentity>();
    }

    private void Update()
    {
        if (aircraftStatus == null)
            aircraftStatus = GetComponent<AircraftStatus>();

        if (flightIdentity == null)
            flightIdentity = GetComponent<AircraftFlightIdentity>();

        if (aircraftStatus == null ||
            flightIdentity == null ||
            !flightIdentity.IsInitialized ||
            !aircraftStatus.IsFlying ||
            currentFuelLitres <= 0f)
        {
            return;
        }

        currentFuelLitres = Mathf.Max(
            0f,
            currentFuelLitres -
            consumptionLitresPerHour * Time.deltaTime / 3600f);
    }

    /// <summary>
    /// Adds fuel and returns the amount accepted.
    /// </summary>
    public float AddFuel(float amountLitres)
    {
        if (amountLitres <= 0f)
            return 0f;

        float accepted = Mathf.Min(
            amountLitres,
            maximumFuelLitres - currentFuelLitres);

        currentFuelLitres += accepted;
        return accepted;
    }

    public void RestoreFuel(float savedFuelLitres)
    {
        currentFuelLitres = Mathf.Clamp(
            savedFuelLitres,
            0f,
            maximumFuelLitres);
    }
}
