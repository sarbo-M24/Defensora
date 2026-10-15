using System.Collections.Generic;
using UnityEngine;

namespace Defensora.FX
{
    /// <summary>
    /// A sagging string of papel picado flags hung between two points.
    /// Move the Start / End children (e.g. onto two lantern posts) and the string rebuilds.
    /// The whole string is one mesh using one atlas material, and the flutter runs in the
    /// shader (Defensora/Papel Picado), so many strings stay cheap.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class PapelPicadoString : MonoBehaviour
    {
        public enum FlagPattern { Random, Sequence, Single }

        [Header("Hang Points")]
        [SerializeField] private Transform start;
        [SerializeField] private Transform end;
        [Tooltip("Optional posts between Start and End, in order. The string sags between each pair.")]
        [SerializeField] private Transform[] midPoints = new Transform[0];
        [Tooltip("How far the middle of each span droops, in metres.")]
        [SerializeField, Min(0f)] private float sag = 0.45f;

        [Header("Flags")]
        [SerializeField, Min(0.05f)] private float flagWidth = 0.32f;
        [SerializeField, Min(0f)] private float flagGap = 0.05f;
        [Tooltip("Bare string left at each end before the first flag.")]
        [SerializeField, Min(0f)] private float endPadding = 0.25f;
        [SerializeField] private FlagPattern pattern = FlagPattern.Random;
        [Tooltip("Design used when Pattern is Single.")]
        [SerializeField, Min(0)] private int singleFlag;
        [SerializeField] private int seed = 1;
        [Tooltip("Vertical subdivisions per flag, so the flutter can bend it. 2-3 is plenty.")]
        [SerializeField, Range(1, 4)] private int flagRows = 2;

        [Header("String")]
        [SerializeField, Min(0.001f)] private float stringThickness = 0.008f;
        [SerializeField, Range(4, 48)] private int stringSegments = 20;
        [Tooltip("How much the string itself swings relative to the flags' free edges.")]
        [SerializeField, Range(0f, 1f)] private float stringSwing = 0.5f;

        [Header("Atlas")]
        [Tooltip("UV rect of each flag design in the atlas (x, y, width, height).")]
        [SerializeField] private Rect[] flagRects =
        {
            new Rect(0.0226f, 0.5564f, 0.2730f, 0.3951f), // sugar-skull lady (blue)
            new Rect(0.3235f, 0.5540f, 0.3427f, 0.3922f), // sun (yellow)
            new Rect(0.6829f, 0.5530f, 0.2784f, 0.3985f), // flower wreath (dark green)
            new Rect(0.0229f, 0.0658f, 0.3055f, 0.4244f), // skull & leaves (green)
            new Rect(0.3343f, 0.0667f, 0.3213f, 0.4234f), // flower (pink)
            new Rect(0.6663f, 0.0667f, 0.2953f, 0.4239f), // heart (red)
        };
        [Tooltip("Atlas aspect (width / height), used to keep the flags' proportions.")]
        [SerializeField, Min(0.01f)] private float atlasAspect = 3542f / 2083f;

        private Mesh mesh;
        private bool dirty = true;

        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<Vector2> sway = new List<Vector2>();
        private readonly List<Color32> colors = new List<Color32>();
        private readonly List<int> triangles = new List<int>();
        private readonly List<Transform> points = new List<Transform>();

        /// <summary>Hangs the string between two world-space points.</summary>
        public void HangBetween(Vector3 a, Vector3 b) => HangAlong(new[] { a, b });

        /// <summary>Hangs the string through world-space points in order, creating mid point children as needed.</summary>
        public void HangAlong(IList<Vector3> points)
        {
            if (points == null || points.Count < 2 || start == null || end == null)
                return;

            Vector3 centre = Vector3.zero;
            foreach (Vector3 p in points)
                centre += p;
            transform.position = centre / points.Count;

            start.position = points[0];
            end.position = points[points.Count - 1];

            var mids = new Transform[points.Count - 2];
            for (int i = 0; i < mids.Length; i++)
            {
                Transform mid = midPoints != null && i < midPoints.Length ? midPoints[i] : null;
                if (mid == null)
                {
                    mid = new GameObject("Point " + (i + 1)).transform;
                    mid.SetParent(transform, false);
                }
                mid.position = points[i + 1];
                mids[i] = mid;
            }
            midPoints = mids;
            Rebuild();
        }

        private void CollectPoints(List<Transform> into)
        {
            into.Clear();
            into.Add(start);
            if (midPoints != null)
                foreach (Transform mid in midPoints)
                    if (mid != null)
                        into.Add(mid);
            into.Add(end);
        }

        private void OnEnable()
        {
            dirty = true;
            Rebuild();
        }

        private void OnDisable()
        {
            if (mesh != null)
            {
                GetComponent<MeshFilter>().sharedMesh = null;
                if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh);
                mesh = null;
            }
        }

