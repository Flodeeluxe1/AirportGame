using System.Collections.Generic;
using UnityEngine;

public class EdgePlacementHandler : IPlacementHandler
{
    private const float PreviewOrientationHysteresis = 0.15f;
    private static readonly Vector3 HoverPreviewScale =
        new Vector3(0.1f, 2.5f, 0.1f);

    private BuildController controller;
    private GridManager gridManager;
    private EdgeOccupancy edgeOccupancy;
    private GridSurface gridSurface;

    private BuildableDefinition definition;
    private GameObject previewObject;
    private PlacementPreview placementPreview;

    private GridEdge currentEdge;
    private GridEdge startEdge;

    private bool isDragging;
    private bool hasLockedCornerDirection;
    private bool hasHoverOrientation;
    private bool hasConfirmedDragDirection;
    private Vector3 originalPreviewScale;
    private Vector3 lastHitPoint;
    private Vector3 dragStartWorldPosition;

    private readonly List<GridEdge> previewEdges =
        new List<GridEdge>();

    private readonly List<GameObject> additionalPreviewObjects =
        new List<GameObject>();

    public void Initialize(
        BuildController controller,
        GridManager gridManager,
        GridOccupancy gridOccupancy,
        EdgeOccupancy edgeOccupancy)
    {
        this.controller = controller;
        this.gridManager = gridManager;
        this.edgeOccupancy = edgeOccupancy;

        gridSurface =
            gridManager.GetComponent<GridSurface>();
    }

    public void BeginPlacement(
        BuildableDefinition definition,
        GameObject previewObject,
        PlacementPreview placementPreview)
    {
        this.definition = definition;
        this.previewObject = previewObject;
        this.placementPreview = placementPreview;
        originalPreviewScale = previewObject.transform.localScale;

        currentEdge =
            new GridEdge(
                Vector2Int.zero,
                EdgeDirection.Horizontal
            );

        startEdge =
            currentEdge;

        isDragging = false;
        hasLockedCornerDirection = false;
        hasHoverOrientation = false;
        hasConfirmedDragDirection = false;
        lastHitPoint = gridManager.GridToWorld(Vector2Int.zero);
        dragStartWorldPosition = lastHitPoint;

        ClearAdditionalPreviews();
    }

    public void UpdatePlacement(Vector3 hitPoint)
    {
        lastHitPoint = hitPoint;

        Vector3 localPosition =
            hitPoint -
            gridManager.transform.position;

        float cellSize =
            gridManager.CellSize;

        float gridX =
            localPosition.x / cellSize;

        float gridZ =
            localPosition.z / cellSize;

        int cellX =
            Mathf.FloorToInt(gridX);

        int cellZ =
            Mathf.FloorToInt(gridZ);

        float localX =
            gridX - cellX;

        float localZ =
            gridZ - cellZ;

        float distanceToVertical =
            Mathf.Abs(localX);

        float distanceToHorizontal =
            Mathf.Abs(localZ);

        EdgeDirection nearestDirection;

        if (distanceToVertical <
            distanceToHorizontal)
        {
            nearestDirection =
                EdgeDirection.Vertical;
        }
        else
        {
            nearestDirection =
                EdgeDirection.Horizontal;
        }

        EdgeDirection selectedDirection =
            nearestDirection;

        Vector2Int selectedCell =
            new Vector2Int(cellX, cellZ);

        if (!isDragging)
        {
            if (!hasHoverOrientation)
            {
                hasHoverOrientation = true;
            }
            else if (nearestDirection != currentEdge.direction)
            {
                float currentDirectionDistance =
                    currentEdge.direction == EdgeDirection.Vertical
                        ? distanceToVertical
                        : distanceToHorizontal;

                float candidateDirectionDistance =
                    nearestDirection == EdgeDirection.Vertical
                        ? distanceToVertical
                        : distanceToHorizontal;

                if (candidateDirectionDistance +
                    PreviewOrientationHysteresis >=
                    currentDirectionDistance)
                {
                    selectedDirection = currentEdge.direction;
                }
            }
        }
        else
        {
            if (!hasConfirmedDragDirection)
            {
                Vector3 dragDelta =
                    hitPoint - dragStartWorldPosition;

                dragDelta.y = 0f;

                float directionThreshold =
                    cellSize * 0.15f;

                if (Mathf.Abs(dragDelta.x) < directionThreshold &&
                    Mathf.Abs(dragDelta.z) < directionThreshold)
                {
                    currentEdge = startEdge;
                    UpdateStartingMarker(startEdge);
                    return;
                }

                selectedDirection =
                    Mathf.Abs(dragDelta.x) >= Mathf.Abs(dragDelta.z)
                        ? EdgeDirection.Horizontal
                        : EdgeDirection.Vertical;

                Vector2Int firstEdgeCell =
                    startEdge.cell;

                // GridEdge.cell is the start endpoint of the segment.
                // When dragging in the negative axis direction, the
                // first segment must be placed on the negative side
                // of the marker rather than extending east/north.
                if (selectedDirection == EdgeDirection.Horizontal &&
                    dragDelta.x < 0f)
                {
                    firstEdgeCell += Vector2Int.left;
                }
                else if (selectedDirection == EdgeDirection.Vertical &&
                         dragDelta.z < 0f)
                {
                    firstEdgeCell += Vector2Int.down;
                }

                startEdge =
                    new GridEdge(
                        firstEdgeCell,
                        selectedDirection
                    );

                hasConfirmedDragDirection = true;
            }

            if (!hasLockedCornerDirection &&
                selectedDirection != startEdge.direction)
            {
                bool hasMovedPerpendicular =
                    startEdge.direction == EdgeDirection.Horizontal
                        ? selectedCell.y != startEdge.cell.y
                        : selectedCell.x != startEdge.cell.x;

                if (hasMovedPerpendicular)
                {
                    hasLockedCornerDirection = true;
                }
            }

            if (hasLockedCornerDirection)
            {
                selectedDirection =
                    startEdge.direction == EdgeDirection.Horizontal
                        ? EdgeDirection.Vertical
                        : EdgeDirection.Horizontal;
            }
            else
            {
                selectedDirection = startEdge.direction;
            }
        }

        currentEdge =
            new GridEdge(
                selectedCell,
                selectedDirection
            );

        if (isDragging)
        {
            UpdateWallPreview();
        }
        else
        {
            UpdateSinglePreview();
        }
    }

