using System.Collections.Generic;
using UnityEngine;

public class RunwayNetworkRegistration : MonoBehaviour
{
    private TaxiwayNetwork taxiwayNetwork;
    private GridSurface gridSurface;
    private PlacedBuilding placedBuilding;

    private readonly List<Vector2Int> registeredCells =
        new List<Vector2Int>();

    private void Awake()
    {
        // Registration is initialized explicitly after PlacedBuilding
        // has received its saved or newly placed footprint.
    }

    public void Initialize(
        TaxiwayNetwork network,
        GridSurface surface,
        PlacedBuilding building)
    {
        UnregisterExistingCells();

        taxiwayNetwork = network;
        gridSurface = surface;
        placedBuilding = building;

        if (taxiwayNetwork == null ||
            placedBuilding == null ||
            placedBuilding.Definition == null)
        {
            Debug.LogError(
                $"RunwayNetworkRegistration on {name} cannot register: " +
                "TaxiwayNetwork or PlacedBuilding data is missing.",
                this);
            return;
        }

        RegisterFootprint();
    }

    private void RegisterFootprint()
    {
        Vector2Int origin = placedBuilding.Origin;
        int width = placedBuilding.Width;
        int depth = placedBuilding.Depth;
        BuildableDefinition definition = placedBuilding.Definition;

        if (width <= 0 || depth <= 0)
        {
            Debug.LogWarning(
                $"RunwayNetworkRegistration on {name} has an invalid footprint.",
                this);
            return;
        }

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                Vector2Int cell =
                    origin + new Vector2Int(x, z);

                if (!taxiwayNetwork.AddRunwayCell(
                        cell,
                        definition,
                        this))
                {
                    Debug.LogWarning(
                        $"Could not register runway cell {cell} " +
                        $"for {name}. The cell may already belong " +
                        "to another runway.",
                        this);
                    continue;
                }

                registeredCells.Add(cell);

                if (gridSurface != null)
                    gridSurface.RegisterRunwayCell(cell);
            }
        }

        // Register the grid-aligned runway centerline.
        // Width and depth already represent the rotated footprint.
        if (width >= depth)
        {
            int centerZ = origin.y + depth / 2;

            for (int x = 0; x < width; x++)
            {
                Vector2Int cell =
                    new Vector2Int(origin.x + x, centerZ);

                taxiwayNetwork.AddRunwayCenterlineCell(
                    cell,
                    this);
            }
        }
        else
        {
            int centerX = origin.x + width / 2;

            for (int z = 0; z < depth; z++)
            {
                Vector2Int cell =
                    new Vector2Int(centerX, origin.y + z);

                taxiwayNetwork.AddRunwayCenterlineCell(
                    cell,
                    this);
            }
        }

        Debug.Log(
            $"RUNWAY NETWORK REGISTERED | {name} | " +
            $"Origin: {origin} | Footprint: {width}x{depth} | " +
            $"Registered cells: {registeredCells.Count}",
            this);
    }

    public void RefreshSurfaceRegistration()
    {
        if (taxiwayNetwork == null || gridSurface == null)
            return;

        foreach (Vector2Int cell in registeredCells)
        {
            // Ignore stale components from objects awaiting destruction
            // during a layout reset or load.
            if (taxiwayNetwork.IsRunwayCellRegisteredBy(cell, this))
                gridSurface.RegisterRunwayCell(cell);
        }
    }

    private void UnregisterExistingCells()
    {
        foreach (Vector2Int cell in registeredCells)
        {
            if (taxiwayNetwork == null)
                continue;

            bool removed =
                taxiwayNetwork.RemoveRunwayCell(cell, this);

            // An old registration must not alter surfaces owned by
            // a newer runway registered at the same coordinates.
            if (!removed || gridSurface == null)
                continue;

            gridSurface.UnregisterRunwayCell(cell);

            // If a taxiway still occupies this cell, restore its surface.
            if (taxiwayNetwork.IsTaxiway(cell))
                gridSurface.RegisterTaxiwayCell(cell);
        }

        registeredCells.Clear();
    }

    private void OnDestroy()
    {
        UnregisterExistingCells();
    }
}