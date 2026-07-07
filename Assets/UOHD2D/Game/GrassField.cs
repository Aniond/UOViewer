using System.Collections.Generic;

using UnityEngine;

namespace UOHD2D.Game
{
	/*
	 * Renders baked grass tufts with GPU instancing, chunked spatially so whole
	 * chunks are skipped once they're beyond the camera fade distance (distance
	 * fade itself happens in the grass shader by shrinking tufts).
	 */
	[ExecuteAlways]
	public class GrassField : MonoBehaviour
	{
		private const int ChunkTiles = 16;
		private const int BatchSize = 1023;

		public GrassFieldData Data;
		public Material Material;
		public float CullDistance = 70f;

		private class Chunk
		{
			public Vector3 Center;
			public List<Matrix4x4[]> Batches = new List<Matrix4x4[]>();
			public List<int> Counts = new List<int>();
		}

		private List<Chunk> _chunks;
		private Mesh _tuftMesh;

		private void OnEnable()
		{
			Rebuild();
		}

		public void Rebuild()
		{
			_chunks = null;

			if (Data == null || Data.Positions == null || Data.Positions.Length == 0)
				return;

			if (_tuftMesh == null)
				_tuftMesh = BuildTuftMesh();

			var byChunk = new Dictionary<long, List<Matrix4x4>>();

			for (var i = 0; i < Data.Positions.Length; i++)
			{
				var pos = Data.Positions[i];
				var rot = Quaternion.Euler(0f, GrassFieldData.DecodeYaw(Data.Yaws[i]), 0f);
				var scale = GrassFieldData.DecodeScale(Data.Scales[i]);

				var cx = Mathf.FloorToInt(pos.x / ChunkTiles);
				var cz = Mathf.FloorToInt(pos.z / ChunkTiles);
				var key = ((long)cx << 32) ^ (uint)cz;

				List<Matrix4x4> list;

				if (!byChunk.TryGetValue(key, out list))
					byChunk[key] = list = new List<Matrix4x4>();

				list.Add(Matrix4x4.TRS(pos, rot, new Vector3(scale, scale, scale)));
			}

			_chunks = new List<Chunk>();

			foreach (var kv in byChunk)
			{
				var chunk = new Chunk();
				var sum = Vector3.zero;

				foreach (var m in kv.Value)
					sum += m.GetPosition();

				chunk.Center = sum / kv.Value.Count;

				for (var start = 0; start < kv.Value.Count; start += BatchSize)
				{
					var count = Mathf.Min(BatchSize, kv.Value.Count - start);
					var batch = new Matrix4x4[count];
					kv.Value.CopyTo(start, batch, 0, count);
					chunk.Batches.Add(batch);
					chunk.Counts.Add(count);
				}

				_chunks.Add(chunk);
			}
		}

		private void Update()
		{
			if (_chunks == null || Material == null || _tuftMesh == null)
				return;

			var cam = Camera.main;

#if UNITY_EDITOR
			if (cam == null && UnityEditor.SceneView.lastActiveSceneView != null)
				cam = UnityEditor.SceneView.lastActiveSceneView.camera;
#endif

			var camPos = cam != null ? cam.transform.position : Vector3.zero;
			var rp = new RenderParams(Material)
			{
				shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
				receiveShadows = true,
				worldBounds = new Bounds(camPos, Vector3.one * 500f)
			};

			foreach (var chunk in _chunks)
			{
				if (cam != null && (chunk.Center - camPos).sqrMagnitude > CullDistance * CullDistance)
					continue;

				for (var i = 0; i < chunk.Batches.Count; i++)
					Graphics.RenderMeshInstanced(rp, _tuftMesh, 0, chunk.Batches[i], chunk.Counts[i]);
			}
		}

		// Two crossed quads, bottom pivot, ~0.55 units tall.
		private static Mesh BuildTuftMesh()
		{
			var mesh = new Mesh { name = "grass_tuft" };
			var h = 0.55f;
			var w = 0.45f;

			var verts = new[]
			{
				new Vector3(-w, 0, 0), new Vector3(w, 0, 0), new Vector3(-w, h, 0), new Vector3(w, h, 0),
				new Vector3(0, 0, -w), new Vector3(0, 0, w), new Vector3(0, h, -w), new Vector3(0, h, w)
			};
			var uvs = new[]
			{
				new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1),
				new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1)
			};
			var tris = new[] { 0, 2, 1, 1, 2, 3, 4, 6, 5, 5, 6, 7 };

			mesh.vertices = verts;
			mesh.uv = uvs;
			mesh.triangles = tris;
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
			return mesh;
		}
	}
}
