using UnityEngine;
using UnityEngine.InputSystem;

public class RobotOrbitCamera : MonoBehaviour
{
    public Vector3 focus;
    public float minDistance = 1.4f;
    public float maxDistance = 12f;
    Vector3 initialFocus;
    float yaw, pitch, distance, initialYaw, initialPitch, initialDistance;
    public static bool IsPointerOverControls(Vector2 screenPosition)
    {
        Vector2 p = new Vector2(screenPosition.x / Screen.width * 1280, (1 - screenPosition.y / Screen.height) * 720);
        return new Rect(954, 20, 302, 120).Contains(p);
    }
    void ResetView()
    {
        focus = initialFocus; yaw = initialYaw; pitch = initialPitch; distance = initialDistance;
    }
    void Awake()
    {
        initialFocus = focus;
        Vector3 offset = transform.position - focus;
        distance = initialDistance = offset.magnitude;
        yaw = initialYaw = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
        pitch = initialPitch = Mathf.Asin(offset.y / distance) * Mathf.Rad2Deg;
    }
    void LateUpdate()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;
        Vector2 delta = mouse.delta.ReadValue();
        if (mouse.rightButton.isPressed)
        {
            yaw += delta.x * .22f;
            pitch = Mathf.Clamp(pitch - delta.y * .22f, 5f, 80f);
        }
        if (mouse.middleButton.isPressed)
            focus += (-transform.right * delta.x - transform.up * delta.y) * distance * .0012f;
        float wheel = mouse.scroll.ReadValue().y;
        // Input System can report normalized notches or Windows 120-unit ticks.
        if (Mathf.Abs(wheel) > 10) wheel /= 120f;
        distance = Mathf.Clamp(distance * Mathf.Exp(-wheel * .18f), minDistance, maxDistance);
        if (Keyboard.current != null)
        {
            if (Keyboard.current.leftArrowKey.wasPressedThisFrame) yaw -= 15;
            if (Keyboard.current.rightArrowKey.wasPressedThisFrame) yaw += 15;
            if (Keyboard.current.upArrowKey.wasPressedThisFrame) pitch = Mathf.Clamp(pitch + 10, 5, 80);
            if (Keyboard.current.downArrowKey.wasPressedThisFrame) pitch = Mathf.Clamp(pitch - 10, 5, 80);
        }
        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
        {
            ResetView();
        }
        transform.position = focus + Quaternion.Euler(-pitch, yaw, 0) * Vector3.forward * distance;
        transform.LookAt(focus);
    }
    void OnGUI()
    {
        Matrix4x4 previous = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(Screen.width / 1280f, Screen.height / 720f, 1));
        GUI.Box(new Rect(954, 20, 302, 120), "Camera");
        var style = new GUIStyle(GUI.skin.button) { fontSize = 22 };
        if (GUI.Button(new Rect(966, 52, 62, 38), "<", style)) yaw -= 20;
        if (GUI.Button(new Rect(1038, 52, 62, 38), ">", style)) yaw += 20;
        if (GUI.Button(new Rect(1110, 52, 62, 38), "+", style)) distance = Mathf.Max(minDistance, distance * .8f);
        if (GUI.Button(new Rect(1182, 52, 62, 38), "-", style)) distance = Mathf.Min(maxDistance, distance * 1.25f);
        if (GUI.Button(new Rect(966, 96, 278, 34), "Reset view", style)) ResetView();
        GUI.matrix = previous;
    }
}
