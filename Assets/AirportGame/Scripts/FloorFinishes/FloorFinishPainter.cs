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
    private Vector2Int lastPaintedCell = new Vector2Int(int.MinValue, int.MinValue);

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
            lastPaintedCell = new Vector2Int(int.MinValue, int.MinValue);
            PaintUnderPointer();
        }

        if (isDragging && mouse.leftButton.isPressed)
            PaintUnderPointer();

        if (mouse.leftButton.wasReleasedThisFrame)
        {
            isDragging = false;
            lastPaintedCell = new Vector2Int(int.MinValue, int.MinValue);
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
            return;

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

        if (lastPaintedCell.x == int.MinValue)
        {
            PaintCell(cell);
        }
        else
        {
            PaintLine(lastPaintedCell, cell);
        }

        lastPaintedCell = cell;
    }

    private void PaintLine(Vector2Int from, Vector2Int to)
    {
        int x = from.x;
        int z = from.y;
        int dx = Mathf.Abs(to.x - from.x);
        int dz = Mathf.Abs(to.y - from.y);
        int stepX = from.x < to.x ? 1 : -1;
        int stepZ = from.y < to.y ? 1 : -1;
        int error = dx - dz;

        while (true)
        {
            PaintCell(new Vector2Int(x, z));

            if (x == to.x && z == to.y)
                break;

            int twiceError = error * 2;

            if (twiceError > -dz)
            {
                error -= dz;
                x += stepX;
            }

            if (twiceError < dx)
            {
                error += dx;
                z += stepZ;
            }
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
