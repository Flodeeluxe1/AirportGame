
using UnityEngine;

public class TaxiwayPlacementHandler : IPlacementHandler
{
    private GridManager gridManager;
    private GridOccupancy gridOccupancy;
    private GridSurface gridSurface;

    private TaxiwayPrefabRuleManager prefabRuleManager;
    private TaxiwayNetwork taxiwayNetwork;

    private BuildableDefinition definition;
    private GameObject previewObject;
    private PlacementPreview placementPreview;

    private Vector2Int currentGridPosition;
    private Vector2Int startPosition;
    private Vector2Int endPosition;

    private Vector3 originalPrefabScale;

    private int rotation;
    private bool isDragging;

    public void Initialize(
        BuildController controller,
        GridManager gridManager,
        GridOccupancy gridOccupancy,
        EdgeOccupancy edgeOccupancy)
    {
        this.gridManager = gridManager;
        this.gridOccupancy = gridOccupancy;

        gridSurface = gridManager != null
            ? gridManager.GetComponent<GridSurface>()
            : null;

        prefabRuleManager =
            Object.FindFirstObjectByType<TaxiwayPrefabRuleManager>();

        taxiwayNetwork =
            Object.FindFirstObjectByType<TaxiwayNetwork>();

        if (gridManager == null)
        {
            Debug.LogError(
                "TaxiwayPlacementHandler: GridManager was not found.");
        }

        if (prefabRuleManager == null)
        {
            Debug.LogError(
                "TaxiwayPlacementHandler: TaxiwayPrefabRuleManager was not found.");
        }

        if (taxiwayNetwork == null)
        {
            Debug.LogError(
                "TaxiwayPlacementHandler: TaxiwayNetwork was not found.");
        }
    }

    public void BeginPlacement(
        BuildableDefinition definition,
        GameObject previewObject,
        PlacementPreview placementPreview)
    {
        this.definition = definition;
        this.previewObject = previewObject;
        this.placementPreview = placementPreview;

        originalPrefabScale = previewObject != null
            ? previewObject.transform.localScale
            : Vector3.one;

        rotation = 0;
        isDragging = false;

        currentGridPosition = Vector2Int.zero;
        startPosition = Vector2Int.zero;
        endPosition = Vector2Int.zero;
    }

    public void UpdatePlacement(Vector3 hitPoint)
    {
        if (gridManager == null || definition == null)
            return;

        currentGridPosition = gridManager.WorldToGrid(hitPoint);

        if (!isDragging)
        {
            startPosition = currentGridPosition;
            endPosition = currentGridPosition;
        }
        else
        {
            endPosition = currentGridPosition;
        }

        UpdatePreview();
        UpdatePreviewValidity();
    }

    public void BeginTaxiway()
    {
        if (definition == null || gridManager == null)
            return;

        startPosition = currentGridPosition;
        endPosition = currentGridPosition;

        isDragging = true;

        UpdatePreview();
        UpdatePreviewValidity();
    }

    public void EndTaxiway()
    {
        if (!isDragging)
            return;

        endPosition = currentGridPosition;
        isDragging = false;

        TryPlace();
    }

    private void GetTaxiwayArea(
        out Vector2Int origin,
        out int areaWidth,
        out int areaDepth)
    {
        int taxiwayWidth = Mathf.Max(1, definition.width);

        if (rotation == 0)
        {
            // Fixed width along X; taxiway length along Z.
            int minZ = Mathf.Min(
                startPosition.y,
                endPosition.y);

            int length = Mathf.Abs(
                endPosition.y - startPosition.y) + 1;

            origin = new Vector2Int(
                startPosition.x - taxiwayWidth / 2,
                minZ);

            areaWidth = taxiwayWidth;
            areaDepth = length;
        }
        else
        {
            // Taxiway length along X; fixed width along Z.
            int minX = Mathf.Min(
                startPosition.x,
                endPosition.x);

            int length = Mathf.Abs(
                endPosition.x - startPosition.x) + 1;

            origin = new Vector2Int(
                minX,
                startPosition.y - taxiwayWidth / 2);

            areaWidth = length;
            areaDepth = taxiwayWidth;
        }
    }

    private void UpdatePreview()
    {
        if (previewObject == null ||
            gridManager == null ||
            definition == null)
        {
            return;
        }

        GetTaxiwayArea(
            out Vector2Int origin,
            out int areaWidth,
            out int areaDepth);

        Vector3 position = gridManager.GridToWorld(origin);

        position += new Vector3(
            areaWidth * gridManager.CellSize * 0.5f,
            0f,
            areaDepth * gridManager.CellSize * 0.5f);

        previewObject.transform.position = position;

        // The preview represents the rectangular footprint.
        // Rotation is handled by changing the footprint dimensions.
        previewObject.transform.rotation = Quaternion.identity;

        previewObject.transform.localScale = new Vector3(
            areaWidth * gridManager.CellSize,
            originalPrefabScale.y,
            areaDepth * gridManager.CellSize);
    }

    private void UpdatePreviewValidity()
    {
        if (placementPreview == null)
            return;

        if (definition == null || gridManager == null)
        {
            placementPreview.SetValid(false);
            return;
        }

        GetTaxiwayArea(
            out Vector2Int origin,
            out int areaWidth,
            out int areaDepth);

        placementPreview.SetValid(
            ValidateArea(
                origin,
                areaWidth,
                areaDepth,
                out _));
    }

