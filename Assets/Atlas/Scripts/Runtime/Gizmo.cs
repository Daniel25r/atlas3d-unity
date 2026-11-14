// ATLAS 3D (Unity) - move / rotate / scale manipulator, the same as the
// Godot version:
//   Mover    arrows with cone tips (X red, Y green, Z blue) and a ball in
//            the middle that moves freely in the view's plane.
//   Girar    three rings and a ball in the middle that turns the object
//            towards wherever the finger slides («trackball»).
//   Escalar  arms with cube tips and a cube in the middle (uniform).
// Drawn over the scene at a constant size on screen; the handles are found
// in screen space with a generous radius, so thin ones are easy to grab
// with a finger.
using System.Collections.Generic;
using UnityEngine;

namespace Atlas
{
    public enum Tool { Select, Move, Rotate, Scale }

    public class Gizmo : MonoBehaviour
    {
        public const int Layer = 2;   // never picked as a scene object
        const int Center = 3, None = -1;
        const float BaseSizePx = 105f;   // arm length on screen (logical px)
        const float HitRadiusPx = 22f;

        public Camera cam;
        public Material unlitTemplate;
        public Tool tool = Tool.Move;
        public Transform target;
        public float pxScale = 1f;   // logical -> screen pixels (canvas scale)

        Transform rootMove, rootRotate, rootScale;
        readonly Material[] mats = new Material[4];
        static readonly Color[] AxisCol = { AtlasTheme.AxisX, AtlasTheme.AxisY, AtlasTheme.AxisZ, new Color(0.92f, 0.93f, 0.95f) };

        // Drag state
        int active = None;
        Vector2 s0;
        Vector3 startPos, startScale;
        Quaternion startRot;

        public bool Dragging { get { return active != None; } }

        public void Build()
        {
            var tmpl = Resources.Load<Material>("AtlasOverlay");
            bool overlay = tmpl != null && tmpl.shader != null && tmpl.shader.isSupported;
            for (int h = 0; h < 4; h++)
            {
                mats[h] = new Material(overlay ? tmpl : unlitTemplate);
                if (!overlay)
                {
                    MatUtil.SetTransparent(mats[h], true);
                    mats[h].renderQueue = 4000;
                }
            }
            rootMove = new GameObject("Mover").transform;
            rootRotate = new GameObject("Girar").transform;
            rootScale = new GameObject("Escalar").transform;
            foreach (var r in new[] { rootMove, rootRotate, rootScale }) r.SetParent(transform, false);

            Mesh cyl = PrimMesh(PrimitiveType.Cylinder), box = PrimMesh(PrimitiveType.Cube), ball = PrimMesh(PrimitiveType.Sphere);
            Mesh cone = Primitives.Get("cone");
            Mesh ring = Primitives.Ring(0.95f, 0.022f);
            for (int i = 0; i < 3; i++)
            {
                // Unity's cylinder is 2 high and 1 wide; the cone is 1 x 1.
                Part(rootMove, i, cyl, AxisRot(i), AxisRot(i) * new Vector3(0f, 0.49f, 0f), new Vector3(0.036f, 0.39f, 0.036f));
                Part(rootMove, i, cone, AxisRot(i), AxisRot(i) * new Vector3(0f, 0.9f, 0f), new Vector3(0.13f, 0.2f, 0.13f));
                Part(rootScale, i, cyl, AxisRot(i), AxisRot(i) * new Vector3(0f, 0.49f, 0f), new Vector3(0.036f, 0.39f, 0.036f));
                Part(rootScale, i, box, AxisRot(i), AxisRot(i) * new Vector3(0f, 0.9f, 0f), Vector3.one * 0.11f);
                Part(rootRotate, i, ring, AxisRot(i), Vector3.zero, Vector3.one);
            }
            Part(rootMove, Center, ball, Quaternion.identity, Vector3.zero, Vector3.one * 0.16f);
            Part(rootRotate, Center, ball, Quaternion.identity, Vector3.zero, Vector3.one * 0.22f);
            Part(rootScale, Center, box, Quaternion.identity, Vector3.zero, Vector3.one * 0.14f);
            SetTool(tool);
            Colors();
            gameObject.SetActive(false);
        }

        static Mesh PrimMesh(PrimitiveType t)
        {
            var tmp = GameObject.CreatePrimitive(t);
            var m = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
            return m;
        }

