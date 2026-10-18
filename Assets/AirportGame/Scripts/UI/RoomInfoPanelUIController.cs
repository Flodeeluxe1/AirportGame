
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Rendering;

public class RoomInfoPanelUIController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private RoomPurposeController roomPurposeController;
    [SerializeField] private GridManager gridManager;

    [Header("Panel")]
    [SerializeField] private float panelWidth = 320f;
    [SerializeField] private float panelPadding = 18f;

    [Header("Room Highlight")]
    [SerializeField]
    private Color highlightColor =
        new Color(1f, 0.82f, 0f, 0.28f);

    [SerializeField] private float highlightHeight = 0.06f;

    [Header("UI Colors")]
    [SerializeField]
    private Color panelBackgroundColor =
        new Color(0.10f, 0.12f, 0.15f, 0.98f);

    [SerializeField]
    private Color primaryTextColor =
        new Color(0.95f, 0.96f, 0.98f, 1f);

    [SerializeField]
    private Color secondaryTextColor =
        new Color(0.72f, 0.76f, 0.82f, 1f);

    [SerializeField]
    private Color assignButtonColor =
        new Color(0.18f, 0.48f, 0.28f, 1f);

    [SerializeField]
    private Color cancelButtonColor =
        new Color(0.25f, 0.28f, 0.33f, 1f);

    private VisualElement root;
    private VisualElement panel;
    private Label roomTitleLabel;
    private Label roomSizeLabel;
    private Label currentPurposeLabel;
    private DropdownField purposeDropdown;
    private Label requirementsLabel;
    private Label validationLabel;
    private Button assignButton;
    private Button cancelButton;
    private Button closeButton;

    private RoomDetectionController.DetectedRoom selectedRoom;
    private RoomPurpose pendingPurpose = RoomPurpose.None;

    private GameObject highlightObject;
    private Mesh highlightMesh;
    private Material highlightMaterial;

    private bool isOpen;

    private void Awake()
    {
        ResolveReferences();

        if (uiDocument == null)
        {
            Debug.LogError(
                "RoomInfoPanelUIController: UIDocument is not assigned.",
                this
            );
            return;
        }

        root = uiDocument.rootVisualElement;

        BuildPanel();
        ClosePanel();
    }

    private void OnDestroy()
    {
        DestroyHighlight();

        if (panel != null && panel.parent != null)
        {
            panel.RemoveFromHierarchy();
        }
    }

    private void ResolveReferences()
    {
        if (uiDocument == null)
        {
            uiDocument = GetComponent<UIDocument>();
        }

        if (roomPurposeController == null)
        {
            roomPurposeController =
                FindFirstObjectByType<RoomPurposeController>();
        }

        if (gridManager == null)
        {
            gridManager = FindFirstObjectByType<GridManager>();
        }
    }

    private void BuildPanel()
    {
        panel = new VisualElement();
        panel.name = "RoomInfoPanel";

        panel.style.position = Position.Absolute;
        panel.style.top = 0;
        panel.style.right = 0;
        panel.style.bottom = 0;
        panel.style.width = panelWidth;
        panel.style.paddingLeft = panelPadding;
        panel.style.paddingRight = panelPadding;
        panel.style.paddingTop = panelPadding;
        panel.style.paddingBottom = panelPadding;
        panel.style.backgroundColor = panelBackgroundColor;
        panel.style.borderLeftWidth = 1;
        panel.style.borderLeftColor = new Color(1f, 1f, 1f, 0.12f);

        panel.style.display = DisplayStyle.None;

        ScrollView scrollView = new ScrollView();
        scrollView.style.flexGrow = 1;
        panel.Add(scrollView);

        VisualElement header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.justifyContent = Justify.SpaceBetween;
        header.style.marginBottom = 18;
        scrollView.Add(header);

        roomTitleLabel = CreateLabel("Room Information", 20, true);
        roomTitleLabel.style.flexGrow = 1;
        header.Add(roomTitleLabel);

        closeButton = new Button(ClosePanel);
        closeButton.text = "X";
        closeButton.style.width = 32;
        closeButton.style.height = 32;
        closeButton.style.color = primaryTextColor;
        closeButton.style.backgroundColor = cancelButtonColor;
        header.Add(closeButton);

        roomSizeLabel = CreateLabel("Room size: --", 13, false);
        roomSizeLabel.style.marginBottom = 14;
        scrollView.Add(roomSizeLabel);

        AddSectionTitle(scrollView, "Room Purpose");

        currentPurposeLabel =
            CreateLabel("Current assignment: None", 14, false);
        currentPurposeLabel.style.marginBottom = 10;
        scrollView.Add(currentPurposeLabel);

        purposeDropdown = new DropdownField();
        purposeDropdown.choices = new List<string>(
            Enum.GetNames(typeof(RoomPurpose))
        );
        purposeDropdown.value = RoomPurpose.None.ToString();
        purposeDropdown.style.color = primaryTextColor;
        purposeDropdown.style.backgroundColor =
            new Color(0.16f, 0.18f, 0.22f, 1f);
        purposeDropdown.style.marginBottom = 16;
        scrollView.Add(purposeDropdown);

        AddSectionTitle(scrollView, "Requirements");

        requirementsLabel = CreateLabel("", 13, false);
        requirementsLabel.style.whiteSpace = WhiteSpace.Normal;
        requirementsLabel.style.marginBottom = 14;
        scrollView.Add(requirementsLabel);

        AddSectionTitle(scrollView, "Validation");

        validationLabel = CreateLabel("", 14, true);
        validationLabel.style.whiteSpace = WhiteSpace.Normal;
        validationLabel.style.marginBottom = 20;
        scrollView.Add(validationLabel);

        VisualElement buttonRow = new VisualElement();
        buttonRow.style.flexDirection = FlexDirection.Row;
        buttonRow.style.justifyContent = Justify.SpaceBetween;
        buttonRow.style.marginTop = 8;
        scrollView.Add(buttonRow);

        assignButton = new Button(ApplyAssignment);
        assignButton.text = "Assign";
        assignButton.style.flexGrow = 1;
        assignButton.style.height = 38;
        assignButton.style.marginRight = 8;
        assignButton.style.color = primaryTextColor;
        assignButton.style.backgroundColor = assignButtonColor;
        buttonRow.Add(assignButton);

        cancelButton = new Button(ClosePanel);
        cancelButton.text = "Cancel";
        cancelButton.style.flexGrow = 1;
        cancelButton.style.height = 38;
        cancelButton.style.color = primaryTextColor;
        cancelButton.style.backgroundColor = cancelButtonColor;
        buttonRow.Add(cancelButton);

        root.Add(panel);
    }

    private Label CreateLabel(string text, int fontSize, bool bold)
    {
        Label label = new Label(text);

        label.style.color = primaryTextColor;
        label.style.fontSize = fontSize;
        label.style.unityFontStyleAndWeight =
            bold ? FontStyle.Bold : FontStyle.Normal;

        return label;
    }

    private void AddSectionTitle(VisualElement parent, string text)
    {
        Label label = CreateLabel(text, 15, true);
        label.style.marginBottom = 8;
        parent.Add(label);
    }

    public void OpenForRoom(
        RoomDetectionController.DetectedRoom room)
    {
        if (room == null)
        {
            return;
        }

        ResolveReferences();

        selectedRoom = room;
        pendingPurpose = roomPurposeController != null
            ? roomPurposeController.GetAssignedPurpose(room)
            : RoomPurpose.None;

        if (purposeDropdown != null)
        {
            purposeDropdown.SetValueWithoutNotify(
                pendingPurpose.ToString()
            );

            purposeDropdown.RegisterValueChangedCallback(evt =>
            {
                if (Enum.TryParse(evt.newValue, out RoomPurpose parsed))
                {
                    pendingPurpose = parsed;
                    UpdateValidationDisplay();
                }
            });
        }

        isOpen = true;

        if (panel != null)
        {
            panel.style.display = DisplayStyle.Flex;
        }

        UpdateRoomInformation();
        BuildHighlight();
    }

    public void ClosePanel()
    {
        isOpen = false;
        selectedRoom = null;

        if (panel != null)
        {
            panel.style.display = DisplayStyle.None;
        }

        DestroyHighlight();
    }

    public bool IsPointerOverPanel(Vector2 screenPosition)
    {
        if (!isOpen || panel == null)
        {
            return false;
        }

        return screenPosition.x >= Screen.width - panelWidth;
    }

    private void UpdateRoomInformation()
    {
        if (selectedRoom == null)
        {
            return;
        }

        roomTitleLabel.text = "Room Information";
        roomSizeLabel.text =
            $"Foundation cells: {selectedRoom.CellCount}";

        currentPurposeLabel.text =
            $"Current assignment: {GetCurrentPurposeName()}";

        UpdateValidationDisplay();
    }

    private string GetCurrentPurposeName()
    {
        if (roomPurposeController == null || selectedRoom == null)
        {
            return RoomPurpose.None.ToString();
        }

        return roomPurposeController
            .GetAssignedPurpose(selectedRoom)
            .ToString();
    }

    private void UpdateValidationDisplay()
    {
        if (selectedRoom == null)
        {
            return;
        }

        if (pendingPurpose == RoomPurpose.None)
        {
            requirementsLabel.text =
                "No room purpose selected.\n\n" +
                "Choose a purpose to see its requirements.";

            validationLabel.text =
                "This room has no assigned purpose.";

            validationLabel.style.color = secondaryTextColor;
            return;
        }

        if (pendingPurpose == RoomPurpose.FlightSchool)
        {
            requirementsLabel.text =
                "Flight School requirements:\n" +
                "• At least 20 foundation cells\n" +
                "• Fully enclosed by walls or doors\n" +
                "• At least one door\n" +
                "• At least one instructor desk\n" +
                "• At least one student seat\n" +
                "• Required furniture must be inside this room";

            if (roomPurposeController != null)
            {
                bool valid = roomPurposeController.ValidateFlightSchool(
                    selectedRoom,
                    out string validationMessage
                );

                validationLabel.text = valid
                    ? "Valid: this room meets the requirements."
                    : "Not valid yet:\n" + validationMessage;

                validationLabel.style.color = valid
                    ? new Color(0.35f, 0.85f, 0.48f, 1f)
                    : new Color(1f, 0.55f, 0.40f, 1f);
            }
            else
            {
                validationLabel.text =
                    "Cannot validate: RoomPurposeController is missing.";

                validationLabel.style.color =
                    new Color(1f, 0.55f, 0.40f, 1f);
            }
        }
        else
        {
            requirementsLabel.text =
                "No validation requirements have been configured for " +
                pendingPurpose + " yet.";

            validationLabel.text =
                "Validation for this purpose is not implemented yet.";

            validationLabel.style.color = secondaryTextColor;
        }
    }

    private void ApplyAssignment()
    {
        if (selectedRoom == null || roomPurposeController == null)
        {
            Debug.LogWarning(
                "Cannot assign room purpose: room or controller is missing.",
                this
            );
            return;
        }

        roomPurposeController.AssignPurpose(
            selectedRoom,
            pendingPurpose
        );

        ClosePanel();
    }

    private void BuildHighlight()
    {
        DestroyHighlight();

        if (selectedRoom == null || gridManager == null)
        {
            return;
        }

        if (selectedRoom.cells == null || selectedRoom.cells.Count == 0)
        {
            return;
        }

        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();

        Vector3 gridOrigin = gridManager.transform.position;

        Vector3 center00 = gridManager.GridToWorldCenter(Vector2Int.zero);
        Vector3 center10 = gridManager.GridToWorldCenter(Vector2Int.right);
        Vector3 center01 = gridManager.GridToWorldCenter(Vector2Int.up);

        float cellWidth = Vector3.Distance(center00, center10);
        float cellDepth = Vector3.Distance(center00, center01);

        if (cellWidth <= 0.0001f || cellDepth <= 0.0001f)
        {
            Debug.LogWarning(
                "Could not determine grid cell dimensions for room highlight.",
                this
            );
            return;
        }

        float halfWidth = cellWidth * 0.5f;
        float halfDepth = cellDepth * 0.5f;
        float y = gridOrigin.y + highlightHeight;

        foreach (Vector2Int cell in selectedRoom.cells)
        {
            Vector3 center = gridManager.GridToWorldCenter(cell);

            int start = vertices.Count;

            vertices.Add(new Vector3(
                center.x - halfWidth, y, center.z - halfDepth));

            vertices.Add(new Vector3(
                center.x - halfWidth, y, center.z + halfDepth));

            vertices.Add(new Vector3(
                center.x + halfWidth, y, center.z + halfDepth));

            vertices.Add(new Vector3(
                center.x + halfWidth, y, center.z - halfDepth));

            // Two triangles per foundation cell.
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);

            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }

        highlightMesh = new Mesh();
        highlightMesh.name = "Room Highlight Mesh";
        highlightMesh.SetVertices(vertices);
        highlightMesh.SetTriangles(triangles, 0);
        highlightMesh.RecalculateNormals();
        highlightMesh.RecalculateBounds();

        highlightObject = new GameObject("Room Highlight");
        highlightObject.transform.SetParent(transform, false);
        highlightObject.transform.position = Vector3.zero;
        highlightObject.transform.rotation = Quaternion.identity;
        highlightObject.transform.localScale = Vector3.one;

        MeshFilter meshFilter =
            highlightObject.AddComponent<MeshFilter>();

        meshFilter.sharedMesh = highlightMesh;

        MeshRenderer meshRenderer =
            highlightObject.AddComponent<MeshRenderer>();

        CreateHighlightMaterial();

        if (highlightMaterial == null)
        {
            DestroyHighlight();
            return;
        }

        meshRenderer.sharedMaterial = highlightMaterial;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage =
            UnityEngine.Rendering.LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage =
            UnityEngine.Rendering.ReflectionProbeUsage.Off;
    }

    private void CreateHighlightMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

        if (shader == null)
        {
            Debug.LogError(
                "Could not find URP Unlit shader for room highlight.",
                this
            );
            return;
        }

        highlightMaterial = new Material(shader);
        highlightMaterial.name = "Room Highlight Material";

        highlightMaterial.SetFloat("_Surface", 1.0f);
        highlightMaterial.SetFloat("_Blend", 0.0f);

        highlightMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        highlightMaterial.DisableKeyword("_SURFACE_TYPE_OPAQUE");
        highlightMaterial.DisableKeyword("_ALPHATEST_ON");
        highlightMaterial.DisableKeyword("_ALPHAMODULATE_ON");

        if (highlightMaterial.HasProperty("_BaseColor"))
        {
            Color color = highlightColor;
            color.a = 0.28f;
            highlightMaterial.SetColor("_BaseColor", color);
        }

        highlightMaterial.SetFloat("_ReceiveShadows", 0.0f);

        highlightMaterial.SetInt(
            "_SrcBlend",
            (int)BlendMode.SrcAlpha
        );

        highlightMaterial.SetInt(
            "_DstBlend",
            (int)BlendMode.OneMinusSrcAlpha
        );

        highlightMaterial.SetInt("_ZWrite", 0);
        highlightMaterial.renderQueue = 3000;
    }

    private void DestroyHighlight()
    {
        if (highlightObject != null)
        {
            Destroy(highlightObject);
            highlightObject = null;
        }

        if (highlightMesh != null)
        {
            Destroy(highlightMesh);
            highlightMesh = null;
        }

        if (highlightMaterial != null)
        {
            Destroy(highlightMaterial);
            highlightMaterial = null;
        }
    }
}