    private void UpdateSinglePreview()
    {
        UpdateStartingMarker(currentEdge);
    }

    private void UpdateStartingMarker(GridEdge markerEdge)
    {
        previewObject.transform.localScale = originalPreviewScale;

        ApplyEdgeTransform(
            previewObject,
            markerEdge
        );

        Vector3 markerPosition =
            gridManager.GridToWorld(markerEdge.cell);

        markerPosition.y = previewObject.transform.position.y;
        previewObject.transform.position = markerPosition;
        previewObject.transform.localScale = HoverPreviewScale;

        placementPreview.SetValid(
            IsEdgeValid(markerEdge)
        );

        ClearAdditionalPreviews();
    }

    public void BeginWall()
    {
        if (definition == null)
            return;

        startEdge =
            currentEdge;

        dragStartWorldPosition = lastHitPoint;
        hasConfirmedDragDirection = false;

        isDragging = true;
        hasLockedCornerDirection = false;

        UpdateStartingMarker(startEdge);
    }

    public void EndWall()
    {
        if (!isDragging)
            return;

        if (!hasConfirmedDragDirection)
        {
            isDragging = false;
            hasLockedCornerDirection = false;
            ClearAdditionalPreviews();
            UpdateSinglePreview();
            return;
        }

        UpdateWallPreview();

        LogWallPath();

        if (AreAllPreviewEdgesValid())
        {
            PlaceWallLine();
        }
        else
        {
            Debug.Log(
                "Cannot place wall: " +
                "one or more wall edges are invalid."
            );
        }

        isDragging = false;
        hasLockedCornerDirection = false;
        hasConfirmedDragDirection = false;

        ClearAdditionalPreviews();

        UpdateSinglePreview();
    }

    private void UpdateWallPreview()
    {
        BuildPreviewEdgeList();

        EnsurePreviewCount(
            previewEdges.Count
        );

        bool allValid =
            AreAllPreviewEdgesValid();

        for (
            int i = 0;
            i < previewEdges.Count;
            i++)
        {
            GameObject preview =
                GetPreviewObject(i);

            GridEdge edge =
                previewEdges[i];

            ApplyEdgeTransform(
                preview,
                edge
            );

            PlacementPreview previewComponent =
                preview.GetComponent<PlacementPreview>();

            if (previewComponent != null)
            {
                previewComponent.SetValid(
                    allValid
                );
            }
        }
    }

    private void BuildPreviewEdgeList()
    {
        previewEdges.Clear();

        Vector2Int start =
            startEdge.cell;

        Vector2Int end =
            currentEdge.cell;

        if (startEdge.direction ==
            currentEdge.direction)
        {
            AddStraightLine(
                start,
                end,
                startEdge.direction
            );

            return;
        }

        AddCornerPath(
            start,
            end
        );
    }

