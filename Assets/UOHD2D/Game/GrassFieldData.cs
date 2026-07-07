using UnityEngine;

namespace UOHD2D.Game
{
	/*
	 * Baked grass scatter for one region, stored as an asset so the scene file
	 * stays small. Positions are world space; yaw/scale are quantized bytes
	 * (yaw 0-255 -> 0-360deg, scale 0-255 -> 0.5-1.5).
	 */
	public class GrassFieldData : ScriptableObject
	{
		public Vector3[] Positions;
		public byte[] Yaws;
		public byte[] Scales;

		public static float DecodeYaw(byte yaw)
		{
			return yaw * (360f / 256f);
		}

		public static float DecodeScale(byte scale)
		{
			return 0.5f + scale * (1f / 255f);
		}
	}
}
