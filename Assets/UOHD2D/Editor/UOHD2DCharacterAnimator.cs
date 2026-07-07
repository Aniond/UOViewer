#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace UOHD2D
{
    /*
     * Builds the base_human locomotion Animator Controller (Idle <-> Walk driven by a "Moving"
     * bool) from the generated humanoid clips, and wires the humanoid Avatar + controller onto
     * the prefab's Animator so the spawned character animates. Also points the CharacterProfile
     * at the controller. Idempotent - rebuilds the controller in place.
     *
     * Menu: UO HD2D/Rig/Build Character Animator (base_human).
     * Run AFTER "Build Humanoid Avatar (base_human)".
     */
    public static class UOHD2DCharacterAnimator
    {
        private const string RigRoot = "Assets/UOHD2D/Generated/Rig/base_human";
        private const string PrefabPath = RigRoot + "/base_human.prefab";
        private const string AvatarPath = RigRoot + "/base_human_avatar.asset";
        private const string WalkPath = RigRoot + "/anim_walk.anim";
        private const string IdlePath = RigRoot + "/anim_idle.anim";
        private const string ControllerPath = RigRoot + "/base_human_controller.controller";
        private const string ProfilePath = RigRoot + "/base_human_profile.asset";

        [MenuItem("UO HD2D/Rig/Build Character Animator (base_human)")]
        public static void Build()
        {
            var walk = AssetDatabase.LoadAssetAtPath<AnimationClip>(WalkPath);
            var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(IdlePath);
            var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(AvatarPath);

            if (walk == null || idle == null)
            {
                Debug.LogError("[UOHD2D] Animator: missing clips (walk=" + (walk != null) + ", idle=" + (idle != null) + ").");
                return;
            }

            if (avatar == null)
                Debug.LogWarning("[UOHD2D] Animator: no humanoid avatar at " + AvatarPath + " - run 'Build Humanoid Avatar' first, else clips won't retarget.");

            // (Re)create the controller from scratch so it's deterministic.
            AssetDatabase.DeleteAsset(ControllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("Moving", AnimatorControllerParameterType.Bool);

            var sm = controller.layers[0].stateMachine;

            var idleState = sm.AddState("Idle");
            idleState.motion = idle;

            var walkState = sm.AddState("Walk");
            walkState.motion = walk;

            sm.defaultState = idleState;

            // Idle -> Walk when Moving becomes true; Walk -> Idle when it goes false.
            // 0.25s crossfades: snappy 0.1s blends read twitchy/"edgy" on this rig.
            var toWalk = idleState.AddTransition(walkState);
            toWalk.AddCondition(AnimatorConditionMode.If, 0f, "Moving");
            toWalk.hasExitTime = false;
            toWalk.duration = 0.25f;

            var toIdle = walkState.AddTransition(idleState);
            toIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "Moving");
            toIdle.hasExitTime = false;
            toIdle.duration = 0.25f;

            EditorUtility.SetDirty(controller);

            // Wire the Animator on the prefab: add if missing, set avatar + controller.
            var prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);

            try
            {
                var animator = prefabRoot.GetComponentInChildren<Animator>();
                if (animator == null)
                    animator = prefabRoot.AddComponent<Animator>();

                animator.runtimeAnimatorController = controller;
                if (avatar != null)
                    animator.avatar = avatar;
                animator.applyRootMotion = false; // movement is driven by the network/tile logic, not the clip

                PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }

            // Point the profile's Controller at the new controller so the factory assigns it.
            var profile = AssetDatabase.LoadAssetAtPath<Game.CharacterProfile>(ProfilePath);
            if (profile != null)
            {
                profile.Controller = controller;
                EditorUtility.SetDirty(profile);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[UOHD2D] Animator: built " + ControllerPath + " (Idle<->Walk on Moving), wired avatar + controller onto prefab, updated profile.");
        }
    }
}
#endif
