using UnityEngine;

/// <summary>
/// Input handling supporting both Legacy Input and Input System package.
/// Uses #if ENABLE_INPUT_SYSTEM to switch between modes at compile time.
/// </summary>
public static class GameInput
{
    /// <summary>WebGL/タッチでも開始できるよう、クリック・タップも開始入力として扱う</summary>
    private static bool IsConfirmPointerPressed()
    {
#if ENABLE_INPUT_SYSTEM
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            return true;
        }

        var touch = UnityEngine.InputSystem.Touchscreen.current;
        if (touch != null && touch.primaryTouch.press.wasPressedThisFrame)
        {
            return true;
        }

        return false;
#else
        return Input.GetMouseButtonDown(0) || Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began;
#endif
    }

    public static bool IsStartPressed()
    {
        if (IsConfirmPointerPressed())
        {
            return true;
        }

#if ENABLE_INPUT_SYSTEM
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard == null)
        {
            return false;
        }

        return keyboard.spaceKey.wasPressedThisFrame
            || keyboard.enterKey.wasPressedThisFrame
            || keyboard.numpadEnterKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return);
#endif
    }

    public static bool IsRetryPressed()
    {
        if (IsConfirmPointerPressed())
        {
            return true;
        }

#if ENABLE_INPUT_SYSTEM
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard == null)
        {
            return false;
        }

        return keyboard.rKey.wasPressedThisFrame
            || keyboard.spaceKey.wasPressedThisFrame
            || keyboard.enterKey.wasPressedThisFrame
            || keyboard.numpadEnterKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return);
#endif
    }

    public static Vector3 GetMoveVector()
    {
#if ENABLE_INPUT_SYSTEM
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard == null)
        {
            return Vector3.zero;
        }

        var x = 0f;
        var z = 0f;

        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
        {
            x -= 1f;
        }

        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
        {
            x += 1f;
        }

        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
        {
            z -= 1f;
        }

        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
        {
            z += 1f;
        }

        return Vector3.ClampMagnitude(new Vector3(x, 0f, z), 1f);
#else
        // Legacy Input: use "Horizontal" and "Vertical" axes from Input Manager
        var input = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
        return Vector3.ClampMagnitude(input, 1f);
#endif
    }
}
