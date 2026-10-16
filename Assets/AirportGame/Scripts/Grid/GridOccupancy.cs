using System.Collections.Generic;
using UnityEngine;

public class GridOccupancy : MonoBehaviour
{
    private readonly Dictionary<Vector2Int, GameObject> occupiedCells =
        new Dictionary<Vector2Int, GameObject>();

    public bool IsOccupied(Vector2Int position)
    {
        return occupiedCells.ContainsKey(position);
    }

    public bool IsOccupied(Vector2Int position, GameObject ignoredObject)
    {
        if (!occupiedCells.TryGetValue(position, out GameObject occupant))
            return false;

        return occupant != ignoredObject;
    }

    public bool CanOccupy(
        Vector2Int origin,
        int width,
        int depth,
        GameObject ignoredObject = null)
    {
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                Vector2Int cell = origin + new Vector2Int(x, z);

                if (IsOccupied(cell, ignoredObject))
                    return false;
            }
        }

        return true;
    }

    public void Occupy(
        Vector2Int origin,
        int width,
        int depth,
        GameObject building)
    {
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                Vector2Int cell = origin + new Vector2Int(x, z);

                occupiedCells[cell] = building;
            }
        }
    }

    public void Release(
        Vector2Int origin,
        int width,
        int depth)
    {
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                Vector2Int cell = origin + new Vector2Int(x, z);

                occupiedCells.Remove(cell);
            }
        }
    }

    public void ReleaseBuilding(GameObject building)
    {
        if (building == null)
            return;

        List<Vector2Int> cellsToRelease =
            new List<Vector2Int>();

        foreach (
            KeyValuePair<Vector2Int, GameObject> entry
            in occupiedCells)
        {
            if (entry.Value == building)
            {
                cellsToRelease.Add(
                    entry.Key
                );
            }
        }

        foreach (Vector2Int cell in cellsToRelease)
        {
            occupiedCells.Remove(cell);
        }
    }

    public void Clear()
    {
        occupiedCells.Clear();
    }
}