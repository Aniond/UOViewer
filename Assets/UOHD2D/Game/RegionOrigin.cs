using UnityEngine;

namespace UOHD2D.Game
{
    // Placed on the "UO_map{n}_{x0}_{y0}" root created by UOHD2DImporter. Records
    // the region's absolute UO tile origin and map id so absolute UO coordinates
    // (as sent over the network) can be converted to this scene's local world space.
    //
    // Matches UOHD2DImporter's convention: worldX = tileX, worldZ = -tileY,
    // worldY = uoZ * (4/44), with tileX/tileY relative to the region's (x0,y0).
    public class RegionOrigin : MonoBehaviour
    {
        private const float ZScale = 4f / 44f;

        public int Map;
        public int X0;
        public int Y0;

        private static RegionOrigin _active;

        public static RegionOrigin Active
        {
            get
            {
                if (_active == null)
                    _active = FindObjectOfType<RegionOrigin>();

                return _active;
            }
        }

        private void Awake()
        {
            _active = this;
        }

        public Vector3 ToWorld(int uoX, int uoY, int uoZ)
        {
            var localX = uoX - X0;
            var localY = uoY - Y0;
            return transform.TransformPoint(new Vector3(localX, uoZ * ZScale, -localY));
        }

        public void ToUo(Vector3 worldPos, out int uoX, out int uoY, out int uoZ)
        {
            var local = transform.InverseTransformPoint(worldPos);
            uoX = X0 + Mathf.RoundToInt(local.x);
            uoY = Y0 - Mathf.RoundToInt(local.z);
            uoZ = Mathf.RoundToInt(local.y / ZScale);
        }
    }
}
