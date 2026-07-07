#if UNITY_EDITOR
using System.Collections.Generic;

using UnityEngine;

namespace UOHD2D
{
	/*
	 * Emits kit piece geometry in tile-local space so the importer's kit pass
	 * can transform and append it into per-family combined meshes. Local
	 * convention: origin at the tile's NW corner; the tile spans x 0..1 and
	 * z 0..-1 (world z = -tileY); +y up. Wall pieces (rotY 0) run along the
	 * tile's NORTH edge; rotY 90 rotates around the origin so they run down
	 * the WEST edge. Roof slopes descend toward their rotY direction
	 * (0 = UO north = +worldZ).
	 *
	 * All dimensions are exact tile math: story height = 20 z-units, roof
	 * rise = 3 z-units per tile row.
	 */
	public class KitPieceGeom
	{
		public readonly List<Vector3> Verts = new List<Vector3>();
		public readonly List<Vector3> Normals = new List<Vector3>();
		public readonly List<Vector2> UVs = new List<Vector2>();
		public readonly List<int> Tris = new List<int>();
	}

	public static class UOHD2DKitMeshBuilder
	{
		public const float StoryHeight = 20f * (4f / 44f); // 1.8182
		public const float RoofStep = 3f * (4f / 44f);     // 0.2727
		public const float WallThickness = 0.2f;
		public const float RoofThickness = 0.12f;
		public const float FoundationDepth = 0.5f; // walls reach below their base z to meet sloping platforms

		private static readonly Dictionary<string, KitPieceGeom> Cache = new Dictionary<string, KitPieceGeom>();

		public static KitPieceGeom Get(string piece, float wallHeight)
		{
			// Gable-merged walls have non-standard heights; cache by piece+height.
			var key = piece + ":" + wallHeight.ToString("F3");
			KitPieceGeom geom;

			if (Cache.TryGetValue(key, out geom))
				return geom;

			geom = Build(piece, wallHeight);
			Cache[key] = geom;
			return geom;
		}

		private static KitPieceGeom Build(string piece, float h)
		{
			var g = new KitPieceGeom();
			var t = WallThickness;

			var f = -FoundationDepth;

			switch (piece)
			{
				case "wall":
					AddBox(g, new Vector3(0f, f, -t * 0.5f), new Vector3(1f, h, t * 0.5f));
					break;

				case "window":
					// Wall slab with a 0.4x0.7 opening: jambs, sill, lintel.
					AddBox(g, new Vector3(0f, f, -t * 0.5f), new Vector3(0.3f, h, t * 0.5f));
					AddBox(g, new Vector3(0.7f, f, -t * 0.5f), new Vector3(1f, h, t * 0.5f));
					AddBox(g, new Vector3(0.3f, f, -t * 0.5f), new Vector3(0.7f, 0.8f, t * 0.5f));
					AddBox(g, new Vector3(0.3f, 1.5f, -t * 0.5f), new Vector3(0.7f, h, t * 0.5f));
					break;

				case "post":
					AddBox(g, new Vector3(-0.1f, f, -0.1f), new Vector3(0.1f, h, 0.1f));
					break;

				case "corner":
					AddBox(g, new Vector3(-0.12f, f, -0.12f), new Vector3(0.12f, h, 0.12f));
					break;

				case "flat":
					AddBox(g, new Vector3(0f, 0f, -1f), new Vector3(1f, RoofThickness, 0f));
					break;

				case "ridge":
					// Prism cap, ridge line running E-W through the tile center.
					AddRidge(g);
					break;

				// v1: hip/valley corners render as slopes; the audit flags them
				// so the verify loop can promote them to real corner wedges later.
				case "slope":
				case "corner_out":
				case "corner_in":
				default:
					AddSlope(g);
					break;
			}

			return g;
		}

		// Wedge descending toward local +z-ish? No: descending toward the tile's
		// NORTH edge (local z = 0); high edge at the south edge (local z = -1).
		// rotY at placement turns this toward the actual downhill direction.
		private static void AddSlope(KitPieceGeom g)
		{
			var lowY = 0f;
			var highY = RoofStep;

			// Top surface corners: north edge low, south edge high.
			var nw = new Vector3(0f, lowY, 0f);
			var ne = new Vector3(1f, lowY, 0f);
			var sw = new Vector3(0f, highY, -1f);
			var se = new Vector3(1f, highY, -1f);

			var down = -RoofThickness;
			var nwB = nw + Vector3.up * down;
			var neB = ne + Vector3.up * down;
			var swB = sw + Vector3.up * down;
			var seB = se + Vector3.up * down;

			var topNormal = Vector3.Cross(ne - nw, sw - nw).normalized;

			if (topNormal.y < 0)
				topNormal = -topNormal;

			AddQuad(g, nw, ne, sw, se, topNormal, true);      // top
			AddQuad(g, nwB, neB, swB, seB, -topNormal, false); // bottom
			AddQuad(g, nw, ne, nwB, neB, new Vector3(0f, 0f, 1f), false);  // north fascia
			AddQuad(g, sw, se, swB, seB, new Vector3(0f, 0f, -1f), true);  // south edge
			AddQuad(g, nw, sw, nwB, swB, new Vector3(-1f, 0f, 0f), true);  // west skirt
			AddQuad(g, ne, se, neB, seB, new Vector3(1f, 0f, 0f), false);  // east skirt
		}

