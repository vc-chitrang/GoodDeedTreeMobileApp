using UnityEngine;
using UnityEngine.UIElements;

namespace GoodDeedTreeMobileApp {
    /// <summary>
    /// Keeps the focused text field visible above the on-screen keyboard, like Android's
    /// adjustPan: while the keyboard is up, the given screen slides up just enough that the
    /// focused field sits a margin above the keyboard, and slides back down when it closes.
    /// </summary>
    public class KeyboardAvoider {
        private const float MarginPanelPx = 40f;             // gap between field and keyboard
        private const float FallbackKeyboardFraction = 0.42f; // used if the OS reports no keyboard area
        private const float SmoothSeconds = 0.12f;

        private readonly VisualElement root;
        private readonly VisualElement screen;
        private float offset;          // current upward shift, panel pixels
        private float velocity;

        public KeyboardAvoider(VisualElement panelRoot, VisualElement screenToShift) {
            root = panelRoot;
            screen = screenToShift;
        }

        public void Tick() {
            if (screen == null || root.panel == null) {
                return;
            }

            float target = 0f;
            float panelHeight = root.layout.height;
            TextField field = FocusedField();
            if (field != null && TouchScreenKeyboard.visible && panelHeight > 0f && Screen.height > 0) {
                float keyboardPx = TouchScreenKeyboard.area.height;
                if (keyboardPx <= 0f) {
                    keyboardPx = Screen.height * FallbackKeyboardFraction;
                }
                float keyboardPanel = keyboardPx * (panelHeight / Screen.height);
                // worldBound already includes the current shift; remove it to get the rest position.
                float fieldBottom = field.worldBound.yMax + offset;
                float visibleBottom = panelHeight - keyboardPanel - MarginPanelPx;
                target = Mathf.Max(0f, fieldBottom - visibleBottom);
            }

            offset = Mathf.SmoothDamp(offset, target, ref velocity, SmoothSeconds, Mathf.Infinity, Time.unscaledDeltaTime);
            if (Mathf.Abs(offset - target) < 0.5f) {
                offset = target;
            }
            screen.style.translate = new Translate(0f, -offset);
        }

        /// <summary>Snap back immediately, e.g. when leaving the input screen.</summary>
        public void Reset() {
            offset = 0f;
            velocity = 0f;
            if (screen != null) {
                screen.style.translate = new Translate(0f, 0f);
            }
        }

        private TextField FocusedField() {
            VisualElement focused = root.panel.focusController.focusedElement as VisualElement;
            while (focused != null) {
                if (focused is TextField textField) {
                    return textField;
                }
                focused = focused.parent;
            }
            return null;
        }
    }
}
