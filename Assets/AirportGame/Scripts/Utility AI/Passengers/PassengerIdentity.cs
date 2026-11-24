using System;
using UnityEngine;

/// <summary>
/// Identifies an individual passenger and records the transport mode used
/// to arrive at the airport. The arrival mode determines the return journey.
/// </summary>
[DisallowMultipleComponent]
public class PassengerIdentity : MonoBehaviour
{
    public enum TransportMode
    {
        Car,
        Bus,
        Train,
        Taxi,
        Underground
    }

    [Header("Passenger")]
    [SerializeField] private string passengerId;
    [SerializeField] private TransportMode arrivalTransportMode;

    public string PassengerId => passengerId;
    public TransportMode ArrivalTransportMode => arrivalTransportMode;

    private void Awake()
    {
        EnsurePassengerId();
    }

    /// <summary>
    /// Assigns the passenger's arrival transport mode.
    /// Call this when the passenger is spawned or initialized.
    /// </summary>
    public void Initialize(TransportMode transportMode)
    {
        EnsurePassengerId();
        arrivalTransportMode = transportMode;
    }

    private void EnsurePassengerId()
    {
        if (string.IsNullOrWhiteSpace(passengerId))
            passengerId = Guid.NewGuid().ToString("N");
    }
}
