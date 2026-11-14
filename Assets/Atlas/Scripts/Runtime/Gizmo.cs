// ATLAS 3D (Unity) - touch gizmo for the selected object.
// Three handles (X red, Y green, Z blue) plus a centre one. Mover: drag a
// handle to slide along its axis (centre: on the ground). Girar: drag a
// handle sideways to turn around its axis. Escalar: drag a handle along
// its axis to stretch it (centre: the whole object). The handles keep the
// same size on screen at any distance and are found before the objects.
using UnityEngine;

namespace Atlas
{
    public enum Tool { Move, Rotate, Scale }

    public class Gizmo : MonoBehaviour
    {
        public const int Layer = 2;   // «Ignore Raycast»: never hit by the scene picking
        public Camera cam;
        public Material unlitTemplate;
        public Tool tool = Tool.Move;
        public Transform target;

        readonly Transform[] axes = new Transform[3];
        Transform centre;
        static readonly Vector3[] Dirs = { Vector3.right, Vector3.up, Vector3.forward };

        // Drag state
        int dragAxis = -1;        // 0..2 axis, 3 centre
        Vector3 startPos, startScale, startAxisWorld;
        Quaternion startRot;
        float startT;
        Vector2 startScreen;
        Plane dragPlane;
        Vector3 planeOffset;

        public bool Dragging { get { return dragAxis >= 0; } }

        public void Build()
        {
            Color[] cols = { AtlasTheme.AxisX, AtlasTheme.AxisY, AtlasTheme.AxisZ };
            for (int i = 0; i < 3; i++)
            {
                var axis = new GameObject("Eje" + i).transform;
                axis.SetParent(transform, false);
                axis.localRotation = Quaternion.FromToRotation(Vector3.up, Dirs[i]);
                var shaft = Part(PrimitiveType.Cylinder, axis, cols[i], new Vector3(0f, 0.5f, 0f), new Vector3(0.06f, 0.5f, 0.06f));
                shaft.name = "Barra";
                var tip = Part(PrimitiveType.Cube, axis, cols[i], new Vector3(0f, 1.05f, 0f), Vector3.one * 0.2f);
                tip.name = "Punta";
                axes[i] = axis;
            }
            centre = Part(PrimitiveType.Sphere, transform, Color.white, Vector3.zero, Vector3.one * 0.24f).transform;
            centre.name = "Centro";
            SetTool(tool);
            gameObject.SetActive(false);
        }

        GameObject Part(PrimitiveType kind, Transform parent, Color c, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(kind);
            go.layer = Layer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var m = new Material(unlitTemplate);
            m.SetColor("_BaseColor", c);
            r.sharedMaterial = m;
            // Fatter touch area than the visible bar.
            var cap = go.GetComponent<CapsuleCollider>();
            if (cap != null) cap.radius = 1.6f;
            return go;
        }

        public void SetTool(Tool t)
        {
            tool = t;
            for (int i = 0; i < 3; i++)
            {
                if (axes[i] == null) continue;
                var tip = axes[i].Find("Punta");
                if (tip == null) continue;
                var mf = tip.GetComponent<MeshFilter>();
                if (mf != null) mf.sharedMesh = PrimitiveMesh(t == Tool.Rotate ? PrimitiveType.Sphere : PrimitiveType.Cube);
                tip.localScale = Vector3.one * (t == Tool.Rotate ? 0.26f : 0.2f);
            }
        }

        static Mesh sphereMesh, cubeMesh;

        static Mesh PrimitiveMesh(PrimitiveType kind)
        {
            if (kind == PrimitiveType.Sphere && sphereMesh != null) return sphereMesh;
            if (kind == PrimitiveType.Cube && cubeMesh != null) return cubeMesh;
            var tmp = GameObject.CreatePrimitive(kind);
            var mesh = tmp.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(tmp);
            if (kind == PrimitiveType.Sphere) sphereMesh = mesh; else cubeMesh = mesh;
            return mesh;
        }

