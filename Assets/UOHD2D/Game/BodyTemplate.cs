using UnityEngine;

namespace UOHD2D.Game
{
    public enum BodyGender { Male, Female }

    /*
     * A base body template - one per gender. Every character (player, NPC, creature-person) is
     * built on a template, so its skeleton and proportions are fixed and shared. Equipment is
     * fitted ONCE against a template's skeleton and then fits every character on that template.
     *
     * Individuals differ only by texture (skin/face) and equipped gear, never by body shape - so
     * gear never needs per-character refitting.
     */
    [CreateAssetMenu(menuName = "UO HD2D/Body Template")]
    public class BodyTemplate : ScriptableObject
    {
        public BodyGender Gender = BodyGender.Male;

        [Tooltip("The base body prefab/model (GLB or FBX) with the template skeleton.")]
        public GameObject BodyPrefab;

        [Tooltip("Plain skin material applied to the body so it's a bare base (no baked clothing). " +
                 "Clothing/armor then layer OVER it as separate equipment. Leave null to keep the " +
                 "model's own texture.")]
        public Material SkinMaterial;

        [Tooltip("Humanoid avatar for this template (built by UOHD2DHumanoidRig with a T-pose).")]
        public Avatar Avatar;

        [Tooltip("Locomotion animator controller (idle/walk) driving this template.")]
        public RuntimeAnimatorController Controller;

        [Tooltip("Head-to-toe world height the body scales to.")]
        public float TargetHeight = 1.8f;

        [Tooltip("Fine vertical seating so the soles sit exactly on the tile.")]
        public float GroundOffset = 0.02f;
    }
}
