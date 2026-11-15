using UnityEngine;

/// <summary>
/// Defines a shared seating location for two hobby pilots.
/// Add this to a parent object and assign two child seat-position transforms.
/// </summary>
[DisallowMultipleComponent]
public class PilotSocialSeatingArea : MonoBehaviour
{
    [Header("Seat Positions")]
    [Tooltip("Assign at least two transforms. Each transform marks where a pilot should stand until sitting animations are available; its forward direction is not required.")]
    [SerializeField] private Transform[] seatPoints;

    private PilotSocialSeatingController firstOccupant;
    private PilotSocialSeatingController secondOccupant;

    public bool IsAvailable =>
        isActiveAndEnabled &&
        seatPoints != null &&
        seatPoints.Length >= 2 &&
        seatPoints[0] != null &&
        seatPoints[1] != null &&
        firstOccupant == null &&
        secondOccupant == null;

    public Transform[] GetSeatPointSnapshot()
    {
        if (seatPoints == null)
            return new Transform[0];

        return (Transform[])seatPoints.Clone();
    }

    public bool TryReservePair(
        PilotSocialSeatingController first,
        PilotSocialSeatingController second,
        out Transform firstSeat,
        out Transform secondSeat)
    {
        firstSeat = null;
        secondSeat = null;

        if (first == null || second == null || first == second || !IsAvailable)
            return false;

        firstOccupant = first;
        secondOccupant = second;
        firstSeat = seatPoints[0];
        secondSeat = seatPoints[1];
        return true;
    }

    public void ReleasePair(
        PilotSocialSeatingController first,
        PilotSocialSeatingController second)
    {
        bool matchesPair =
            (firstOccupant == first && secondOccupant == second) ||
            (firstOccupant == second && secondOccupant == first);

        if (!matchesPair)
            return;

        firstOccupant = null;
        secondOccupant = null;
    }

    private void OnDisable()
    {
        firstOccupant = null;
        secondOccupant = null;
    }

    private void OnValidate()
    {
        if (seatPoints == null)
            seatPoints = new Transform[0];
    }
}
