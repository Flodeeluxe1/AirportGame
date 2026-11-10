using System.Collections.Generic;
using UnityEngine;

public class EdgeOccupancy : MonoBehaviour
{
    public event System.Action OccupancyChanged;

    private readonly Dictionary<GridEdge, GameObject> occupiedEdges =
        new Dictionary<GridEdge, GameObject>();

    public bool IsOccupied(GridEdge edge)
    {
        return occupiedEdges.ContainsKey(edge);
    }

    public bool IsOccupied(
        GridEdge edge,
        GameObject ignoredObject)
    {
        if (!occupiedEdges.TryGetValue(
                edge,
                out GameObject occupant))
        {
            return false;
        }

        return occupant != ignoredObject;
    }

    public bool CanOccupy(GridEdge edge)
    {
        return !occupiedEdges.ContainsKey(edge);
    }

    public bool CanOccupy(
        GridEdge edge,
        GameObject ignoredObject)
    {
        return !IsOccupied(
            edge,
            ignoredObject
        );
    }

    public bool TryGetOccupant(
        GridEdge edge,
        out GameObject occupant)
    {
        return occupiedEdges.TryGetValue(
            edge,
            out occupant
        );
    }

    public GameObject GetOccupant(
        GridEdge edge)
    {
        if (occupiedEdges.TryGetValue(
                edge,
                out GameObject occupant))
        {
            return occupant;
        }

        return null;
    }

    public void Occupy(
        GridEdge edge,
        GameObject building)
    {
        if (building == null)
        {
            Debug.LogWarning(
                "EdgeOccupancy: Cannot occupy an edge " +
                "with a null GameObject."
            );

            return;
        }

        if (occupiedEdges.TryGetValue(edge, out GameObject previousOwner) &&
            previousOwner == building)
        {
            return;
        }

        occupiedEdges[edge] = building;
        OccupancyChanged?.Invoke();
    }

    public void Release(GridEdge edge)
    {
        if (occupiedEdges.Remove(edge))
            OccupancyChanged?.Invoke();
    }

    public void Release(
        GridEdge edge,
        GameObject expectedOwner)
    {
        if (!occupiedEdges.TryGetValue(
                edge,
                out GameObject currentOwner))
        {
            return;
        }

        if (currentOwner != expectedOwner)
            return;

        if (occupiedEdges.Remove(edge))
            OccupancyChanged?.Invoke();
    }

    public void ReleaseBuilding(
        GameObject building)
    {
        if (building == null)
            return;

        List<GridEdge> edgesToRelease =
            new List<GridEdge>();

        foreach (
            KeyValuePair<GridEdge, GameObject> entry
            in occupiedEdges)
        {
            if (entry.Value == building)
            {
                edgesToRelease.Add(
                    entry.Key
                );
            }
        }

        foreach (GridEdge edge in edgesToRelease)
            occupiedEdges.Remove(edge);

        if (edgesToRelease.Count > 0)
            OccupancyChanged?.Invoke();
    }

    public void Clear()
    {
        if (occupiedEdges.Count == 0)
            return;

        occupiedEdges.Clear();
        OccupancyChanged?.Invoke();
    }
}