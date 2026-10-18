using UnityEngine;

[DisallowMultipleComponent]
public class AircraftStatus : MonoBehaviour
{
    public bool IsTaxiing { get; private set; }
    public bool IsParked { get; private set; }
    public bool IsFlying { get; private set; }

    public bool HasCompletedLanding { get; private set; }

    public float CurrentSpeedKnots { get; private set; }

    public void SetTaxiing(bool value)
    {
        IsTaxiing = value;

        if (value)
        {
            IsParked = false;
            IsFlying = false;
        }
    }

    public void SetParked(bool value)
    {
        IsParked = value;

        if (value)
        {
            IsTaxiing = false;
            IsFlying = false;
            CurrentSpeedKnots = 0f;
        }
    }

    public void SetFlying(bool value)
    {
        IsFlying = value;

        if (value)
        {
            IsTaxiing = false;
            IsParked = false;
        }
    }

    public void SetCurrentSpeedKnots(float speed)
    {
        CurrentSpeedKnots = Mathf.Max(0f, speed);
    }

    public void ResetForTrainingLesson()
    {
        IsTaxiing = false;
        IsParked = true;
        IsFlying = false;
        HasCompletedLanding = false;
        CurrentSpeedKnots = 0f;
    }

    public void MarkLandingComplete()
    {
        HasCompletedLanding = true;
    }
}