		private static void AddRidge(KitPieceGeom g)
		{
			var apexY = RoofStep;

			var n0 = new Vector3(0f, 0f, 0f);
			var n1 = new Vector3(1f, 0f, 0f);
			var s0 = new Vector3(0f, 0f, -1f);
			var s1 = new Vector3(1f, 0f, -1f);
			var r0 = new Vector3(0f, apexY, -0.5f);
			var r1 = new Vector3(1f, apexY, -0.5f);

			var nNormal = Vector3.Cross(n1 - n0, r0 - n0).normalized;

			if (nNormal.y < 0) nNormal = -nNormal;

			var sNormal = new Vector3(nNormal.x, nNormal.y, -nNormal.z);

			AddQuad(g, n0, n1, r0, r1, nNormal, true);  // north face
			AddQuad(g, r0, r1, s0, s1, sNormal, true);  // south face

			// End caps (triangles).
			AddTriangle(g, n0, r0, s0, new Vector3(-1f, 0f, 0f));
			AddTriangle(g, n1, s1, r1, new Vector3(1f, 0f, 0f));
		}

		private static void AddBox(KitPieceGeom g, Vector3 min, Vector3 max)
		{
			// +X face
			AddQuad(g, new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z), new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z), Vector3.right, true);
			// -X face
			AddQuad(g, new Vector3(min.x, min.y, max.z), new Vector3(min.x, min.y, min.z), new Vector3(min.x, max.y, max.z), new Vector3(min.x, max.y, min.z), Vector3.left, true);
			// +Y face
			AddQuad(g, new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z), new Vector3(min.x, max.y, max.z), new Vector3(max.x, max.y, max.z), Vector3.up, true);
			// -Y face
			AddQuad(g, new Vector3(min.x, min.y, max.z), new Vector3(max.x, min.y, max.z), new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z), Vector3.down, true);
			// +Z face
			AddQuad(g, new Vector3(max.x, min.y, max.z), new Vector3(min.x, min.y, max.z), new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z), Vector3.forward, true);
			// -Z face
			AddQuad(g, new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z), new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z), Vector3.back, true);
		}

		// a-b along one edge, c-d the opposite edge (a under c, b under d).
		private static void AddQuad(KitPieceGeom g, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, bool flip)
		{
			var baseIndex = g.Verts.Count;

			g.Verts.Add(a); g.Verts.Add(b); g.Verts.Add(c); g.Verts.Add(d);

			for (var i = 0; i < 4; i++)
				g.Normals.Add(normal);

			g.UVs.Add(PlanarUV(a, normal));
			g.UVs.Add(PlanarUV(b, normal));
			g.UVs.Add(PlanarUV(c, normal));
			g.UVs.Add(PlanarUV(d, normal));

			if (flip)
			{
				g.Tris.Add(baseIndex); g.Tris.Add(baseIndex + 2); g.Tris.Add(baseIndex + 1);
				g.Tris.Add(baseIndex + 1); g.Tris.Add(baseIndex + 2); g.Tris.Add(baseIndex + 3);
			}
			else
			{
				g.Tris.Add(baseIndex); g.Tris.Add(baseIndex + 1); g.Tris.Add(baseIndex + 2);
				g.Tris.Add(baseIndex + 1); g.Tris.Add(baseIndex + 3); g.Tris.Add(baseIndex + 2);
			}
		}

		private static void AddTriangle(KitPieceGeom g, Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
		{
			var baseIndex = g.Verts.Count;
			g.Verts.Add(a); g.Verts.Add(b); g.Verts.Add(c);

			for (var i = 0; i < 3; i++)
			{
				g.Normals.Add(normal);
				g.UVs.Add(PlanarUV(g.Verts[baseIndex + i], normal));
			}

			g.Tris.Add(baseIndex); g.Tris.Add(baseIndex + 1); g.Tris.Add(baseIndex + 2);
		}

		// One texture repeat per world unit, projected on the face's dominant plane.
		private static Vector2 PlanarUV(Vector3 p, Vector3 normal)
		{
			var an = new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z));

			if (an.y >= an.x && an.y >= an.z)
				return new Vector2(p.x, p.z);

			if (an.x >= an.z)
				return new Vector2(p.z, p.y);

			return new Vector2(p.x, p.y);
		}
	}
}
#endif
