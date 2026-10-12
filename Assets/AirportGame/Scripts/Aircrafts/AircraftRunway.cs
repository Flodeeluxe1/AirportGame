using UnityEngine;

public class AircraftRunway : MonoBehaviour
{
    [Header("Runway Surface")]
    [SerializeField]
    private float surfaceHeightOffset = 0f;

    public bool TryGetGeometry(
        GridManager gridManager,
        out Vector3 center,
        out Vector3 runwayAxis,
        out float runwayLength,
        out float surfaceY)
    {
        center = Vector3.zero;
        runwayAxis = Vector3.zero;
        runwayLength = 0f;
        surfaceY = 0f;

        if (gridManager == null)
            return false;

        PlacedBuilding placedBuilding =
            GetComponent<PlacedBuilding>();

        if (placedBuilding == null)
            return false;

        float footprintWidth =
            placedBuilding.Width *
            gridManager.CellSize;

        float footprintDepth =
            placedBuilding.Depth *
            gridManager.CellSize;

        if (footprintWidth <= 0f ||
            footprintDepth <= 0f)
        {
            return false;
        }

        Vector3 origin =
            gridManager.GridToWorld(
                placedBuilding.Origin
            );

        center = origin + new Vector3(
            footprintWidth * 0.5f,
            0f,
            footprintDepth * 0.5f
        );

        surfaceY =
            transform.position.y +
            surfaceHeightOffset;

        center.y = surfaceY;

        if (placedBuilding.Width >=
            placedBuilding.Depth)
        {
            runwayAxis = Vector3.right;
            runwayLength = footprintWidth;
        }
        else
        {
            runwayAxis = Vector3.forward;
            runwayLength = footprintDepth;
        }

        return true;
    }
}