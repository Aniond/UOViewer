using UnityEngine;

namespace UOHD2D.Game
{
	/*
	 * A generated character sprite set: one packed grid sheet where
	 * row = animIndex * 8 + directionIndex and column = frame.
	 * Direction order matches the workbench manifest and maps from the UO
	 * direction byte via UODirectionToIndex.
	 */
	[CreateAssetMenu(menuName = "UO HD2D/Character Sprite Set")]
	public class CharacterSpriteSet : ScriptableObject
	{
		[System.Serializable]
		public class AnimDef
		{
			public string Name;
			public int Frames;
			public float Fps;
		}

		public Texture2D Sheet;
		public Material Material;
		public int Canvas = 64;
		public float PPU = UOScale.PPU;
		public Vector2 Pivot = new Vector2(0.5f, 0.06f);
		public AnimDef[] Animations;
		public int Columns;
		public int Rows;

		// Sheet direction order (row within an animation block).
		// UO direction byte (running flag masked off): 0=N 1=NE 2=E 3=SE 4=S 5=SW 6=W 7=NW
		private static readonly int[] UODirToSheetIndex = { 4, 3, 2, 1, 0, 7, 6, 5 };
		// sheet order: south, south-east, east, north-east, north, north-west, west, south-west

		public static int UODirectionToIndex(byte uoDirection)
		{
			return UODirToSheetIndex[uoDirection & 0x07];
		}

		public int FindAnimation(string name)
		{
			for (var i = 0; i < Animations.Length; i++)
				if (Animations[i].Name == name)
					return i;

			return 0;
		}

		public Rect GetUV(int animIndex, int dirIndex, int frame)
		{
			var row = animIndex * 8 + dirIndex;
			var u = frame / (float)Columns;
			var v = 1f - (row + 1) / (float)Rows;
			return new Rect(u, v, 1f / Columns, 1f / Rows);
		}
	}
}
