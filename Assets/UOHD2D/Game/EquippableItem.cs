using UnityEngine;

namespace UOHD2D.Game
{
    /*
     * One wearable item (helmet, hat, weapon, shield, backpack...). Bundles the mesh prefab with
     * the fit data - which body bone it rides, and the local offset/rotation/scale that seat it
     * correctly on that bone. Tune the fit once here and every EquipItem call places it perfectly.
     *
     * Attach = Rigid parents the prefab to the bone (helmets, weapons - the default). Skinned would
     * rebind a SkinnedMeshRenderer to the body skeleton (full body armor) - not all pieces support it.
     */
    [CreateAssetMenu(menuName = "UO HD2D/Equippable Item")]
    public class EquippableItem : ScriptableObject
    {
        public string DisplayName = "Item";
        public ArmorSlot Slot = ArmorSlot.Head;
        public GameObject Prefab;

        [Header("Rigid fit on the bone")]
        [Tooltip("Body bone this piece rides, e.g. Head, R_Hand, L_Hand, Spine02.")]
        public string BoneName = "Head";

        [Tooltip("Local position offset on the bone.")]
        public Vector3 LocalOffset = Vector3.zero;

        [Tooltip("Local rotation (euler) on the bone.")]
        public Vector3 LocalEuler = Vector3.zero;

        [Tooltip("Uniform scale of the piece.")]
        public float Scale = 1f;
    }
}