        void LateUpdate()
        {
            if (target == null || cam == null)
            {
                if (gameObject.activeSelf) gameObject.SetActive(false);
                return;
            }
            transform.position = target.position;
            // Rotation and scale handles follow the object's own axes.
            transform.rotation = tool == Tool.Move ? Quaternion.identity : target.rotation;
            float d = Vector3.Distance(cam.transform.position, target.position);
            transform.localScale = Vector3.one * Mathf.Max(0.05f, d * 0.13f);
        }

        public void Show(Transform t)
        {
            target = t;
            gameObject.SetActive(t != null);
            if (t != null) LateUpdate();
        }

        Vector3 AxisWorld(int i)
        {
            return i < 3 ? (transform.rotation * Dirs[i]).normalized : Vector3.up;
        }

        /// Starts a drag if the finger is on a handle.
        public bool TryBegin(Vector2 screen)
        {
            if (target == null || !gameObject.activeInHierarchy) return false;
            RaycastHit hit;
            if (!Physics.Raycast(cam.ScreenPointToRay(screen), out hit, 1000f, 1 << Layer)) return false;
            dragAxis = -1;
            for (int i = 0; i < 3; i++)
                if (hit.transform.IsChildOf(axes[i])) dragAxis = i;
            if (dragAxis < 0 && hit.transform == centre) dragAxis = 3;
            if (dragAxis < 0) return false;
            startPos = target.position;
            startRot = target.rotation;
            startScale = target.localScale;
            startScreen = screen;
            startAxisWorld = AxisWorld(dragAxis);
            if (dragAxis < 3) startT = AxisParam(screen, startPos, startAxisWorld);
            else
            {
                dragPlane = new Plane(Vector3.up, startPos);
                float e;
                Ray r = cam.ScreenPointToRay(screen);
                planeOffset = dragPlane.Raycast(r, out e) ? startPos - r.GetPoint(e) : Vector3.zero;
            }
            return true;
        }

        public void Drag(Vector2 screen)
        {
            if (dragAxis < 0 || target == null) return;
            float size = transform.localScale.x;
            if (tool == Tool.Move)
            {
                if (dragAxis < 3)
                    target.position = startPos + startAxisWorld * (AxisParam(screen, startPos, startAxisWorld) - startT);
                else
                {
                    float e;
                    Ray r = cam.ScreenPointToRay(screen);
                    if (dragPlane.Raycast(r, out e)) target.position = r.GetPoint(e) + planeOffset;
                }
            }
            else if (tool == Tool.Rotate)
            {
                float deg = (screen.x - startScreen.x) * 0.45f - (screen.y - startScreen.y) * 0.2f;
                Vector3 axis = dragAxis < 3 ? startAxisWorld : Vector3.up;
                target.rotation = Quaternion.AngleAxis(deg, axis) * startRot;
            }
            else
            {
                if (dragAxis < 3)
                {
                    float f = Mathf.Max(0.02f, 1f + (AxisParam(screen, startPos, startAxisWorld) - startT) / Mathf.Max(0.01f, size));
                    Vector3 s = startScale;
                    s[dragAxis] = startScale[dragAxis] * f;
                    target.localScale = s;
                }
                else
                {
                    float f = Mathf.Max(0.02f, 1f + (screen.y - startScreen.y + screen.x - startScreen.x) * 0.004f);
                    target.localScale = startScale * f;
                }
            }
        }

        public void End()
        {
            dragAxis = -1;
        }

        /// Position along the line (origin, dir) closest to the finger's ray.
        float AxisParam(Vector2 screen, Vector3 origin, Vector3 dir)
        {
            Ray r = cam.ScreenPointToRay(screen);
            Vector3 w = origin - r.origin;
            float b = Vector3.Dot(dir, r.direction);
            float dd = Vector3.Dot(dir, w);
            float e = Vector3.Dot(r.direction, w);
            float denom = 1f - b * b;
            if (Mathf.Abs(denom) < 1e-4f) return 0f;
            return (b * e - dd) / denom;
        }
    }
}
