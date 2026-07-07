using UnityEngine;

namespace UOHD2D.Game
{
    // Describes one character's visual: the rigged body prefab (a generated + auto-rigged
    // humanoid, e.g. Assets/UOHD2D/Generated/Rig/base_human/base_human.prefab), an optional
    // Animator controller, and a target world height so different source models normalize to
    // the same in-world size. When RigPrefab is null the factory falls back to a primitive
    // stand-in so the game still runs before any model is assigned.
    [CreateAssetMenu(menuName = "UO HD2D/Character Profile")]
    public class CharacterProfile : ScriptableObject
    {
        public GameObject RigPrefab;
        public RuntimeAnimatorController Controller;

        // Humanoid avatar to assign at spawn. GLB models import with a generic Animator, so a
        // separately-built humanoid Avatar (UOHD2DHumanoidRig) must be applied for humanoid clips
        // to retarget. Leave null for models that already carry a valid avatar (FBX Humanoid).
        public Avatar Avatar;

        // Desired head-to-toe height in world units (~1 UO tile per stride). The factory scales
        // the instantiated model so its bounds match this. 1.8 ~= an average human against the
        // 1-tile-per-unit world.
        public float TargetHeight = 1.8f;

        // Fine-tune vertical seating after the factory drops the soles to the tile plane.
        // Positive lifts the model, negative sinks it - used to absorb the slack between the mesh
        // bounding box and the actual sole contact point.
        public float GroundOffset = 0f;
    }
}
