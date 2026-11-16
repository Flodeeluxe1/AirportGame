using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared spatial index for Utility AI world agents and interaction providers.
/// Components register on enable and update their bucket when their position changes.
/// Queries only inspect buckets intersecting the requested radius.
/// </summary>
public static class UtilityAISpatialRegistry<T> where T : Component
{
    private const float CellSize = 12f;

    private static readonly Dictionary<Vector2Int, List<T>> Buckets =
        new Dictionary<Vector2Int, List<T>>();
    private static readonly Dictionary<T, Vector2Int> ComponentCells =
        new Dictionary<T, Vector2Int>();

    public static void Register(T component)
    {
        if (component == null)
            return;

        Vector2Int cell = GetCell(component.transform.position);
        if (ComponentCells.TryGetValue(component, out Vector2Int previousCell))
        {
            if (previousCell == cell)
                return;

            RemoveFromBucket(component, previousCell);
        }

        AddToBucket(component, cell);
        ComponentCells[component] = cell;
    }

    public static void Unregister(T component)
    {
        if (ReferenceEquals(component, null))
            return;

        if (ComponentCells.TryGetValue(component, out Vector2Int cell))
        {
            RemoveFromBucket(component, cell);
            ComponentCells.Remove(component);
        }
    }

    public static void UpdatePosition(T component)
    {
        if (component == null)
            return;

        Vector2Int cell = GetCell(component.transform.position);
        if (ComponentCells.TryGetValue(component, out Vector2Int previousCell))
        {
            if (previousCell == cell)
                return;

            RemoveFromBucket(component, previousCell);
        }

        AddToBucket(component, cell);
        ComponentCells[component] = cell;
    }

    public static void GetNearby(Vector3 position, float radius, List<T> results)
    {
        if (results == null)
            return;

        results.Clear();
        radius = Mathf.Max(0f, radius);

        Vector2Int minimum = GetCell(new Vector3(
            position.x - radius, 0f, position.z - radius));
        Vector2Int maximum = GetCell(new Vector3(
            position.x + radius, 0f, position.z + radius));
        float radiusSquared = radius * radius;

        for (int x = minimum.x; x <= maximum.x; x++)
        {
            for (int y = minimum.y; y <= maximum.y; y++)
            {
                if (!Buckets.TryGetValue(new Vector2Int(x, y), out List<T> bucket))
                    continue;

                for (int i = 0; i < bucket.Count; i++)
                {
                    T component = bucket[i];
                    if (component == null)
                        continue;

                    Vector3 offset = component.transform.position - position;
                    offset.y = 0f;

                    if (offset.sqrMagnitude <= radiusSquared)
                        results.Add(component);
                }
            }
        }
    }

    public static void GetAll(List<T> results)
    {
        if (results == null)
            return;

        results.Clear();
        foreach (KeyValuePair<Vector2Int, List<T>> pair in Buckets)
        {
            List<T> bucket = pair.Value;
            for (int i = 0; i < bucket.Count; i++)
            {
                T component = bucket[i];
                if (component != null)
                    results.Add(component);
            }
        }
    }

    private static Vector2Int GetCell(Vector3 position)
    {
        return new Vector2Int(
            Mathf.FloorToInt(position.x / CellSize),
            Mathf.FloorToInt(position.z / CellSize));
    }

    private static void AddToBucket(T component, Vector2Int cell)
    {
        if (!Buckets.TryGetValue(cell, out List<T> bucket))
        {
            bucket = new List<T>(4);
            Buckets.Add(cell, bucket);
        }

        if (!bucket.Contains(component))
            bucket.Add(component);
    }

    private static void RemoveFromBucket(T component, Vector2Int cell)
    {
        if (!Buckets.TryGetValue(cell, out List<T> bucket))
            return;

        bucket.Remove(component);
        if (bucket.Count == 0)
            Buckets.Remove(cell);
    }
}
