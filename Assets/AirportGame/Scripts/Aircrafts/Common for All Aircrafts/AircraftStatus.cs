using UnityEngine;

[DisallowMultipleComponent]
public class AircraftStatus : MonoBehaviour
{
    public static event System.Action<AircraftStatus> StatusChanged;

    private AircraftFlightIdentity flightIdentity;

    private void Awake()
    {
        flightIdentity = GetComponent<AircraftFlightIdentity>();
    }

    private void NotifyStatusChanged()
    {
        StatusChanged?.Invoke(this);

        if (flightIdentity != null)
            flightIdentity.NotifyAircraftChanged();
    }

    public bool IsTaxiing { get; private set; }
    public bool IsParked { get; private set; }
    public bool IsFlying { get; private set; }

    public bool HasCompletedLanding { get; private set; }

    public float CurrentSpeedKnots { get; private set; }

    public const float WorldUnitsPerSecondPerKnot = 0.15f;

    public static float KnotsToWorldSpeed(float knots)
    {
        return Mathf.Max(0f, knots) * WorldUnitsPerSecondPerKnot;
    }

    public static float WorldSpeedToKnots(float worldSpeed)
    {
        return Mathf.Max(0f, worldSpeed) / WorldUnitsPerSecondPerKnot;
    }

    public void SetTaxiing(bool value)
    {
        bool changed = IsTaxiing != value ||
                       (value && (IsParked || IsFlying));

        IsTaxiing = value;

        if (value)
        {
            IsParked = false;
            IsFlying = false;
        }

        if (changed)
            NotifyStatusChanged();
    }

    public void SetParked(bool value)
    {
        bool changed = IsParked != value ||
                       (value && (IsTaxiing || IsFlying));

        IsParked = value;

        if (value)
        {
            IsTaxiing = false;
            IsFlying = false;
            CurrentSpeedKnots = 0f;
        }

        if (changed)
            NotifyStatusChanged();
    }

    public void SetFlying(bool value)
    {
        bool changed = IsFlying != value ||
                       (value && (IsTaxiing || IsParked));

        IsFlying = value;

        if (value)
        {
            IsTaxiing = false;
            IsParked = false;
        }

        if (changed)
            NotifyStatusChanged();
    }

    public void SetCurrentSpeedKnots(float speed)
    {
        CurrentSpeedKnots = Mathf.Max(0f, speed);
    }

    public void ResetForTrainingLesson()
    {
        bool changed = IsTaxiing || !IsParked || IsFlying ||
                       HasCompletedLanding;

        IsTaxiing = false;
        IsParked = true;
        IsFlying = false;
        HasCompletedLanding = false;
        CurrentSpeedKnots = 0f;

        if (changed)
            NotifyStatusChanged();
    }

    public void MarkLandingComplete()
    {
        if (HasCompletedLanding)
            return;

        HasCompletedLanding = true;
        NotifyStatusChanged();
    }
}