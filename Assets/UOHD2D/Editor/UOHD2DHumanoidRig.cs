#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;

using UnityEditor;
using UnityEngine;

namespace UOHD2D
{
    /*
     * Builds a Mecanim Humanoid Avatar for a generated + auto-rigged character whose skeleton
     * uses Tripo's bone names (Hip / Spine01 / L_Upperarm / ...). glTFast imports GLBs with a
     * GENERIC avatar only, so a humanoid clip (text-to-motion, Mixamo) won't retarget onto it.
     * This maps the Tripo skeleton to Unity's standard humanoid bones and bakes an Avatar asset
     * you can drop on an Animator so any humanoid clip plays and retargets.
     *
     * Menu: UO HD2D/Rig/Build Humanoid Avatar (base_human). Idempotent - overwrites the avatar.
     */
    public static class UOHD2DHumanoidRig
    {
        private const string RigRoot = "Assets/UOHD2D/Generated/Rig/base_human";
        private const string PrefabPath = RigRoot + "/base_human.prefab";
        private const string AvatarPath = RigRoot + "/base_human_avatar.asset";

        // Unity humanoid bone name -> Tripo transform name. Covers the 15 required bones plus the
        // optional ones this skeleton provides. Fingers are absent (optional) and left unmapped.
        private static readonly Dictionary<string, string> HumanToTripo = new Dictionary<string, string>
        {
            { "Hips", "Hip" },
            { "Spine", "Spine01" },
            { "Chest", "Spine02" },
            { "Neck", "NeckTwist01" },
            { "Head", "Head" },
            { "LeftUpperLeg", "L_Thigh" },
            { "LeftLowerLeg", "L_Calf" },
            { "LeftFoot", "L_Foot" },
            { "LeftToes", "L_ToeBase" },
            { "RightUpperLeg", "R_Thigh" },
            { "RightLowerLeg", "R_Calf" },
            { "RightFoot", "R_Foot" },
            { "RightToes", "R_ToeBase" },
            { "LeftShoulder", "L_Clavicle" },
            { "LeftUpperArm", "L_Upperarm" },
            { "LeftLowerArm", "L_Forearm" },
            { "LeftHand", "L_Hand" },
            { "RightShoulder", "R_Clavicle" },
            { "RightUpperArm", "R_Upperarm" },
            { "RightLowerArm", "R_Forearm" },
            { "RightHand", "R_Hand" },
        };

        [MenuItem("UO HD2D/Rig/Build Humanoid Avatar (base_human)")]
        public static void BuildForBaseHuman()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

            if (prefab == null)
            {
                Debug.LogError("[UOHD2D] Humanoid rig: prefab not found at " + PrefabPath);
                return;
            }

            // Instantiate so we can read the live transform hierarchy + world/local poses.
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

            try
            {
                var avatar = BuildAvatar(instance);

                if (avatar == null || !avatar.isValid || !avatar.isHuman)
                {
                    Debug.LogError("[UOHD2D] Humanoid rig: avatar build failed (valid=" + (avatar != null && avatar.isValid) + ", human=" + (avatar != null && avatar.isHuman) + "). Check the console for BuildHumanAvatar warnings.");
                    return;
                }

                var existing = AssetDatabase.LoadAssetAtPath<Avatar>(AvatarPath);

                if (existing != null)
                {
                    EditorUtility.CopySerialized(avatar, existing);
                    Object.DestroyImmediate(avatar);
                }
                else
                {
                    AssetDatabase.CreateAsset(avatar, AvatarPath);
                }

                AssetDatabase.SaveAssets();
                Debug.Log("[UOHD2D] Humanoid rig: built valid humanoid avatar at " + AvatarPath);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static Avatar BuildAvatar(GameObject root)
        {
            // Index every transform by name so we can resolve both the human-bone map and the
            // full skeleton (BuildHumanAvatar needs a SkeletonBone entry for every transform on
            // the path from the root down to each mapped bone - simplest is: include all).
            var transforms = root.GetComponentsInChildren<Transform>(true);

            var humanBones = new List<HumanBone>();
            var defaults = new HumanLimit { useDefaultValues = true };

            foreach (var kv in HumanToTripo)
            {
                // Only add the mapping if the transform actually exists in this skeleton.
                var found = false;

                foreach (var t in transforms)
                {
                    if (t.name == kv.Value)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    Debug.LogWarning("[UOHD2D] Humanoid rig: Tripo bone '" + kv.Value + "' for human bone '" + kv.Key + "' not found - skipping.");
                    continue;
                }

                humanBones.Add(new HumanBone
                {
                    humanName = kv.Key,
                    boneName = kv.Value,
                    limit = defaults
                });
            }

            var skeleton = new List<SkeletonBone>(transforms.Length);

            foreach (var t in transforms)
            {
                skeleton.Add(new SkeletonBone
                {
                    name = t.name,
                    position = t.localPosition,
                    rotation = t.localRotation,
                    scale = t.localScale
                });
            }

            var desc = new HumanDescription
            {
                human = humanBones.ToArray(),
                skeleton = skeleton.ToArray(),
                // Neutral tuning - the A-pose from the model is close enough for retargeting.
                upperArmTwist = 0.5f,
                lowerArmTwist = 0.5f,
                upperLegTwist = 0.5f,
                lowerLegTwist = 0.5f,
                armStretch = 0.05f,
                legStretch = 0.05f,
                feetSpacing = 0f,
                hasTranslationDoF = false
            };

            return AvatarBuilder.BuildHumanAvatar(root, desc);
        }
    }
}
#endif
