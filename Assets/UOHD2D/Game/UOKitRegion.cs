using System;
using System.Collections.Generic;

using UnityEngine;

namespace UOHD2D.Game
{
	/*
	 * Serialized record of every kit piece the importer placed, kept on the
	 * Kit3D root. Feeds debug overlays now and a per-building interior /
	 * roof-hiding system later without re-running the importer.
	 */
	public class UOKitRegion : MonoBehaviour
	{
		[Serializable]
		public struct PieceRecord
		{
			public int ArtId;
			public string Family;
			public string Piece;
			public int RotY;
			public int X;
			public int Y;
			public int Z;
			public int Story;
			public int ClusterId;
		}

		public List<PieceRecord> Records = new List<PieceRecord>();
	}
}
