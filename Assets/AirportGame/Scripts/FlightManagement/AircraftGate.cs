
using UnityEngine;

public enum AircraftGateSize
{
    Grass = 0,
    Small = 1,
    Middle = 2,
    Big = 3
}

public class AircraftGate : MonoBehaviour
{
    [Header("Gate")]
    [SerializeField]
    private AircraftGateSize gateSize =
        AircraftGateSize.Small;

    [SerializeField] private string displayNamePrefix = "Gate";

    public AircraftGateSize Size => gateSize;

    public bool CanAccommodate(
        AircraftGateSize requiredSize)
    {
        return gateSize >= requiredSize;
    }

    public bool TryGetGridOrigin(
        out Vector2Int origin)
    {
        PlacedBuilding placedBuilding =
            GetComponent<PlacedBuilding>();

        if (placedBuilding == null)
        {
            origin = Vector2Int.zero;
            return false;
        }

        origin = placedBuilding.Origin;
        return true;
    }

    public string StableId
    {
        get
        {
            if (!TryGetGridOrigin(out Vector2Int origin))
                return string.Empty;

            return $"gate_{origin.x}_{origin.y}";
        }
    }

    public string DisplayName
    {
        get
        {
            if (!TryGetGridOrigin(out Vector2Int origin))
                return $"{displayNamePrefix} (Unplaced)";

            return $"{displayNamePrefix} ({origin.x}, {origin.y})";
        }
    }

    [ContextMenu("Debug Aircraft Gate")]
    private void DebugAircraftGate()
    {
        if (!TryGetGridOrigin(out Vector2Int origin))
        {
            Debug.LogWarning(
                $"{name}: This gate has no PlacedBuilding component. " +
                "Place it through the building system first.",
                this
            );

            return;
        }

        Debug.Log(
            $"AIRCRAFT GATE | " +
            $"Name: {DisplayName} | " +
            $"Size: {Size} | " +
            $"Grid Origin: {origin} | " +
            $"Stable ID: {StableId}",
            this
        );
    }
}