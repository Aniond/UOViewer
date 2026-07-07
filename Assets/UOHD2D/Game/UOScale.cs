namespace UOHD2D.Game
{
	/*
	 * Scale conventions shared across the project (same values the importer bakes
	 * with): 1 UO tile = 1 world unit, 44 art pixels = 1 world unit, 1 UO z-step
	 * = 4/44 world units. worldX = tileX, worldZ = -tileY, worldY = uoZ * ZScale.
	 */
	public static class UOScale
	{
		public const float PPU = 44f;
		public const float ZScale = 4f / 44f;
	}
}