    private void AddStraightLine(
        Vector2Int start,
        Vector2Int end,
        EdgeDirection direction)
    {
        if (direction ==
            EdgeDirection.Horizontal)
        {
            int step =
                end.x >= start.x
                    ? 1
                    : -1;

            // A horizontal edge extends from its cell toward +X.
            // When dragging west from the locked start vertex,
            // begin with the edge immediately west of that vertex.
            int firstX =
                step < 0
                    ? start.x - 1
                    : start.x;

            for (
                int x = firstX;
                ;
                x += step)
            {
                previewEdges.Add(
                    new GridEdge(
                        new Vector2Int(
                            x,
                            start.y
                        ),
                        EdgeDirection.Horizontal
                    )
                );

                if (x == end.x)
                    break;
            }
        }
        else
        {
            int step =
                end.y >= start.y
                    ? 1
                    : -1;

            // A vertical edge extends from its cell toward +Z.
            // When dragging south from the locked start vertex,
            // begin with the edge immediately south of that vertex.
            int firstZ =
                step < 0
                    ? start.y - 1
                    : start.y;

            for (
                int z = firstZ;
                ;
                z += step)
            {
                previewEdges.Add(
                    new GridEdge(
                        new Vector2Int(
                            start.x,
                            z
                        ),
                        EdgeDirection.Vertical
                    )
                );

                if (z == end.y)
                    break;
            }
        }
    }

    private void AddCornerPath(
        Vector2Int start,
        Vector2Int end)
    {
        /*
         * GridEdge.cell represents the START of an edge segment:
         *
         * Horizontal (x,z) = (x,z) -> (x+1,z)
         * Vertical   (x,z) = (x,z) -> (x,z+1)
         *
         * Therefore the corner itself is a grid vertex, not an edge.
         */

        if (startEdge.direction ==
            EdgeDirection.Horizontal)
        {
            Vector2Int corner =
                new Vector2Int(
                    end.x,
                    start.y
                );

            if (corner.x > start.x)
            {
                for (
                    int x = start.x;
                    x < corner.x;
                    x++)
                {
                    previewEdges.Add(
                        new GridEdge(
                            new Vector2Int(
                                x,
                                start.y
                            ),
                            EdgeDirection.Horizontal
                        )
                    );
                }
            }
            else
            {
                for (
                    int x = start.x;
                    x >= corner.x;
                    x--)
                {
                    previewEdges.Add(
                        new GridEdge(
                            new Vector2Int(
                                x,
                                start.y
                            ),
                            EdgeDirection.Horizontal
                        )
                    );
                }
            }

            if (end.y > corner.y)
            {
                for (
                    int z = corner.y;
                    z <= end.y;
                    z++)
                {
                    previewEdges.Add(
                        new GridEdge(
                            new Vector2Int(
                                corner.x,
                                z
                            ),
                            EdgeDirection.Vertical
                        )
                    );
                }
            }
            else
            {
                for (
                    int z = corner.y - 1;
                    z >= end.y;
                    z--)
                {
                    previewEdges.Add(
                        new GridEdge(
                            new Vector2Int(
                                corner.x,
                                z
                            ),
                            EdgeDirection.Vertical
                        )
                    );
                }
            }
        }
        else
        {
            Vector2Int corner =
                new Vector2Int(
                    start.x,
                    end.y
                );

            if (corner.y > start.y)
            {
                for (
                    int z = start.y;
                    z < corner.y;
                    z++)
                {
                    previewEdges.Add(
                        new GridEdge(
                            new Vector2Int(
                                start.x,
                                z
                            ),
                            EdgeDirection.Vertical
                        )
                    );
                }
            }
            else
            {
                for (
                    int z = start.y;
                    z >= corner.y;
                    z--)
                {
                    previewEdges.Add(
                        new GridEdge(
                            new Vector2Int(
                                start.x,
                                z
                            ),
                            EdgeDirection.Vertical
                        )
                    );
                }
            }

            if (end.x > corner.x)
            {
                for (
                    int x = corner.x;
                    x <= end.x;
                    x++)
                {
                    previewEdges.Add(
                        new GridEdge(
                            new Vector2Int(
                                x,
                                corner.y
                            ),
                            EdgeDirection.Horizontal
                        )
                    );
                }
            }
            else
            {
                for (
                    int x = corner.x - 1;
                    x >= end.x;
                    x--)
                {
                    previewEdges.Add(
                        new GridEdge(
                            new Vector2Int(
                                x,
                                corner.y
                            ),
                            EdgeDirection.Horizontal
                        )
                    );
                }
            }
        }

        RemoveDuplicateEdges();
    }

