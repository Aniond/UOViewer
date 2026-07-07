using System.Collections.Generic;
using UnityEngine;

namespace UOHD2D.Game
{
    /*
     * Maps UO item graphic ids (what the server sends in a mobile's equipment list) to our 3D
     * EquippableItem assets. Fill in one entry per UO item you've built a mesh for; unmapped item
     * ids render nothing. This is the bridge between the authentic UO item system and our gear:
     * the server decides WHAT is worn (item id + layer), this decides how it LOOKS in 3D.
     */
    [CreateAssetMenu(menuName = "UO HD2D/Gear Catalog")]
    public class GearCatalog : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            [Tooltip("UO item graphic id (hex in UO docs, decimal here), e.g. 5127 = plate helm.")]
            public int UoItemId;
            public EquippableItem Item;
        }

        public List<Entry> Entries = new List<Entry>();

        private Dictionary<int, EquippableItem> _byId;

        private void BuildIndex()
        {
            _byId = new Dictionary<int, EquippableItem>();
            foreach (var e in Entries)
                if (e.Item != null && !_byId.ContainsKey(e.UoItemId))
                    _byId[e.UoItemId] = e.Item;
        }

        // Look up the 3D item for a UO graphic id. Returns null when we have no mesh for it.
        public EquippableItem Resolve(int uoItemId)
        {
            if (_byId == null)
                BuildIndex();

            return _byId.TryGetValue(uoItemId, out var item) ? item : null;
        }

        // Rebuild the index after edits (call from tooling if the list changed at runtime).
        public void Invalidate() => _byId = null;
    }
}
