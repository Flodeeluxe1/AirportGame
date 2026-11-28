using UnityEngine;

/// <summary>
/// Editor-only visualization of the virtual security boundary generated
/// by a placed body scanner. The boundary extends along the scanner's
/// local X axis until it reaches a wall or door on each side.
/// </summary>
[DisallowMultipleComponent]
public class SecurityBoundaryVisualizer : MonoBehaviour
{
    [SerializeField] private bool showBoundaryGizmo = true;
    [SerializeField, Min(0f)] private float lineHeight = 0.05f;

    private GridManager gridManager;
    private EdgeOccupancy edgeOccupancy;

    private void OnDrawGizmos()
    {
        if (!showBoundaryGizmo)
            return;

        PlacedBuilding placedBuilding = GetComponent<PlacedBuilding>();
        if (placedBuilding == null ||
            placedBuilding.Definition == null ||
            placedBuilding.Definition.passengerJourneyStationRole !=
                PassengerJourneyStationRole.BodySecurity)
        {
            return;
        }

        ResolveReferences();
        if (gridManager == null || edgeOccupancy == null)
            return;

        Vector3 localX = transform.right;
        bool alongGridX = Mathf.Abs(localX.x) >= Mathf.Abs(localX.z);
        int axisSign = alongGridX
            ? (localX.x >= 0f ? 1 : -1)
            : (localX.z >= 0f ? 1 : -1);

        Vector2Int positiveStep = alongGridX
            ? new Vector2Int(axisSign, 0)
            : new Vector2Int(0, axisSign);
        Vector2Int negativeStep = -positiveStep;

        Vector3 scannerCenter = transform.position;
        scannerCenter.y += lineHeight;

        bool foundPositive = TryFindWall(
            placedBuilding.Origin,
            positiveStep,
            out Vector3 positivePoint);

        bool foundNegative = TryFindWall(
            placedBuilding.Origin,
            negativeStep,
            out Vector3 negativePoint);

        if (foundPositive && foundNegative)
        {
            Gizmos.color = new Color(0.2f, 0.95f, 0.45f, 1f);
            Gizmos.DrawLine(negativePoint, positivePoint);
        }
        else
        {
            Gizmos.color = new Color(1f, 0.65f, 0.1f, 1f);

            if (foundNegative)
                Gizmos.DrawLine(negativePoint, scannerCenter);

            if (foundPositive)
                Gizmos.DrawLine(scannerCenter, positivePoint);

            Vector3 axis = alongGridX
                ? Vector3.right * axisSign
                : Vector3.forward * axisSign;

            if (!foundNegative)
                Gizmos.DrawLine(scannerCenter, scannerCenter - axis * gridManager.CellSize * 2f);

            if (!foundPositive)
                Gizmos.DrawLine(scannerCenter, scannerCenter + axis * gridManager.CellSize * 2f);
        }

        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(scannerCenter, gridManager.CellSize * 0.12f);
    }

    private void ResolveReferences()
    {
        if (gridManager == null)
            gridManager = FindAnyObjectByType<GridManager>();

        if (edgeOccupancy == null)
            edgeOccupancy = FindAnyObjectByType<EdgeOccupancy>();
    }

    private bool TryFindWall(
        Vector2Int scannerCell,
        Vector2Int step,
        out Vector3 wallPoint)
    {
        wallPoint = default;
        Vector2Int currentCell = scannerCell;
        int maximumSteps = gridManager.Width + gridManager.Height;

        for (int i = 0; i < maximumSteps; i++)
        {
            GridEdge sharedEdge = GetSharedEdge(currentCell, step);

            if (IsWallOrDoor(sharedEdge))
            {
                wallPoint = GetEdgeCenter(sharedEdge);
                wallPoint.y = transform.position.y + lineHeight;
                return true;
            }

            Vector2Int nextCell = currentCell + step;
            if (!gridManager.IsInsideGrid(nextCell))
                return false;

            currentCell = nextCell;
        }

        return false;
    }

    private static GridEdge GetSharedEdge(
        Vector2Int cell,
        Vector2Int step)
    {
        if (step.x > 0)
        {
            return new GridEdge(
                cell + Vector2Int.right,
                EdgeDirection.Vertical);
        }

        if (step.x < 0)
        {
            return new GridEdge(
                cell,
                EdgeDirection.Vertical);
        }

        if (step.y > 0)
        {
            return new GridEdge(
                cell + Vector2Int.up,
                EdgeDirection.Horizontal);
        }

        return new GridEdge(
            cell,
            EdgeDirection.Horizontal);
    }

    private bool IsWallOrDoor(GridEdge edge)
    {
        if (!edgeOccupancy.TryGetOccupant(
                edge,
                out GameObject occupant) ||
            occupant == null)
        {
            return false;
        }

        PlacedBuilding boundary = occupant.GetComponent<PlacedBuilding>();
        if (boundary == null || boundary.Definition == null)
            return false;

        BuildCategory category = boundary.Definition.category;
        return category == BuildCategory.Wall ||
               category == BuildCategory.Door;
    }

    private Vector3 GetEdgeCenter(GridEdge edge)
    {
        Vector3 point = gridManager.GridToWorld(edge.cell);

        if (edge.direction == EdgeDirection.Vertical)
            point.z += gridManager.CellSize * 0.5f;
        else
            point.x += gridManager.CellSize * 0.5f;

        return point;
    }
}
