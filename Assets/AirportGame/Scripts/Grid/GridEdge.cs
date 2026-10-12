using System;
using UnityEngine;

[Serializable]
public struct GridEdge : IEquatable<GridEdge>
{
    public Vector2Int cell;
    public EdgeDirection direction;

    public GridEdge(
        Vector2Int cell,
        EdgeDirection direction)
    {
        this.cell = cell;
        this.direction = direction;
    }

    public bool Equals(GridEdge other)
    {
        return cell == other.cell &&
               direction == other.direction;
    }

    public override bool Equals(object obj)
    {
        return obj is GridEdge other &&
               Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            cell,
            direction
        );
    }

    public static bool operator ==(
        GridEdge left,
        GridEdge right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(
        GridEdge left,
        GridEdge right)
    {
        return !left.Equals(right);
    }
}