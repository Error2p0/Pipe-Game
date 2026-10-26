using HeadLoss.Core.Data.Defs;
using UnityEngine;

namespace HeadLoss.Unity.View
{
    /// <summary>Orthographic top-down camera: pan (right/middle drag, WASD/arrows) and zoom (wheel, toward the cursor).</summary>
    public sealed class CameraController : MonoBehaviour
    {
        public Camera Camera { get; private set; } = null!;
        CameraDef _def = new CameraDef();
        Vector3 _dragOriginWorld;
        bool _dragging;

        public void Init(CameraDef def, Color background)
        {
            _def = def;
            Camera = gameObject.AddComponent<Camera>();
            Camera.orthographic = true;
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = background;
            Camera.nearClipPlane = 0.1f;
            Camera.farClipPlane = 50f;
            Camera.transparencySortMode = TransparencySortMode.Orthographic;
            transform.position = new Vector3(0, 0, -10);
            gameObject.tag = "MainCamera";
        }

        /// <summary>Frames a rectangle (world metres) with a margin.</summary>
        public void Frame(Rect r, float margin)
        {
            transform.position = new Vector3(r.center.x, r.center.y, -10);
            float aspect = Camera.aspect > 0 ? Camera.aspect : 16f / 9f;
            float size = Mathf.Max(r.height * 0.5f, r.width * 0.5f / aspect) + margin;
            Camera.orthographicSize = Mathf.Clamp(size, (float)_def.ZoomMin, (float)_def.ZoomMax);
        }

        public Vector2 ScreenToWorld(Vector2 screen)
        {
            Vector3 w = Camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 10));
            return new Vector2(w.x, w.y);
        }

        /// <param name="notches">Wheel notches (positive = zoom in).</param>
        public void Zoom(float notches, Vector2 screenPivot)
        {
            if (Mathf.Approximately(notches, 0)) return;
            Vector2 before = ScreenToWorld(screenPivot);
            float size = Camera.orthographicSize / Mathf.Pow((float)_def.ZoomStep, notches);
            Camera.orthographicSize = Mathf.Clamp(size, (float)_def.ZoomMin, (float)_def.ZoomMax);
            Vector2 after = ScreenToWorld(screenPivot);
            transform.position += (Vector3)(before - after);
        }

        /// <param name="direction">Keyboard pan direction (−1…1 per axis).</param>
        public void KeyPan(Vector2 direction, float dt)
        {
            if (direction.sqrMagnitude < 1e-4f) return;
            transform.position += (Vector3)(direction * (float)(_def.KeyPanSpeed * Camera.orthographicSize * dt));
        }

        public void BeginDrag(Vector2 screen)
        {
            _dragging = true;
            _dragOriginWorld = ScreenToWorld(screen);
        }

        public void UpdateDrag(Vector2 screen)
        {
            if (!_dragging) return;
            Vector2 now = ScreenToWorld(screen);
            transform.position += (Vector3)((Vector2)_dragOriginWorld - now);
        }

        public void EndDrag() => _dragging = false;
        public bool Dragging => _dragging;
    }
}