    private void RemoveDuplicateEdges()
    {
        for (
            int i = previewEdges.Count - 1;
            i >= 0;
            i--)
        {
            for (
                int j = 0;
                j < i;
                j++)
            {
                if (previewEdges[i] ==
                    previewEdges[j])
                {
                    previewEdges.RemoveAt(i);
                    break;
                }
            }
        }
    }

    private void EnsurePreviewCount(
        int requiredCount)
    {
        while (
            additionalPreviewObjects.Count <
            requiredCount - 1)
        {
            GameObject newPreview =
                Object.Instantiate(
                    definition.prefab
                );

            newPreview.name =
                "Wall Placement Preview";

            PlacementPreview newPlacementPreview =
                newPreview.GetComponent<PlacementPreview>();

            if (newPlacementPreview == null)
            {
                newPlacementPreview =
                    newPreview.AddComponent<PlacementPreview>();
            }

            newPlacementPreview.Initialize();

            additionalPreviewObjects.Add(
                newPreview
            );
        }

        for (
            int i = 0;
            i < additionalPreviewObjects.Count;
            i++)
        {
            additionalPreviewObjects[i].SetActive(
                i < requiredCount - 1
            );
        }
    }

    private GameObject GetPreviewObject(
        int index)
    {
        if (index == 0)
            return previewObject;

        return additionalPreviewObjects[
            index - 1
        ];
    }

    private void ApplyEdgeTransform(
        GameObject target,
        GridEdge edge)
    {
        if (target == null)
            return;

        Vector3 position =
            gridManager.GridToWorld(
                edge.cell
            );

        float prefabHeight =
            GetPrefabHeight(target);

        position.y =
            prefabHeight * 0.5f;

        if (edge.direction ==
            EdgeDirection.Horizontal)
        {
            position += new Vector3(
                gridManager.CellSize * 0.5f,
                0f,
                0f
            );

            target.transform.position =
                position;

            target.transform.rotation =
                Quaternion.identity;
        }
        else
        {
            position += new Vector3(
                0f,
                0f,
                gridManager.CellSize * 0.5f
            );

            target.transform.position =
                position;

            target.transform.rotation =
                Quaternion.Euler(
                    0f,
                    90f,
                    0f
                );
        }
    }

    private float GetPrefabHeight(
        GameObject target)
    {
        Renderer[] renderers =
            target.GetComponentsInChildren<Renderer>(
                true
            );

        if (renderers.Length == 0)
        {
            return 0f;
        }

        Bounds bounds =
            renderers[0].bounds;

        for (
            int i = 1;
            i < renderers.Length;
            i++)
        {
            bounds.Encapsulate(
                renderers[i].bounds
            );
        }

        return bounds.size.y;
    }

    private bool AreAllPreviewEdgesValid()
    {
        if (previewEdges.Count == 0)
            return false;

        foreach (
            GridEdge edge
            in previewEdges)
        {
            if (!IsEdgeValid(edge))
                return false;
        }

        return true;
    }

    private bool IsEdgeValid(
        GridEdge edge)
    {
        if (!IsEdgeInsideGrid(edge))
            return false;

        if (edgeOccupancy == null ||
            !edgeOccupancy.CanOccupy(edge))
        {
            return false;
        }

        return HasRequiredFoundation(edge);
    }

    private bool HasRequiredFoundation(
        GridEdge edge)
    {
        if (definition.requiredSurface ==
            BuildSurface.Any)
        {
            return true;
        }

        if (gridSurface == null)
            return false;

        if (definition.requiredSurface ==
            BuildSurface.Foundation)
        {
            return HasFoundationAlongEdge(edge);
        }

        if (definition.requiredSurface ==
            BuildSurface.Grass)
        {
            return HasGrassAlongEdge(edge);
        }

        return false;
    }

    private bool HasFoundationAlongEdge(
        GridEdge edge)
    {
        Vector2Int cell =
            edge.cell;

        if (edge.direction ==
            EdgeDirection.Horizontal)
        {
            Vector2Int firstCell =
                cell;

            Vector2Int secondCell =
                cell +
                Vector2Int.down;

            return
                gridSurface.GetSurface(firstCell) ==
                    BuildSurface.Foundation ||
                gridSurface.GetSurface(secondCell) ==
                    BuildSurface.Foundation;
        }

        Vector2Int firstSide =
            cell;

        Vector2Int secondSide =
            cell +
            Vector2Int.left;

        return
            gridSurface.GetSurface(firstSide) ==
                BuildSurface.Foundation ||
            gridSurface.GetSurface(secondSide) ==
                BuildSurface.Foundation;
    }

