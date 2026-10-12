using UnityEngine;

public interface IPlacementHandler
{
    void Initialize(
        BuildController controller,
        GridManager gridManager,
        GridOccupancy gridOccupancy,
        EdgeOccupancy edgeOccupancy
    );

    void BeginPlacement(
        BuildableDefinition definition,
        GameObject previewObject,
        PlacementPreview placementPreview
    );

    void UpdatePlacement(Vector3 hitPoint);

    void Rotate();

    void TryPlace();

    void Cancel();

    void Reset();
}