        /// Rotation that puts +Y on axis i.
        static Quaternion AxisRot(int i)
        {
            if (i == 0) return Quaternion.Euler(0f, 0f, -90f);
            if (i == 2) return Quaternion.Euler(90f, 0f, 0f);
            return Quaternion.identity;
        }

        void Part(Transform parent, int handle, Mesh mesh, Quaternion rot, Vector3 pos, Vector3 scale)
        {
            var go = new GameObject("Asa" + handle);
            go.layer = Layer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mats[handle];
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        public void SetTool(Tool t)
        {
            tool = t;
            if (rootMove == null) return;
            rootMove.gameObject.SetActive(t == Tool.Move);
            rootRotate.gameObject.SetActive(t == Tool.Rotate);
            rootScale.gameObject.SetActive(t == Tool.Scale);
        }

        void Colors()
        {
            for (int h = 0; h < 4; h++)
            {
                Color c = AxisCol[h];
                if (h == active) c = new Color(1f, 0.92f, 0.55f);
                else if (active != None) c.a = 0.25f;   // the others fade while dragging
                mats[h].SetColor("_BaseColor", c);
            }
        }

        public void Show(Transform t)
        {
            target = t;
            gameObject.SetActive(t != null);
            if (t != null) LateUpdate();
        }

        void LateUpdate()
        {
            if (target == null || cam == null)
            {
                if (gameObject.activeSelf) gameObject.SetActive(false);
                return;
            }
            transform.position = target.position;
            // Moving uses the world axes; turning and stretching the object's own.
            transform.rotation = tool == Tool.Move ? Quaternion.identity : target.rotation;
            float d = Mathf.Max(0.001f, Vector3.Dot(target.position - cam.transform.position, cam.transform.forward));
            float worldH = 2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float s = worldH * BaseSizePx * pxScale / Mathf.Max(1f, Screen.height);
            transform.localScale = Vector3.one * s;
        }

        Vector3 AxisDir(int i)
        {
            Vector3 v = i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;
            return (transform.rotation * v).normalized;
        }

        float Arm { get { return transform.localScale.x; } }

        Vector2 Screen2(Vector3 world)
        {
            Vector3 p = cam.WorldToScreenPoint(world);
            return new Vector2(p.x, p.y);
        }

        // ------------------------------------------------------------------
        // Hit testing (screen space)

        int HitTest(Vector2 screen)
        {
            Vector3 o = transform.position;
            if (cam.WorldToScreenPoint(o).z <= 0f) return None;
            float radius = HitRadiusPx * pxScale;
            Vector2 o2 = Screen2(o);
            float arm = Arm;
            int best = None;
            float bestD = radius;
            if (tool == Tool.Rotate)
            {
                if ((screen - o2).magnitude < radius * 1.3f) return Center;
                for (int i = 0; i < 3; i++)
                {
                    float d = DistToRing(screen, o, AxisDir(i), arm * 0.95f);
                    if (d < bestD) { bestD = d; best = i; }
                }
                return best;
            }
            if ((screen - o2).magnitude < radius * 1.1f) return Center;
            for (int i = 0; i < 3; i++)
            {
                Vector2 tip = Screen2(o + AxisDir(i) * arm);
                float d = DistToSegment(screen, Vector2.Lerp(o2, tip, 0.2f), tip);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        float DistToRing(Vector2 screen, Vector3 c, Vector3 n, float r)
        {
            Vector3 u = Vector3.Cross(n, Mathf.Abs(Vector3.Dot(n, Vector3.up)) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 v = Vector3.Cross(n, u).normalized;
            float best = float.MaxValue;
            Vector2 prev = Vector2.zero;
            const int steps = 48;
            for (int k = 0; k <= steps; k++)
            {
                float a = Mathf.PI * 2f * k / steps;
                Vector2 s = Screen2(c + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * r);
                if (k > 0) best = Mathf.Min(best, DistToSegment(screen, prev, s));
                prev = s;
            }
            return best;
        }

        static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return (p - (a + ab * t)).magnitude;
        }

        // ------------------------------------------------------------------
        // Dragging

        /// Starts a drag if the finger is on a handle.
        public bool TryBegin(Vector2 screen)
        {
            if (target == null || !gameObject.activeInHierarchy || tool == Tool.Select) return false;
            LateUpdate();
            int h = HitTest(screen);
            if (h == None) return false;
            active = h;
            s0 = screen;
            startPos = target.position;
            startRot = target.rotation;
            startScale = target.localScale;
            Colors();
            return true;
        }

        public void Drag(Vector2 s1)
        {
            if (active == None || target == null) return;
            if (tool == Tool.Move)
            {
                if (active < 3) target.position = startPos + AxisTranslation(active, s0, s1);
                else target.position = startPos + PlaneTranslation(s0, s1);
            }
            else if (tool == Tool.Rotate)
            {
                if (active < 3)
                {
                    Vector3 axis = startRot * (active == 0 ? Vector3.right : active == 1 ? Vector3.up : Vector3.forward);
                    target.rotation = Quaternion.AngleAxis(AxisRotation(axis, s0, s1), axis) * startRot;
                }
                else target.rotation = FreeRotation(s0, s1) * startRot;
            }
            else
            {
                float f = Mathf.Max(0.01f, ScaleFactor(active, s0, s1));
                if (active < 3)
                {
                    Vector3 s = startScale;
                    s[active] = startScale[active] * f;
                    target.localScale = s;
                }
                else target.localScale = startScale * f;
            }
        }

        public void End()
        {
            active = None;
            Colors();
        }

        Vector3 AxisTranslation(int i, Vector2 a, Vector2 b)
        {
            // Axis through the start position (the gizmo follows the object).
            Vector3 d = i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;
            float t0 = RayAxisParam(startPos, d, a), t1 = RayAxisParam(startPos, d, b);
            if (float.IsNaN(t0) || float.IsNaN(t1)) return Vector3.zero;
            return d * (t1 - t0);
        }

        Vector3 PlaneTranslation(Vector2 a, Vector2 b)
        {
            var plane = new Plane(cam.transform.forward, startPos);
            Ray r0 = cam.ScreenPointToRay(a), r1 = cam.ScreenPointToRay(b);
            float e0, e1;
            if (!plane.Raycast(r0, out e0) || !plane.Raycast(r1, out e1)) return Vector3.zero;
            return r1.GetPoint(e1) - r0.GetPoint(e0);
        }

        /// Degrees around `axis` from the finger's angle around the centre;
        /// the object turns the way the finger goes, seen from the camera.
        float AxisRotation(Vector3 axis, Vector2 a, Vector2 b)
        {
            Vector2 c = Screen2(startPos);
            Vector2 v0 = a - c, v1 = b - c;
            if (v0.magnitude < 2f || v1.magnitude < 2f) return 0f;
            float ccw = Vector2.SignedAngle(v0, v1);
            bool towardCamera = Vector3.Dot(axis, cam.transform.forward) < 0f;
            return towardCamera ? -ccw : ccw;
        }

        /// «Trackball»: sliding right turns around the view's up axis,
        /// sliding up around its right axis.
        Quaternion FreeRotation(Vector2 a, Vector2 b)
        {
            Vector2 d = b - a;
            if (d.magnitude < 0.5f) return Quaternion.identity;
            Vector3 axis = (-cam.transform.up * d.x + cam.transform.right * d.y).normalized;
            float angle = d.magnitude / Mathf.Max(1f, 180f * pxScale) * 90f;
            return Quaternion.AngleAxis(angle, axis);
        }

        float ScaleFactor(int h, Vector2 a, Vector2 b)
        {
            Vector2 c = Screen2(startPos);
            if (h == Center)
            {
                float d0 = (a - c).magnitude;
                return d0 > 2f ? (b - c).magnitude / d0 : 1f;
            }
            Vector3 axis = startRot * (h == 0 ? Vector3.right : h == 1 ? Vector3.up : Vector3.forward);
            Vector2 dir = Screen2(startPos + axis * Arm) - c;
            if (dir.magnitude < 0.5f) return 1f;
            dir.Normalize();
            float p0 = Vector2.Dot(a - c, dir);
            if (Mathf.Abs(p0) < 2f) return 1f;
            return Vector2.Dot(b - c, dir) / p0;
        }

        /// Parameter t of the point on the line (o + d t) closest to the
        /// camera ray through the screen point; NaN if it faces the camera.
        float RayAxisParam(Vector3 o, Vector3 d, Vector2 s)
        {
            Ray r = cam.ScreenPointToRay(s);
            Vector3 w0 = o - r.origin;
            float a = Vector3.Dot(d, d), b = Vector3.Dot(d, r.direction), c = Vector3.Dot(r.direction, r.direction);
            float dd = Vector3.Dot(d, w0), e = Vector3.Dot(r.direction, w0);
            float denom = a * c - b * b;
            if (Mathf.Abs(denom) < 1e-6f) return float.NaN;
            return (b * e - c * dd) / denom;
        }
    }
}
