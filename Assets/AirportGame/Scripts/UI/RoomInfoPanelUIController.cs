
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Rendering;

public class RoomInfoPanelUIController : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool logToConsole = false;
    [Header("References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private RoomPurposeController roomPurposeController;
    [SerializeField] private GridManager gridManager;
    [SerializeField] private FlightSchoolAssessmentController flightSchoolAssessmentController;

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
    private VisualElement assessmentSection;

    private Label roomTitleLabel;
    private Label roomSizeLabel;
    private Label currentPurposeLabel;
    private DropdownField purposeDropdown;
    private VisualElement requirementsLabel;
    private Label validationLabel;
    private Label assessmentLabel;

    private Button assignButton;
    private Button cancelButton;
    private Button closeButton;

    private RoomDetectionController.DetectedRoom selectedRoom;
    private RoomPurpose pendingPurpose = RoomPurpose.None;

    private GameObject highlightObject;
    private Mesh highlightMesh;
    private Material highlightMaterial;

    private bool isOpen;
    private float nextAssessmentRefreshTime;

    private const float AssessmentRefreshInterval = 0.5f;

    private void Awake()
    {
        ResolveReferences();

        if (uiDocument == null)
        {
            LogError(
                "RoomInfoPanelUIController: UIDocument is not assigned.",
                this
            );
            return;
        }

        root = uiDocument.rootVisualElement;

        BuildPanel();
        ClosePanel();
    }

    private void Update()
    {
        if (!isOpen || selectedRoom == null)
        {
            return;
        }

        if (Time.unscaledTime < nextAssessmentRefreshTime)
        {
            return;
        }

        nextAssessmentRefreshTime =
            Time.unscaledTime + AssessmentRefreshInterval;

        UpdateValidationDisplay();
        UpdateAssessmentDisplay();
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
                FindAnyObjectByType<RoomPurposeController>();
        }

        if (gridManager == null)
        {
            gridManager = FindAnyObjectByType<GridManager>();
        }

        if (flightSchoolAssessmentController == null)
        {
            flightSchoolAssessmentController =
                FindAnyObjectByType<FlightSchoolAssessmentController>();
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
        panel.style.borderLeftColor =
            new Color(1f, 1f, 1f, 0.12f);
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

        // Register once when building the panel, not every time it opens.
        purposeDropdown.RegisterValueChangedCallback(
            OnPurposeDropdownChanged
        );

        AddSectionTitle(scrollView, "Requirements");

        requirementsLabel = new VisualElement();
        requirementsLabel.style.marginBottom = 14;
        scrollView.Add(requirementsLabel);

        AddSectionTitle(scrollView, "Validation");

        validationLabel = CreateLabel("", 14, true);
        validationLabel.style.whiteSpace = WhiteSpace.Normal;
        validationLabel.style.marginBottom = 20;
        scrollView.Add(validationLabel);

        // Flight School assessment section.
        assessmentSection = new VisualElement();
        assessmentSection.name = "FlightSchoolAssessmentSection";
        assessmentSection.style.marginBottom = 18;
        assessmentSection.style.display = DisplayStyle.None;
        scrollView.Add(assessmentSection);

        AddSectionTitle(
            assessmentSection,
            "Flight School Assessment"
        );

        assessmentLabel = CreateLabel("", 13, false);
        assessmentLabel.style.whiteSpace = WhiteSpace.Normal;
        assessmentLabel.style.marginBottom = 12;
        assessmentSection.Add(assessmentLabel);

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

    private void OnPurposeDropdownChanged(ChangeEvent<string> evt)
    {
        if (Enum.TryParse(evt.newValue, out RoomPurpose parsed))
        {
            pendingPurpose = parsed;
            UpdateValidationDisplay();
            UpdateAssessmentDisplay();
        }
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
        }

        isOpen = true;
        nextAssessmentRefreshTime = 0f;

        if (panel != null)
        {
            panel.style.display = DisplayStyle.Flex;
        }

        UpdateRoomInformation();
        UpdateAssessmentDisplay();
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
        UpdateAssessmentDisplay();
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
        if (selectedRoom == null || requirementsLabel == null)
            return;

        requirementsLabel.Clear();

        void AddRequirement(string description, bool fulfilled)
        {
            Label row = CreateLabel(
                (fulfilled ? "✓ " : "✗ ") + description,
                13,
                false);

            row.style.color = fulfilled
                ? new Color(0.35f, 0.85f, 0.48f, 1f)
                : new Color(1f, 0.35f, 0.35f, 1f);

            row.style.whiteSpace = WhiteSpace.Normal;
            row.style.marginBottom = 4;
            requirementsLabel.Add(row);
        }

        void AddOptionalRequirement(string description, bool present)
        {
            Label row = CreateLabel(
                (present ? "✓ " : "○ ") + description + " (optional)",
                13,
                false);

            row.style.color = new Color(1f, 0.82f, 0.2f, 1f);
            row.style.whiteSpace = WhiteSpace.Normal;
            row.style.marginBottom = 4;
            requirementsLabel.Add(row);
        }

        if (pendingPurpose == RoomPurpose.None)
        {
            Label message = CreateLabel(
                "No room purpose selected.\nChoose a purpose to see its requirements.",
                13,
                false);
            message.style.whiteSpace = WhiteSpace.Normal;
            message.style.color = secondaryTextColor;
            requirementsLabel.Add(message);

            validationLabel.text = "This room has no assigned purpose.";
            validationLabel.style.color = secondaryTextColor;
            return;
        }

        if (roomPurposeController == null)
        {
            Label message = CreateLabel(
                "Cannot check requirements: RoomPurposeController is missing.",
                13,
                false);
            message.style.color = new Color(1f, 0.55f, 0.40f, 1f);
            requirementsLabel.Add(message);

            validationLabel.text = "Room validation is unavailable.";
            validationLabel.style.color = new Color(1f, 0.55f, 0.40f, 1f);
            return;
        }

        HashSet<Vector2Int> roomCells = new HashSet<Vector2Int>(selectedRoom.cells);
        int desks = 0;
        int seats = 0;
        int wallBoards = 0;
        int computers = 0;
        int projectors = 0;
        int books = 0;
        int printers = 0;

        PlacedBuilding[] placedBuildings = FindObjectsByType<PlacedBuilding>(
            FindObjectsInactive.Exclude);

        foreach (PlacedBuilding placed in placedBuildings)
        {
            if (placed == null || placed.Definition == null)
                continue;

            PlacedBuilding building = placed;
            if (building.IsSurfacePlaced)
            {
                building = building.SupportingSurface;
                if (building == null)
                    continue;
            }

            if (building.IsEdgeBuilding)
                continue;

            bool touchesRoom = false;
            for (int x = 0; x < Mathf.Max(1, building.Width) && !touchesRoom; x++)
            {
                for (int z = 0; z < Mathf.Max(1, building.Depth); z++)
                {
                    if (roomCells.Contains(building.Origin + new Vector2Int(x, z)))
                    {
                        touchesRoom = true;
                        break;
                    }
                }
            }

            if (!touchesRoom)
                continue;

            RoomFurnitureRole role = placed.Definition.roomFurnitureRole;

            if ((role & RoomFurnitureRole.Desk) != 0) desks++;
            if ((role & RoomFurnitureRole.Chair) != 0) seats++;
            // Wall-mounted boards are counted separately from floor furniture.
            if ((role & RoomFurnitureRole.Computer) != 0) computers++;
            if ((role & RoomFurnitureRole.Projector) != 0) projectors++;
            if ((role & RoomFurnitureRole.Book) != 0) books++;
            if ((role & RoomFurnitureRole.Printer) != 0) printers++;
        }

        wallBoards = roomPurposeController.CountWallBoardsOnBoundary(selectedRoom);

        bool hasDoor = roomPurposeController.HasDoor(selectedRoom);

        if (pendingPurpose == RoomPurpose.FlightSchool)
        {
            AddRequirement(
                $"At least {roomPurposeController.MinimumFoundationCells} foundation cells ({selectedRoom.CellCount}/{roomPurposeController.MinimumFoundationCells})",
                selectedRoom.CellCount >= roomPurposeController.MinimumFoundationCells);
            AddRequirement("Fully enclosed by walls or doors", true);
            AddRequirement("At least one door", hasDoor);
            AddRequirement("At least one chair", seats >= 1);
            AddRequirement("At least one wall board", wallBoards >= 1);

            AddOptionalRequirement("Computer (+5 first, +2 additional)", computers > 0);
            AddOptionalRequirement("Projector (+6 first, +2 additional)", projectors > 0);
            AddOptionalRequirement("Book (+2 each)", books > 0);
            AddOptionalRequirement("Printer (+4 first, +1 additional)", printers > 0);

            bool valid = roomPurposeController.ValidateFlightSchool(
                selectedRoom, out string validationMessage);

            validationLabel.text = valid
                ? "Valid: this room meets the requirements."
                : "Not valid yet:\n" + validationMessage;

            validationLabel.style.color = valid
                ? new Color(0.35f, 0.85f, 0.48f, 1f)
                : new Color(1f, 0.55f, 0.40f, 1f);
        }
        else if (pendingPurpose == RoomPurpose.CEOOffice ||
                 pendingPurpose == RoomPurpose.CFOOffice)
        {
            AddRequirement("Fully enclosed room", true);
            AddRequirement("At least one door", hasDoor);
            AddRequirement("One desk or office table", desks >= 1);
            AddRequirement("One chair", seats >= 1);
            AddRequirement("One computer", computers >= 1);
            AddRequirement("One printer", printers >= 1);

            bool valid = roomPurposeController.ValidateOffice(
                selectedRoom, out string validationMessage);

            validationLabel.text = valid
                ? "Functional: this office meets all requirements."
                : "Not functional yet:\n" + validationMessage;

            validationLabel.style.color = valid
                ? new Color(0.35f, 0.85f, 0.48f, 1f)
                : new Color(1f, 0.55f, 0.40f, 1f);
        }
        else if (pendingPurpose == RoomPurpose.PilotsLounge)
        {
            AddRequirement("Fully enclosed room", true);
            AddRequirement("At least one door", hasDoor);

            bool valid = roomPurposeController.ValidatePilotsLounge(
                selectedRoom, out string validationMessage);

            validationLabel.text = valid
                ? "Functional: this pilots lounge is active."
                : "Not functional yet:\n" + validationMessage;

            validationLabel.style.color = valid
                ? new Color(0.35f, 0.85f, 0.48f, 1f)
                : new Color(1f, 0.55f, 0.40f, 1f);
        }
        else
        {
            Label message = CreateLabel(
                $"No validation requirements have been configured for {pendingPurpose} yet.",
                13,
                false);
            message.style.color = secondaryTextColor;
            message.style.whiteSpace = WhiteSpace.Normal;
            requirementsLabel.Add(message);

            validationLabel.text = "Validation for this purpose is not implemented yet.";
            validationLabel.style.color = secondaryTextColor;
        }
    }

    private void UpdateAssessmentDisplay()
    {
        if (assessmentSection == null)
        {
            return;
        }

        bool shouldShow =
            selectedRoom != null &&
            pendingPurpose == RoomPurpose.FlightSchool;

        assessmentSection.style.display = shouldShow
            ? DisplayStyle.Flex
            : DisplayStyle.None;

        if (!shouldShow)
        {
            return;
        }

        if (flightSchoolAssessmentController == null)
        {
            assessmentLabel.text =
                "Assessment controller not found.\n\n" +
                "Add FlightSchoolAssessmentController to a scene " +
                "GameObject and ensure it references the room-purpose " +
                "system.";

            return;
        }

        if (selectedRoom.cells == null ||
            selectedRoom.cells.Count == 0)
        {
            assessmentLabel.text =
                "This room has no detected foundation cells.";

            return;
        }

        // Assessment records use the room's minimum grid X/Z anchor.
        int minX = int.MaxValue;
        int minZ = int.MaxValue;

        foreach (Vector2Int cell in selectedRoom.cells)
        {
            if (cell.x < minX)
            {
                minX = cell.x;
            }

            if (cell.y < minZ)
            {
                minZ = cell.y;
            }
        }

        Vector2Int roomAnchor = new Vector2Int(minX, minZ);

        if (!flightSchoolAssessmentController.TryGetAssessment(
                roomAnchor,
                out FlightSchoolRoomAssessment assessment))
        {
            assessmentLabel.text =
                "No assessment record is available for this room yet.\n\n" +
                "Assign the room as a Flight School and reopen this " +
                "panel after the assessment refreshes.";

            return;
        }

        string operationalStatus =
            assessment.meetsMinimumOperationalRequirements
                ? "OPERATIONAL"
                : "NOT OPERATIONAL";

        int baseStudentDemand =
            Mathf.RoundToInt(assessment.facilityQualityScore / 5f);

        int parkingStudentDemand =
            Mathf.Max(0, assessment.designatedTrainingParkingSpotCount) * 3;

        int dailyStudentDemand = Mathf.Min(
            baseStudentDemand + parkingStudentDemand,
            Mathf.Max(0, assessment.studentCapacity));

        assessmentLabel.text =
            $"FACILITY QUALITY\n" +
            $"{assessment.facilityQualityScore}/70\n\n" +

            $"Quality breakdown:\n" +
            $"• Room area: {assessment.areaQualityPoints}/25\n" +
            $"• Student seating: {assessment.seatingQualityPoints}/25\n" +
            $"• Optional equipment: {assessment.equipmentQualityPoints}/20\n\n" +

            $"OPTIONAL EQUIPMENT\n" +
            $"• Books: {assessment.bookCount}\n" +
            $"• Computers: {assessment.computerCount}\n" +
            $"• Printers: {assessment.printerCount}\n" +
            $"• Projectors: {assessment.projectorCount}\n\n" +

            $"FACILITY DETAILS\n" +
            $"• Foundation cells: {assessment.usableFoundationCells}\n" +
            $"• Student seats: {assessment.studentSeatCount}\n" +
            $"• Student capacity: {assessment.studentCapacity}\n" +
            $"• Wall boards: {assessment.wallBoardCount}\n\n" +

            $"OPERATIONAL STATUS\n" +
            $"{operationalStatus}\n" +
            $"{assessment.operationalRequirementMessage}\n\n" +

            $"DAILY STUDENT DEMAND\n" +
            $"{dailyStudentDemand} students\n" +
            $"• Room quality: {baseStudentDemand}\n" +
            $"• Reserved parking: +{parkingStudentDemand}\n" +
            $"• Limited by seats: {assessment.studentCapacity}\n\n" +

            $"SHARED AIRPORT TRAINING PARKING\n" +
            $"• Designated spots: " +
            $"{assessment.designatedTrainingParkingSpotCount}\n" +
            $"• Occupied: " +
            $"{assessment.occupiedTrainingParkingSpotCount}\n" +
            $"• Available: " +
            $"{assessment.availableTrainingParkingSpotCount}";
    }

    private void ApplyAssignment()
    {
        if (selectedRoom == null || roomPurposeController == null)
        {
            LogWarning(
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

        Vector3 center00 =
            gridManager.GridToWorldCenter(Vector2Int.zero);

        Vector3 center10 =
            gridManager.GridToWorldCenter(Vector2Int.right);

        Vector3 center01 =
            gridManager.GridToWorldCenter(Vector2Int.up);

        float cellWidth = Vector3.Distance(center00, center10);
        float cellDepth = Vector3.Distance(center00, center01);

        if (cellWidth <= 0.0001f || cellDepth <= 0.0001f)
        {
            LogWarning(
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
        Shader shader =
            Shader.Find("Universal Render Pipeline/Unlit");

        if (shader == null)
        {
            LogError(
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

    private void Log(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.Log(message, context); else Debug.Log(message); }
    private void LogWarning(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogWarning(message, context); else Debug.LogWarning(message); }
    private void LogError(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogError(message, context); else Debug.LogError(message); }
    private void LogException(System.Exception exception, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogException(exception, context); else Debug.LogException(exception); }
}