    private bool ValidateArea(
        Vector2Int origin,
        int areaWidth,
        int areaDepth,
        out string reason)
    {
        reason = string.Empty;

        if (definition == null)
        {
            reason = "Taxiway BuildableDefinition is missing.";
            return false;
        }

        if (gridManager == null)
        {
            reason = "GridManager is missing.";
            return false;
        }

        if (prefabRuleManager == null)
        {
            reason = "TaxiwayPrefabRuleManager is missing.";
            return false;
        }

        if (taxiwayNetwork == null)
        {
            reason = "TaxiwayNetwork is missing.";
            return false;
        }

        if (areaWidth <= 0 || areaDepth <= 0)
        {
            reason = "Taxiway footprint has invalid dimensions.";
            return false;
        }

        // The prefab rule manager registers rectangular taxiway areas
        // with one dimension equal to the standard five-cell module width.
        if (areaWidth != 5 && areaDepth != 5)
        {
            reason =
                "Taxiway footprint must have a five-cell width in either orientation.";
            return false;
        }

        for (int x = 0; x < areaWidth; x++)
        {
            for (int z = 0; z < areaDepth; z++)
            {
                Vector2Int cell =
                    origin + new Vector2Int(x, z);

                if (!gridManager.IsInsideGrid(cell))
                {
                    reason = "Taxiway extends outside the grid.";
                    return false;
                }

                if (gridOccupancy != null &&
                    gridOccupancy.IsOccupied(cell))
                {
                    reason = $"Cell {cell} is occupied by a building.";
                    return false;
                }

                if (definition.requiredSurface != BuildSurface.Any)
                {
                    if (gridSurface == null ||
                        !gridSurface.IsSurface(
                            cell,
                            definition.requiredSurface))
                    {
                        reason = $"Cell {cell} has the wrong surface.";
                        return false;
                    }
                }

                // Preserve the existing rule that a different taxiway
                // definition cannot occupy an already registered cell.
                // Same-definition overlap remains permitted here.
                if (taxiwayNetwork.IsTaxiway(cell))
                {
                    BuildableDefinition existingDefinition =
                        taxiwayNetwork.GetDefinition(cell);

                    if (existingDefinition != definition)
                    {
                        reason =
                            $"Cell {cell} already contains a different taxiway definition.";
                        return false;
                    }
                }
            }
        }

        return true;
    }

    public void Rotate()
    {
        rotation = rotation == 0 ? 90 : 0;

        UpdatePreview();
        UpdatePreviewValidity();
    }

    public void TryPlace()
    {
        if (definition == null)
        {
            Debug.LogWarning(
                "Cannot place taxiway: BuildableDefinition is missing.");
            return;
        }

        GetTaxiwayArea(
            out Vector2Int origin,
            out int areaWidth,
            out int areaDepth);

        if (!ValidateArea(
                origin,
                areaWidth,
                areaDepth,
                out string reason))
        {
            Debug.Log($"Cannot place taxiway: {reason}");
            return;
        }

        int quantity = areaWidth * areaDepth;

        // Do not charge unless the footprint passes validation and
        // both the visual manager and network are available.
        if (!ConstructionCostUtility.TryPay(
                definition,
                quantity,
                "Taxiway construction"))
        {
            return;
        }

        // Register the visual area. The manager is responsible for
        // selecting, orienting and spawning the appropriate prefabs.
        prefabRuleManager.RegisterTaxiwayArea(
            origin,
            areaWidth,
            areaDepth);

        // Keep the current network registrations in place for existing
        // systems. The network redesign is a separate task.
        RegisterTaxiwayFootprint(
            origin,
            areaWidth,
            areaDepth);

        Debug.Log(
            $"TAXIWAY PLACED | {definition.displayName} | " +
            $"Origin: {origin} | Size: {areaWidth}x{areaDepth} | " +
            $"Rotation: {rotation}");

        endPosition = currentGridPosition;

        UpdatePreview();
        UpdatePreviewValidity();
    }

    private void RegisterTaxiwayFootprint(
        Vector2Int origin,
        int areaWidth,
        int areaDepth)
    {
        if (taxiwayNetwork == null)
            return;

        // Register every cell in the placed footprint.
        for (int x = 0; x < areaWidth; x++)
        {
            for (int z = 0; z < areaDepth; z++)
            {
                Vector2Int cell =
                    origin + new Vector2Int(x, z);

                taxiwayNetwork.AddTaxiwayCell(
                    cell,
                    definition);
            }
        }

        // Preserve the existing centerline registrations.
        // These remain separate from prefab visual placement.
        if (rotation == 0)
        {
            int centerX = origin.x + areaWidth / 2;

            for (int z = 0; z < areaDepth; z++)
            {
                Vector2Int centerCell = new Vector2Int(
                    centerX,
                    origin.y + z);

                taxiwayNetwork.AddTaxiwayCenterlineCell(
                    centerCell);
            }
        }
        else
        {
            int centerZ = origin.y + areaDepth / 2;

            for (int x = 0; x < areaWidth; x++)
            {
                Vector2Int centerCell = new Vector2Int(
                    origin.x + x,
                    centerZ);

                taxiwayNetwork.AddTaxiwayCenterlineCell(
                    centerCell);
            }
        }
    }

    public void Cancel()
    {
        isDragging = false;

        if (previewObject != null)
        {
            previewObject.transform.localScale =
                originalPrefabScale;
        }
    }

    public void Reset()
    {
        definition = null;
        previewObject = null;
        placementPreview = null;

        currentGridPosition = Vector2Int.zero;
        startPosition = Vector2Int.zero;
        endPosition = Vector2Int.zero;

        rotation = 0;
        isDragging = false;

        originalPrefabScale = Vector3.one;
    }
}
