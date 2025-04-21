using UnityEngine;
using UnityEngine.InputSystem;

[ExecuteInEditMode]
public class EditorCameraController : MonoBehaviour
{
    public float moveSpeed = 5.0f;
    public float rotateSpeed = 120.0f;

    // References to the keyboard and required actions
    private Keyboard keyboard;

    void OnEnable()
    {
        // Get reference to the keyboard
        keyboard = Keyboard.current;
    }

    void Update()
    {
        // Only run in editor and not in play mode
        if (!Application.isEditor || Application.isPlaying)
            return;

        // Make sure we have keyboard input
        if (keyboard == null)
        {
            keyboard = Keyboard.current;
            if (keyboard == null) return;
        }

        // Movement
        Vector3 moveDirection = Vector3.zero;
        
        if (keyboard.wKey.isPressed)
            moveDirection += Vector3.forward;
        if (keyboard.sKey.isPressed)
            moveDirection += Vector3.back;
        if (keyboard.aKey.isPressed)
            moveDirection += Vector3.left;
        if (keyboard.dKey.isPressed)
            moveDirection += Vector3.right;
        if (keyboard.qKey.isPressed)
            moveDirection += Vector3.down;
        if (keyboard.eKey.isPressed)
            moveDirection += Vector3.up;

        transform.Translate(moveDirection.normalized * moveSpeed * Time.deltaTime);

        // Rotation
        if (keyboard.leftArrowKey.isPressed)
            transform.Rotate(Vector3.up, -rotateSpeed * Time.deltaTime, Space.World);
        if (keyboard.rightArrowKey.isPressed)
            transform.Rotate(Vector3.up, rotateSpeed * Time.deltaTime, Space.World);
        if (keyboard.upArrowKey.isPressed)
            transform.Rotate(Vector3.right, -rotateSpeed * Time.deltaTime);
        if (keyboard.downArrowKey.isPressed)
            transform.Rotate(Vector3.right, rotateSpeed * Time.deltaTime);
    }
} 