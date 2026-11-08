using UnityEngine;

/// <summary>
/// Stores the grid coordinate represented by one generated pipeline visual.
/// </summary>
[DisallowMultipleComponent]
public sealed class PipelinePiece : MonoBehaviour
{
    public Vector2Int GridCell { get; private set; }

    public void Initialize(Vector2Int gridCell)
    {
        GridCell = gridCell;
    }
}
