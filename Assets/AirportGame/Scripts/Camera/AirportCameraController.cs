using UnityEngine;
using UnityEngine.InputSystem;

public class AirportCameraController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 15f;
    [SerializeField] private float acceleration = 40f;
    [SerializeField] private float deceleration = 50f;

    [Header("Rotation")]
    [SerializeField] private float rotationSpeed = 60f;

    [Header("Zoom")]
    [SerializeField] private float zoomSpeed = 15f;
    [SerializeField] private float minZoom = 5f;
    [SerializeField] private float maxZoom = 40f;
    [SerializeField] private float zoomSmoothness = 10f;

    private Vector3 currentVelocity;

    private float targetZoom;
    private float currentZoom;

    private void Awake()
    {
        currentZoom =
            transform.position.y;

        targetZoom =
            currentZoom;
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;

        if (keyboard == null || mouse == null)
            return;

        HandleMovement(keyboard);
        HandleRotation(keyboard);
        HandleZoom(mouse);
    }

    private void HandleMovement(Keyboard keyboard)
    {
        Vector2 input = Vector2.zero;

        if (keyboard.wKey.isPressed)
            input.y += 1f;

        if (keyboard.sKey.isPressed)
            input.y -= 1f;

        if (keyboard.dKey.isPressed)
            input.x += 1f;

        if (keyboard.aKey.isPressed)
            input.x -= 1f;

        input =
            Vector2.ClampMagnitude(
                input,
                1f
            );

        Vector3 forward =
            transform.forward;

        Vector3 right =
            transform.right;

        // Keep movement horizontal.
        forward.y = 0f;
        right.y = 0f;

        forward.Normalize();
        right.Normalize();

        Vector3 desiredDirection =
            forward * input.y +
            right * input.x;

        Vector3 targetVelocity =
            desiredDirection *
            moveSpeed;

        float speedChange;

        if (input.sqrMagnitude > 0f)
        {
            speedChange =
                acceleration;
        }
        else
        {
            speedChange =
                deceleration;
        }

        currentVelocity =
            Vector3.MoveTowards(
                currentVelocity,
                targetVelocity,
                speedChange *
                Time.deltaTime
            );

        transform.position +=
            currentVelocity *
            Time.deltaTime;
    }

    private void HandleRotation(Keyboard keyboard)
    {
        float rotationInput = 0f;

        if (keyboard.qKey.isPressed)
            rotationInput -= 1f;

        if (keyboard.eKey.isPressed)
            rotationInput += 1f;

        float rotation =
            rotationInput *
            rotationSpeed *
            Time.deltaTime;

        transform.Rotate(
            0f,
            rotation,
            0f,
            Space.World
        );
    }

    private void HandleZoom(Mouse mouse)
    {
        float scroll =
            mouse.scroll.ReadValue().y;

        if (Mathf.Abs(scroll) > 0.01f)
        {
            targetZoom -=
                scroll *
                zoomSpeed *
                0.01f;

            targetZoom =
                Mathf.Clamp(
                    targetZoom,
                    minZoom,
                    maxZoom
                );
        }

        currentZoom =
            Mathf.Lerp(
                currentZoom,
                targetZoom,
                zoomSmoothness *
                Time.deltaTime
            );

        Vector3 position =
            transform.position;

        position.y =
            currentZoom;

        transform.position =
            position;
    }
}