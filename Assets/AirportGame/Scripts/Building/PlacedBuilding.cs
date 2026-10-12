using UnityEngine;

public class PlacedBuilding : MonoBehaviour
{
    public BuildableDefinition Definition { get; private set; }

    public Vector2Int Origin { get; private set; }

    public int Width { get; private set; }

    public int Depth { get; private set; }

    public int Rotation { get; private set; }

    public int EdgeLength { get; private set; }

    public EdgeDirection EdgeDirection { get; private set; }

    public bool IsEdgeBuilding
    {
        get
        {
            return Definition != null &&
                   Definition.placementType ==
                   PlacementType.Edge;
        }
    }

    public void Initialize(
        BuildableDefinition definition,
        Vector2Int origin,
        int width,
        int depth,
        int rotation)
    {
        Definition = definition;
        Origin = origin;
        Width = width;
        Depth = depth;
        Rotation = rotation;

        EdgeLength = 0;

        EdgeDirection =
            rotation == 90 ||
            rotation == 270
                ? EdgeDirection.Vertical
                : EdgeDirection.Horizontal;
    }

    public void InitializeEdge(
        BuildableDefinition definition,
        Vector2Int origin,
        int edgeLength,
        EdgeDirection direction)
    {
        Definition = definition;

        Origin = origin;

        Width = 1;
        Depth = 1;

        EdgeLength =
            Mathf.Max(
                1,
                edgeLength
            );

        EdgeDirection =
            direction;

        Rotation =
            direction ==
            EdgeDirection.Horizontal
                ? 0
                : 90;
    }
}
