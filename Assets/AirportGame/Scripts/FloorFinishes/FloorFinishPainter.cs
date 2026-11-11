using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

public class FloorFinishPainter : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private GridManager gridManager;
    [SerializeField] private GridSurface gridSurface;
    [SerializeField] private FloorFinishRenderer floorFinishRenderer;
    [SerializeField] private UIDocument uiDocument;

    private VisualElement buildWindow;
    private FloorFinishDefinition selectedFinish;
    private bool isActive;
    private bool isDragging;
    private Vector2Int dragStartCell = new Vector2Int(int.MinValue, int.MinValue);

    public bool IsActive => isActive;

    private void Awake()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        if (gridManager == null)
            gridManager = FindFirstObjectByType<GridManager>();

        if (gridSurface == null && gridManager != null)
            gridSurface = gridManager.GetComponent<GridSurface>();

        if (floorFinishRenderer == null)
            floorFinishRenderer = FindFirstObjectByType<FloorFinishRenderer>();

        if (uiDocument == null)
            uiDocument = FindFirstObjectByType<UIDocument>();

        if (uiDocument != null)
            buildWindow = uiDocument.rootVisualElement.Q<VisualElement>("build-window");
    }

    private void Update()
    {
        if (!isActive)
            return;

        if (buildWindow != null &&
            buildWindow.resolvedStyle.display == DisplayStyle.None)
        {
            CancelPainting();
            return;
        }

        Mouse mouse = Mouse.current;
        Keyboard keyboard = Keyboard.current;

        if (mouse == null || keyboard == null)
            return;

        if (keyboard.escapeKey.wasPressedThisFrame ||
            mouse.rightButton.wasPressedThisFrame)
        {
            CancelPainting();
            return;
        }

        if (mouse.leftButton.wasPressedThisFrame)
        {
            if (IsPointerOverBuildUI())
                return;

            isDragging = true;
            dragStartCell = new Vector2Int(int.MinValue, int.MinValue);
            floorFinishRenderer?.BeginStroke();
            PaintUnderPointer();
        }

        if (isDragging && mouse.leftButton.isPressed)
            PaintUnderPointer();

        if (mouse.leftButton.wasReleasedThisFrame)
        {
            isDragging = false;
            floorFinishRenderer?.EndStroke();
            dragStartCell = new Vector2Int(int.MinValue, int.MinValue);
        }
    }

    public void BeginPainting(FloorFinishDefinition finish)
    {
        if (finish == null || finish.material == null)
        {
            Debug.LogWarning("FloorFinishPainter: Selected finish or its material is missing.", this);
            return;
        }

        selectedFinish = finish;
        isActive = true;
        isDragging = false;
        lastPaintedCell = new Vector2Int(int.MinValue, int.MinValue);
    }

    public void BeginErasing()
    {
        selectedFinish = null;
        isActive = true;
        isDragging = false;
        lastPaintedCell = new Vector2Int(int.MinValue, int.MinValue);
    }

    public void CancelPainting()
    {
        if (isDragging)
            floorFinishRenderer?.EndStroke();

        isActive = false;
        isDragging = false;
        selectedFinish = null;
        lastPaintedCell = new Vector2Int(int.MinValue, int.MinValue);
    }

    private void PaintUnderPointer()
    {
        if (mainCamera == null || gridManager == null ||
            gridSurface == null || floorFinishRenderer == null)
            return;

        if (IsPointerOverBuildUI())
        {
            lastPaintedCell = new Vector2Int(int.MinValue, int.MinValue);
            return;
        }

        Ray ray = mainCamera.ScreenPointToRay(
            Mouse.current.position.ReadValue());

        Plane gridPlane = new Plane(
            Vector3.up,
            new Vector3(0f, gridManager.transform.position.y, 0f));

        if (!gridPlane.Raycast(ray, out float distance))
            return;

        Vector3 hitPoint = ray.GetPoint(distance);
        Vector2Int cell = gridManager.WorldToGrid(hitPoint);

        if (!gridManager.IsInsideGrid(cell))
            return;

        if (dragStartCell.x == int.MinValue)
            dragStartCell = cell;

        PaintRectangle(dragStartCell, cell);
    }

    private void PaintRectangle(Vector2Int from, Vector2Int to)
    {
        int minX = Mathf.Min(from.x, to.x);
        int maxX = Mathf.Max(from.x, to.x);
        int minZ = Mathf.Min(from.y, to.y);
        int maxZ = Mathf.Max(from.y, to.y);

        for (int x = minX; x <= maxX; x++)
        {
            for (int z = minZ; z <= maxZ; z++)
                PaintCell(new Vector2Int(x, z));
        }
    }

    private void PaintCell(Vector2Int cell)
    {
        if (!gridManager.IsInsideGrid(cell) ||
            gridSurface.GetSurface(cell) != BuildSurface.Foundation)
        {
            return;
        }

        if (selectedFinish == null)
            floorFinishRenderer.ClearFinish(cell);
        else
            floorFinishRenderer.SetFinish(cell, selectedFinish);
    }

    private bool IsPointerOverBuildUI()
    {
        return buildWindow != null &&
               buildWindow.panel != null &&
               buildWindow.worldBound.Contains(
                   Mouse.current.position.ReadValue());
    }
}