        private void OnValidate() => dirty = true;

        private void Update()
        {
            // At runtime the string is static (the flutter is in the shader); in the editor follow the handles.
            if (Application.isPlaying && !dirty)
                return;

            bool moved = transform.hasChanged;
            if (start == null || end == null)
                moved = false;
            else
            {
                CollectPoints(points);
                foreach (Transform p in points)
                    moved |= p.hasChanged;
            }
            if (dirty || moved)
                Rebuild();
        }

        public void Rebuild()
        {
            dirty = false;
            transform.hasChanged = false;

            if (mesh == null)
            {
                mesh = new Mesh { name = "Papel Picado String", hideFlags = HideFlags.DontSave };
                mesh.MarkDynamic();
            }
            GetComponent<MeshFilter>().sharedMesh = mesh;

            vertices.Clear(); normals.Clear(); uvs.Clear(); sway.Clear(); colors.Clear(); triangles.Clear();
            mesh.Clear();
            if (start == null || end == null)
                return;

            CollectPoints(points);
            Vector3 down = transform.InverseTransformDirection(Vector3.down).normalized;
            var random = new System.Random(seed);
            int flagIndex = 0;

            for (int i = 0; i + 1 < points.Count; i++)
            {
                Vector3 a = transform.InverseTransformPoint(points[i].position);
                Vector3 b = transform.InverseTransformPoint(points[i + 1].position);
                points[i].hasChanged = false;
                points[i + 1].hasChanged = false;

                Vector3 chord = b - a;
                float length = chord.magnitude;
                if (length < 0.01f)
                    continue;

                // Flags hang in the vertical plane through the span; they flutter along its normal.
                Vector3 along = Vector3.ProjectOnPlane(chord, down);
                if (along.sqrMagnitude < 1e-6f)
                    along = Vector3.ProjectOnPlane(Vector3.right, down);
                Vector3 side = Vector3.Cross(down, along).normalized;

                BuildString(a, b, down, side);
                BuildFlags(a, b, down, side, length, random, ref flagIndex);
            }

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, sway);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            // Leave room for the shader-driven flutter so the string isn't culled early.
            Bounds bounds = mesh.bounds;
            bounds.Expand(0.3f);
            mesh.bounds = bounds;
        }

        private Vector3 PointAt(Vector3 a, Vector3 b, Vector3 down, float t)
        {
            return Vector3.Lerp(a, b, t) + down * (sag * 4f * t * (1f - t));
        }

        // Swing weight and phase of the string at t, shared by the flags' top edges so they stay attached.
        private Vector2 StringSway(float t) => new Vector2(stringSwing * Mathf.Sin(Mathf.PI * t), t * 3f);

        private void BuildString(Vector3 a, Vector3 b, Vector3 down, Vector3 side)
        {
            // Two crossed thin ribbons so the cord reads from any angle.
            var cord = new Color32(255, 255, 255, 0);
            float half = stringThickness * 0.5f;
            Vector3[] offsets = { side * half, down * half };

            foreach (Vector3 offset in offsets)
            {
                int first = vertices.Count;
                for (int i = 0; i <= stringSegments; i++)
                {
                    float t = i / (float)stringSegments;
                    Vector3 p = PointAt(a, b, down, t);
                    Vector2 s = StringSway(t);
                    AddVertex(p - offset, side, Vector2.zero, s, cord);
                    AddVertex(p + offset, side, Vector2.zero, s, cord);
                }
                for (int i = 0; i < stringSegments; i++)
                {
                    int v = first + i * 2;
                    AddQuad(v, v + 1, v + 3, v + 2);
                }
            }
        }

        private void BuildFlags(Vector3 a, Vector3 b, Vector3 down, Vector3 side, float length,
            System.Random random, ref int flagIndex)
        {
            if (flagRects == null || flagRects.Length == 0)
                return;

            float usable = length - endPadding * 2f;
            int count = Mathf.FloorToInt((usable + flagGap) / (flagWidth + flagGap));
            if (count < 1)
                return;

            float used = count * flagWidth + (count - 1) * flagGap;
            float offset = endPadding + (usable - used) * 0.5f;
            var paper = new Color32(255, 255, 255, 255);

            for (int f = 0; f < count; f++, flagIndex++)
            {
                int design = pattern switch
                {
                    FlagPattern.Sequence => flagIndex % flagRects.Length,
                    FlagPattern.Single => Mathf.Clamp(singleFlag, 0, flagRects.Length - 1),
                    _ => random.Next(flagRects.Length),
                };
                Rect r = flagRects[design];
                float height = flagWidth * (r.height / (r.width * atlasAspect));
                float flagPhase = (float)random.NextDouble() * 6.283f;

                float t0 = (offset + f * (flagWidth + flagGap)) / length;
                float t1 = t0 + flagWidth / length;
                float tMid = (t0 + t1) * 0.5f;
                Vector3 top0 = PointAt(a, b, down, t0);
                Vector3 top1 = PointAt(a, b, down, t1);

                int first = vertices.Count;
                for (int row = 0; row <= flagRows; row++)
                {
                    float k = row / (float)flagRows;
                    Vector3 drop = down * (height * k);
                    float v = Mathf.Lerp(r.yMax, r.yMin, k);

                    // Top row follows the string exactly; lower rows flap with the flag's own phase.
                    Vector2 s0 = StringSway(t0), s1 = StringSway(t1);
                    if (row > 0)
                    {
                        float phase = StringSway(tMid).y + flagPhase * k;
                        s0 = new Vector2(Mathf.Lerp(s0.x, 1f, k), phase);
                        s1 = new Vector2(Mathf.Lerp(s1.x, 1f, k), phase);
                    }

                    AddVertex(top0 + drop, side, new Vector2(r.xMin, v), s0, paper);
                    AddVertex(top1 + drop, side, new Vector2(r.xMax, v), s1, paper);
                }
                for (int row = 0; row < flagRows; row++)
                {
                    int v = first + row * 2;
                    AddQuad(v, v + 1, v + 3, v + 2);
                }
            }
        }

        private void AddVertex(Vector3 position, Vector3 normal, Vector2 uv, Vector2 swayData, Color32 color)
        {
            vertices.Add(position);
            normals.Add(normal);
            uvs.Add(uv);
            sway.Add(swayData);
            colors.Add(color);
        }

        private void AddQuad(int v0, int v1, int v2, int v3)
        {
            triangles.Add(v0); triangles.Add(v1); triangles.Add(v2);
            triangles.Add(v0); triangles.Add(v2); triangles.Add(v3);
        }

        private void OnDrawGizmosSelected()
        {
            if (start == null || end == null)
                return;

            Gizmos.color = new Color(1f, 0.5f, 0.1f);
            CollectPoints(points);
            for (int s = 0; s + 1 < points.Count; s++)
            {
                Vector3 a = points[s].position, b = points[s + 1].position;
                Vector3 prev = a;
                for (int i = 1; i <= 16; i++)
                {
                    float t = i / 16f;
                    Vector3 p = Vector3.Lerp(a, b, t) + Vector3.down * (sag * 4f * t * (1f - t));
                    Gizmos.DrawLine(prev, p);
                    prev = p;
                }
            }
            foreach (Transform p in points)
                Gizmos.DrawWireSphere(p.position, 0.05f);
        }
    }
}
