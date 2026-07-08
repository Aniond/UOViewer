using UnityEngine;

namespace UOHD2D.Game
{
    /*
     * One wearable item. Two kinds:
     *
     * Prop3D - a rigid mesh (helmet, weapon, shield, backpack) parented to a body bone. Bundles
     * the prefab with the fit data - which bone it rides and the local offset/rotation/scale that
     * seat it there. Tune the fit once and every EquipItem call places it perfectly.
     *
     * ClothingLayer - worn clothing/armor as 2D art in the mannequin's UV space, composited onto
     * the body's skin atlas by ClothingCompositor in paperdoll draw order (the "dress the 3D
     * mannequin with 2D art" pipeline). No mesh; the body silhouette stays the mannequin's.
     */
    [CreateAssetMenu(menuName = "UO HD2D/Equippable Item")]
    public class EquippableItem : ScriptableObject
    {
        public enum EquipKind { Prop3D, ClothingLayer }

        public string DisplayName = "Item";
        public EquipKind Kind = EquipKind.Prop3D;
        public ArmorSlot Slot = ArmorSlot.Head;
        public GameObject Prefab;

        [Header("Clothing layer (2D art in the body's UV space)")]
        [Tooltip("RGBA layer authored against the mannequin's UV atlas (see the wardrobe UV reference). Opaque where cloth covers the body.")]
        public Texture2D ClothingTexture;

        [Tooltip("Tint multiplied into the layer - the UO hue.")]
        public Color ClothingTint = Color.white;

        [Tooltip("UO layer byte this clothing occupies (drives paperdoll draw order + stacking).")]
        public byte UoLayer = UOHD2D.Game.UOLayer.MiddleTorso;

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
