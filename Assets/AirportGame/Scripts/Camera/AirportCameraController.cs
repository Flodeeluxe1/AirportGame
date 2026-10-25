
using UnityEngine;
using UnityEngine.InputSystem;

public class AirportCameraController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera controlledCamera;

    [Header("Movement")]
    [SerializeField] private float minMoveSpeed = 5f;
    [SerializeField] private float maxMoveSpeed = 250f;
    [SerializeField] private float minAcceleration = 15f;
    [SerializeField] private float maxAcceleration = 600f;
    [SerializeField] private float minDeceleration = 15f;
    [SerializeField] private float maxDeceleration = 600f;
    [SerializeField] private float rightMouseDragSensitivity = 0.05f;
    [SerializeField] private float shiftMoveSpeedMultiplier = 2f;

    [Header("Edge Scrolling")]
    [SerializeField] private bool enableEdgeScrolling = true;
    [SerializeField] private float edgeScrollBorder = 20f;
    [SerializeField] private float edgeScrollSpeedMultiplier = 1f;

    [Header("Rotation")]
    [SerializeField] private float rotationSpeed = 60f;
    [SerializeField] private float mouseRotationSensitivity = 0.2f;

    [Header("Camera Tilt")]
    [SerializeField] private float minTiltAngle = 25f;
    [SerializeField] private float maxTiltAngle = 75f;
    [SerializeField] private float tiltSensitivity = 0.2f;

    [Header("Zoom")]
    [SerializeField] private float zoomSpeed = 600f;
    [SerializeField] private float minZoom = 5f;
    [SerializeField] private float maxZoom = 600f;
    [SerializeField] private float zoomSmoothness = 20f;

    [Header("Reset Camera")]
    [SerializeField] private float doubleClickInterval = 0.3f;

    private Vector3 currentVelocity;
    private Vector3 horizontalDirection;

    private float targetZoom;
    private float currentZoom;
    private float currentTiltAngle;

    private Vector3 initialRigPosition;
    private Quaternion initialRigRotation;
    private Vector3 initialHorizontalDirection;
    private float initialZoom;
    private float initialTiltAngle;

    private float lastMiddleClickTime = -1f;

    private void Awake()
    {
        if (controlledCamera == null)
        {
            controlledCamera = GetComponentInChildren<Camera>();
        }

        if (controlledCamera == null)
        {
            Debug.LogError(
                "AirportCameraController: No child Camera found.",
                this
            );

            enabled = false;
            return;
        }

        Vector3 localPosition =
            controlledCamera.transform.localPosition;

        horizontalDirection = new Vector3(
            localPosition.x,
            0f,
            localPosition.z
        );

        currentZoom = horizontalDirection.magnitude;

        if (currentZoom < 0.01f)
        {
            Debug.LogError(
                "AirportCameraController: The camera needs a " +
                "non-zero horizontal offset from the CameraRig.",
                this
            );

            enabled = false;
            return;
        }

        horizontalDirection.Normalize();

        currentZoom = Mathf.Clamp(
            currentZoom,
            minZoom,
            maxZoom
        );

        targetZoom = currentZoom;

        currentTiltAngle = Mathf.Atan2(
            localPosition.y,
            currentZoom
        ) * Mathf.Rad2Deg;

        currentTiltAngle = Mathf.Clamp(
            currentTiltAngle,
            minTiltAngle,
            maxTiltAngle
        );

        // Store the initial camera view for double-click reset.
        initialRigPosition = transform.position;
        initialRigRotation = transform.rotation;
        initialHorizontalDirection = horizontalDirection;
        initialZoom = currentZoom;
        initialTiltAngle = currentTiltAngle;

        UpdateCameraPosition();
    }

    private void Update()
    {
        Mouse mouse = Mouse.current;

        // Reset first and skip this frame's controls if a
        // middle-mouse double-click was detected.
        if (mouse != null && HandleMiddleMouseDoubleClick(mouse))
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;

        if (keyboard != null)
        {
            HandleMovement(keyboard);
            HandleKeyboardRotation(keyboard);
        }

        if (mouse != null)
        {
            HandleMouseRotationAndTilt(mouse);
            HandleZoom(mouse);
        }

        UpdateCameraPosition();
    }

    private bool HandleMiddleMouseDoubleClick(Mouse mouse)
    {
        if (!mouse.middleButton.wasPressedThisFrame)
        {
            return false;
        }

        float currentTime = Time.unscaledTime;

        if (lastMiddleClickTime >= 0f &&
            currentTime - lastMiddleClickTime <= doubleClickInterval)
        {
            lastMiddleClickTime = -1f;
            ResetCameraView();
            return true;
        }

        lastMiddleClickTime = currentTime;
        return false;
    }


    private void ResetCameraView()
    {
        // Keep the CameraRig at its current world position.
        // Reset only its rotation and the camera's viewing settings.
        transform.rotation = initialRigRotation;

        horizontalDirection = initialHorizontalDirection;
        currentZoom = initialZoom;
        targetZoom = initialZoom;
        currentTiltAngle = initialTiltAngle;

        // Prevent leftover movement from carrying the camera away.
        currentVelocity = Vector3.zero;

        UpdateCameraPosition();
    }



    private void HandleMovement(Keyboard keyboard)
    {
        Vector2 input = Vector2.zero;

        // WASD movement.
        if (keyboard.wKey.isPressed)
            input.y += 1f;

        if (keyboard.sKey.isPressed)
            input.y -= 1f;

        if (keyboard.dKey.isPressed)
            input.x += 1f;

        if (keyboard.aKey.isPressed)
            input.x -= 1f;


        // Right mouse drag: direct movement with no acceleration.
        Mouse mouse = Mouse.current;

        if (mouse != null && mouse.rightButton.isPressed)
        {
            Vector2 mouseDelta = mouse.delta.ReadValue();

            // Reverse the input so the camera follows the drag direction.
            Vector3 dragForward = controlledCamera.transform.forward;
            Vector3 dragRight = controlledCamera.transform.right;

            dragForward.y = 0f;
            dragRight.y = 0f;

            dragForward.Normalize();
            dragRight.Normalize();

            Vector3 dragMovement =
                dragRight * -mouseDelta.x +
                dragForward * -mouseDelta.y;

            transform.position +=
                dragMovement * rightMouseDragSensitivity;
        }


        // Edge scrolling.
        bool usingEdgeScroll = false;

        if (enableEdgeScrolling && mouse != null)
        {
            Vector2 mousePosition = mouse.position.ReadValue();

            if (mousePosition.x <= edgeScrollBorder)
            {
                input.x -= 1f;
                usingEdgeScroll = true;
            }

            if (mousePosition.x >= Screen.width - edgeScrollBorder)
            {
                input.x += 1f;
                usingEdgeScroll = true;
            }

            if (mousePosition.y <= edgeScrollBorder)
            {
                input.y -= 1f;
                usingEdgeScroll = true;
            }

            if (mousePosition.y >= Screen.height - edgeScrollBorder)
            {
                input.y += 1f;
                usingEdgeScroll = true;
            }
        }

        input = Vector2.ClampMagnitude(input, 1f);

        float zoomProgress = Mathf.InverseLerp(
            minZoom,
            maxZoom,
            currentZoom
        );

        float currentMoveSpeed = Mathf.Lerp(
            minMoveSpeed,
            maxMoveSpeed,
            zoomProgress
        );
        if (keyboard.leftShiftKey.isPressed ||
            keyboard.rightShiftKey.isPressed)
                {
                    currentMoveSpeed *= shiftMoveSpeedMultiplier;
                }

        float currentAcceleration = Mathf.Lerp(
            minAcceleration,
            maxAcceleration,
            zoomProgress
        );

        float currentDeceleration = Mathf.Lerp(
            minDeceleration,
            maxDeceleration,
            zoomProgress
        );

        Vector3 forward = controlledCamera.transform.forward;
        Vector3 right = controlledCamera.transform.right;

        forward.y = 0f;
        right.y = 0f;

        forward.Normalize();
        right.Normalize();

        Vector3 desiredDirection =
            forward * input.y +
            right * input.x;

        float speedMultiplier =
            usingEdgeScroll ? edgeScrollSpeedMultiplier : 1f;

        Vector3 targetVelocity =
            desiredDirection * currentMoveSpeed * speedMultiplier;

        float speedChange =
            input.sqrMagnitude > 0f
                ? currentAcceleration
                : currentDeceleration;

        currentVelocity = Vector3.MoveTowards(
            currentVelocity,
            targetVelocity,
            speedChange * Time.unscaledDeltaTime
        );

        transform.position +=
            currentVelocity * Time.unscaledDeltaTime;
    }


    private void HandleKeyboardRotation(Keyboard keyboard)
    {
        float rotationInput = 0f;

        if (keyboard.qKey.isPressed)
            rotationInput -= 1f;

        if (keyboard.eKey.isPressed)
            rotationInput += 1f;

        float rotation =
            rotationInput * rotationSpeed * Time.unscaledDeltaTime;

        transform.Rotate(
            0f,
            rotation,
            0f,
            Space.World
        );
    }

    private void HandleMouseRotationAndTilt(Mouse mouse)
    {
        if (!mouse.middleButton.isPressed)
            return;

        Vector2 mouseDelta = mouse.delta.ReadValue();

        float horizontalRotation =
            mouseDelta.x * mouseRotationSensitivity;

        transform.Rotate(
            0f,
            horizontalRotation,
            0f,
            Space.World
        );

        currentTiltAngle -=
            mouseDelta.y * tiltSensitivity;

        currentTiltAngle = Mathf.Clamp(
            currentTiltAngle,
            minTiltAngle,
            maxTiltAngle
        );
    }

    private void HandleZoom(Mouse mouse)
    {
        float scroll = mouse.scroll.ReadValue().y;

        if (Mathf.Abs(scroll) > 0.01f)
        {
            targetZoom -=
                scroll * zoomSpeed * 0.01f;

            targetZoom = Mathf.Clamp(
                targetZoom,
                minZoom,
                maxZoom
            );
        }

        currentZoom = Mathf.Lerp(
            currentZoom,
            targetZoom,
            zoomSmoothness * Time.unscaledDeltaTime
        );
    }

    private void UpdateCameraPosition()
    {
        float height = Mathf.Tan(
            currentTiltAngle * Mathf.Deg2Rad
        ) * currentZoom;

        controlledCamera.transform.localPosition =
            horizontalDirection * currentZoom +
            Vector3.up * height;

        controlledCamera.transform.LookAt(
            transform.position,
            Vector3.up
        );
    }
}
