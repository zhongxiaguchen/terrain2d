using System.Collections.Generic;
using UnityEngine;

namespace Terrain2D
{
    /// <summary>
    /// Ear-clipping triangulation for a single simple polygon.
    ///
    /// Requirements / limits:
    ///   - The polygon must NOT self-intersect.
    ///   - No holes. To cut a hole, draw it as a "keyhole": a narrow slit from the
    ///     outer contour into the hole contour, making one simple polygon.
    ///   - Winding order does not matter, it is normalised internally.
    ///
    /// Complexity is O(n^2) worst case, which is fine for terrain outlines
    /// (tens to a few hundred vertices).
    /// </summary>
    public static class Triangulator
    {
        const float Eps = 1e-9f;

        /// <summary>
        /// Triangulates <paramref name="polygon"/>.
        /// The returned list contains indices into the ORIGINAL list, 3 per triangle.
        /// Returns an empty list if the polygon is degenerate or self-intersecting.
        /// </summary>
        public static List<int> Triangulate(IList<Vector2> polygon)
        {
            var triangles = new List<int>();
            int n = polygon == null ? 0 : polygon.Count;
            if (n < 3) return triangles;

            // Working copy plus a parallel map back to the caller's indices, so we can
            // flip the winding order without invalidating the output indices.
            var verts = new List<Vector2>(n);
            var origin = new List<int>(n);
            for (int i = 0; i < n; i++)
            {
                verts.Add(polygon[i]);
                origin.Add(i);
            }

            if (SignedArea(verts) < 0f)
            {
                verts.Reverse();
                origin.Reverse();
            }

            var remaining = new List<int>(n);
            for (int i = 0; i < n; i++) remaining.Add(i);

            // Safety valve: a self-intersecting polygon can otherwise loop forever.
            int safety = n * n;

            while (remaining.Count > 3 && safety-- > 0)
            {
                bool clipped = false;

                for (int i = 0; i < remaining.Count; i++)
                {
                    int prev = remaining[(i + remaining.Count - 1) % remaining.Count];
                    int cur = remaining[i];
                    int next = remaining[(i + 1) % remaining.Count];

                    Vector2 a = verts[prev];
                    Vector2 b = verts[cur];
                    Vector2 c = verts[next];

                    // Convex corner? (CCW polygon -> positive cross product)
                    if (Cross(b - a, c - b) <= Eps) continue;

                    // No other vertex may sit inside this candidate ear.
                    if (ContainsAnyVertex(verts, remaining, prev, cur, next, a, b, c)) continue;

                    triangles.Add(origin[prev]);
                    triangles.Add(origin[cur]);
                    triangles.Add(origin[next]);

                    remaining.RemoveAt(i);
                    clipped = true;
                    break;
                }

                // Nothing could be clipped -> degenerate or self-intersecting input.
                if (!clipped) break;
            }

            if (remaining.Count == 3)
            {
                triangles.Add(origin[remaining[0]]);
                triangles.Add(origin[remaining[1]]);
                triangles.Add(origin[remaining[2]]);
            }

            return triangles;
        }

        static bool ContainsAnyVertex(List<Vector2> verts, List<int> remaining,
                                      int i0, int i1, int i2,
                                      Vector2 a, Vector2 b, Vector2 c)
        {
            for (int k = 0; k < remaining.Count; k++)
            {
                int idx = remaining[k];
                if (idx == i0 || idx == i1 || idx == i2) continue;
                if (PointInTriangle(verts[idx], a, b, c)) return true;
            }
            return false;
        }

        /// <summary>Positive for counter-clockwise polygons.</summary>
        public static float SignedArea(IList<Vector2> p)
        {
            float sum = 0f;
            for (int i = 0, j = p.Count - 1; i < p.Count; j = i++)
                sum += (p[j].x * p[i].y) - (p[i].x * p[j].y);
            return sum * 0.5f;
        }

        public static float Cross(Vector2 u, Vector2 v) => (u.x * v.y) - (u.y * v.x);

        /// <summary>Inclusive point-in-triangle test (points on an edge count as inside).</summary>
        static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(b - a, p - a);
            float d2 = Cross(c - b, p - b);
            float d3 = Cross(a - c, p - c);

            bool hasNeg = d1 < -Eps || d2 < -Eps || d3 < -Eps;
            bool hasPos = d1 > Eps || d2 > Eps || d3 > Eps;
            return !(hasNeg && hasPos);
        }
    }
}