    private bool HasGrassAlongEdge(
        GridEdge edge)
    {
        Vector2Int cell =
            edge.cell;

        if (edge.direction ==
            EdgeDirection.Horizontal)
        {
            Vector2Int firstCell =
                cell;

            Vector2Int secondCell =
                cell +
                Vector2Int.down;

            return
                gridSurface.GetSurface(firstCell) ==
                    BuildSurface.Grass ||
                gridSurface.GetSurface(secondCell) ==
                    BuildSurface.Grass;
        }

        Vector2Int firstSide =
            cell;

        Vector2Int secondSide =
            cell +
            Vector2Int.left;

        return
            gridSurface.GetSurface(firstSide) ==
                BuildSurface.Grass ||
            gridSurface.GetSurface(secondSide) ==
                BuildSurface.Grass;
    }

    private bool IsEdgeInsideGrid(
        GridEdge edge)
    {
        Vector2Int cell =
            edge.cell;

        if (edge.direction ==
            EdgeDirection.Horizontal)
        {
            return
                cell.x >= 0 &&
                cell.x < gridManager.Width &&
                cell.y >= 0 &&
                cell.y <= gridManager.Height;
        }

        return
            cell.x >= 0 &&
            cell.x <= gridManager.Width &&
            cell.y >= 0 &&
            cell.y < gridManager.Height;
    }

    private void PlaceWallLine()
    {
        if (!ConstructionCostUtility.TryPay(
                definition,
                previewEdges.Count,
                "Wall construction"))
        {
            return;
        }

        foreach (GridEdge edge in previewEdges)
        {
            GameObject placedObject =
                Object.Instantiate(definition.prefab);

            ApplyEdgeTransform(placedObject, edge);

            PlacedBuilding placedBuilding =
                placedObject.AddComponent<PlacedBuilding>();

            placedBuilding.Initialize(
                definition,
                edge.cell,
                1,
                1,
                edge.direction == EdgeDirection.Horizontal ? 0 : 90
            );

            edgeOccupancy.Occupy(edge, placedObject);
        }

        Debug.Log(
            $"Placed wall path: {previewEdges.Count} wall segment(s)."
        );
    }

    public void Rotate()
    {
        // Wall orientation is determined
        // by the edge under the mouse.
    }

    public void TryPlace()
    {
        // Wall placement uses click-and-drag.
    }

    private void ClearAdditionalPreviews()
    {
        foreach (
            GameObject preview
            in additionalPreviewObjects)
        {
            if (preview != null)
            {
                Object.Destroy(
                    preview
                );
            }
        }

        additionalPreviewObjects.Clear();
    }

    public void Cancel()
    {
        isDragging = false;
        hasLockedCornerDirection = false;
        hasConfirmedDragDirection = false;

        ClearAdditionalPreviews();
    }

    public void Reset()
    {
        ClearAdditionalPreviews();

        definition = null;
        previewObject = null;
        placementPreview = null;

        currentEdge =
            new GridEdge(
                Vector2Int.zero,
                EdgeDirection.Horizontal
            );

        startEdge =
            currentEdge;

        isDragging = false;
        hasLockedCornerDirection = false;
        hasHoverOrientation = false;
        hasConfirmedDragDirection = false;
        originalPreviewScale = Vector3.one;
        lastHitPoint = Vector3.zero;
        dragStartWorldPosition = Vector3.zero;

        previewEdges.Clear();
    }

    private void LogWallPath()
    {
        string log =
            $"WALL PATH\n" +
            $"Start: ({startEdge.cell.x}, {startEdge.cell.y}) " +
            $"{startEdge.direction}\n" +
            $"End:   ({currentEdge.cell.x}, {currentEdge.cell.y}) " +
            $"{currentEdge.direction}\n" +
            $"Segments: {previewEdges.Count}\n";

        for (
            int i = 0;
            i < previewEdges.Count;
            i++)
        {
            GridEdge edge =
                previewEdges[i];

            log +=
                $"  {i + 1}: " +
                $"({edge.cell.x}, {edge.cell.y}) " +
                $"{edge.direction}\n";
        }

        Debug.Log(log);
    }
}