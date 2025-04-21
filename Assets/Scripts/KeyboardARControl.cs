using UnityEngine;
using UnityEngine.InputSystem;

[ExecuteInEditMode]
public class KeyboardARControl : MonoBehaviour
{
    public bool enableKeyboardControl = false;
    
    // Поддержка обоих компонентов для обратной совместимости
    private ARManager arManager;
    private ARMLController armlController;
    private Keyboard keyboard;
    private bool wasSpacePressed = false;
    private bool wasRPressed = false;

    void Start()
    {
        // Если клавиатурное управление отключено, то не ищем компоненты
        if (!enableKeyboardControl)
            return;
            
        // Ищем компоненты в сцене
        FindControllers();
    }

    void Update()
    {
        // Если клавиатурное управление отключено, то не обрабатываем ввод
        if (!enableKeyboardControl)
            return;
            
        // Get reference to the keyboard
        if (keyboard == null)
        {
            keyboard = Keyboard.current;
            if (keyboard == null) return;
        }

        if (arManager == null && armlController == null)
        {
            FindControllers();
            return;
        }

        // Check for Space key (SCAN function)
        bool isSpacePressed = keyboard.spaceKey.isPressed;
        if (isSpacePressed && !wasSpacePressed)
        {
            Debug.Log("Starting AR scan via keyboard shortcut (Space)");
            StartAR();
        }
        wasSpacePressed = isSpacePressed;

        // Check for R key (RESET function)
        bool isRPressed = keyboard.rKey.isPressed;
        if (isRPressed && !wasRPressed)
        {
            Debug.Log("Resetting AR via keyboard shortcut (R)");
            StopAR();
        }
        wasRPressed = isRPressed;
    }

    private void StartAR()
    {
        // Пробуем использовать доступные методы
        if (armlController != null)
        {
            armlController.StartAR();
            return;
        }

        if (arManager != null)
        {
            // Используем метод из ARManager, если он есть
            // Проверяем через рефлексию, какие методы доступны
            var method = arManager.GetType().GetMethod("StartAR");
            if (method != null)
            {
                method.Invoke(arManager, null);
                return;
            }
            
            // Альтернативные названия методов, которые могут использоваться
            string[] alternativeMethodNames = {"StartARSession", "StartSession", "Start", "ScanAR", "Scan"};
            foreach (var methodName in alternativeMethodNames)
            {
                method = arManager.GetType().GetMethod(methodName);
                if (method != null)
                {
                    method.Invoke(arManager, null);
                    return;
                }
            }
            
            Debug.LogWarning("KeyboardARControl: No suitable start method found in ARManager");
        }
    }
    
    private void StopAR()
    {
        // Пробуем использовать доступные методы
        if (armlController != null)
        {
            armlController.StopAR();
            return;
        }

        if (arManager != null)
        {
            // Используем метод из ARManager, если он есть
            // Проверяем через рефлексию, какие методы доступны
            var method = arManager.GetType().GetMethod("StopAR");
            if (method != null)
            {
                method.Invoke(arManager, null);
                return;
            }
            
            // Альтернативные названия методов, которые могут использоваться
            string[] alternativeMethodNames = {"StopARSession", "StopSession", "Stop", "ResetAR", "Reset"};
            foreach (var methodName in alternativeMethodNames)
            {
                method = arManager.GetType().GetMethod(methodName);
                if (method != null)
                {
                    method.Invoke(arManager, null);
                    return;
                }
            }
            
            Debug.LogWarning("KeyboardARControl: No suitable stop method found in ARManager");
        }
    }

    private void FindControllers()
    {
        // Сначала ищем ARManager
        arManager = FindFirstObjectByType<ARManager>();
        
        // Затем ищем ARMLController, если ARManager не найден
        if (arManager == null)
        {
            armlController = FindFirstObjectByType<ARMLController>();
            
            if (armlController == null)
            {
                Debug.LogWarning("KeyboardARControl: Neither ARManager nor ARMLController found in scene. Keyboard controls won't work.");
            }
            else
            {
                Debug.Log("KeyboardARControl: Connected to ARMLController. Use SPACE to scan, R to reset.");
            }
        }
        else
        {
            Debug.Log("KeyboardARControl: Connected to ARManager. Use SPACE to scan, R to reset.");
        }
    }
} 