using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HeadLoss.Unity.Interaction
{
    /// <summary>
    /// Input System action maps for the building view. "MouseKeyboard" is active; "Touch" is a stub for the
    /// later mobile port (<c>11-ui-visuals-audio.md</c> §Accessibility).
    /// </summary>
    public sealed class BuildingInput : IDisposable
    {
        public readonly InputActionMap MouseKeyboard = new InputActionMap("MouseKeyboard");
        public readonly InputActionMap Touch = new InputActionMap("Touch");

        public readonly InputAction Point, Primary, Secondary, Middle, Scroll, Pan, Cancel, Delete, DebugOverlay, Plain;
        public readonly InputAction Undo, Redo, Commit, Sheet, Crank, Rotate, RoomLayer;
        public readonly InputAction[] HeatmapKeys = new InputAction[7];

        public BuildingInput()
        {
            Point = MouseKeyboard.AddAction("Point", InputActionType.PassThrough, "<Pointer>/position");
            Primary = MouseKeyboard.AddAction("Primary", InputActionType.Button, "<Mouse>/leftButton");
            Secondary = MouseKeyboard.AddAction("Secondary", InputActionType.Button, "<Mouse>/rightButton");
            Middle = MouseKeyboard.AddAction("Middle", InputActionType.Button, "<Mouse>/middleButton");
            Scroll = MouseKeyboard.AddAction("Scroll", InputActionType.PassThrough, "<Mouse>/scroll");
            Pan = MouseKeyboard.AddAction("Pan", InputActionType.Value);
            Pan.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s").With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            Pan.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow").With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            Cancel = MouseKeyboard.AddAction("Cancel", InputActionType.Button, "<Keyboard>/escape");
            Delete = MouseKeyboard.AddAction("Delete", InputActionType.Button, "<Keyboard>/delete");
            Delete.AddBinding("<Keyboard>/backspace");
            DebugOverlay = MouseKeyboard.AddAction("DebugOverlay", InputActionType.Button, "<Keyboard>/f3");
            Undo = MouseKeyboard.AddAction("Undo", InputActionType.Button);
            Undo.AddCompositeBinding("ButtonWithOneModifier").With("Modifier", "<Keyboard>/ctrl").With("Button", "<Keyboard>/z");
            Redo = MouseKeyboard.AddAction("Redo", InputActionType.Button);
            Redo.AddCompositeBinding("ButtonWithOneModifier").With("Modifier", "<Keyboard>/ctrl").With("Button", "<Keyboard>/y");
            Commit = MouseKeyboard.AddAction("Commit", InputActionType.Button, "<Keyboard>/enter");
            Commit.AddBinding("<Keyboard>/numpadEnter");
            Sheet = MouseKeyboard.AddAction("RequirementSheet", InputActionType.Button, "<Keyboard>/tab");
            Crank = MouseKeyboard.AddAction("Crank", InputActionType.Button, "<Keyboard>/c");
            Rotate = MouseKeyboard.AddAction("Rotate", InputActionType.Button, "<Keyboard>/r");
            RoomLayer = MouseKeyboard.AddAction("RoomLayer", InputActionType.Button, "<Keyboard>/h");
            Plain = MouseKeyboard.AddAction("HeatmapPlain", InputActionType.Button, "<Keyboard>/0");
            Plain.AddBinding("<Keyboard>/backquote");
            Plain.AddBinding("<Keyboard>/numpad0");
            for (int i = 0; i < 7; i++)
            {
                HeatmapKeys[i] = MouseKeyboard.AddAction($"Heatmap{i + 1}", InputActionType.Button, $"<Keyboard>/{i + 1}");
                HeatmapKeys[i].AddBinding($"<Keyboard>/numpad{i + 1}");
            }
            // Touch: stubbed until the mobile pass (tap = primary, drag = pan, pinch = zoom).
            Touch.AddAction("Tap", InputActionType.Button, "<Touchscreen>/primaryTouch/tap");
            MouseKeyboard.Enable();
        }

        public Vector2 PointerPosition => Point.ReadValue<Vector2>();

        public void Dispose()
        {
            MouseKeyboard.Disable();
            MouseKeyboard.Dispose();
            Touch.Dispose();
        }
    }
}
