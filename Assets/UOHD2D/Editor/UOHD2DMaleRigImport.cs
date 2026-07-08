#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;

using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace UOHD2D
{
    /*
     * Imports the authentic UO male mannequin (Tripo-rigged FBX generated from the classic
     * paperdoll body) and builds every asset the male BodyTemplate needs:
     *
     *   1 Import FBX Set      - copy model/idle/walk/turn FBXs in, configure humanoid import
     *                           (model/walk/turn share the Tripo skeleton; idle is a Mixamo clip
     *                           that retargets through muscle space), extract embedded textures.
     *   2 Build Skin Material - URP/Lit material using the model's own naked-body color atlas.
     *   3 Build Prefab        - wrapper prefab with feet baked to the origin + CharacterRig.
     *   4 Build Animator      - Idle/Walk/Turn controller (Moving bool + Turn trigger).
     *   5 Update Template     - point template_male at the new prefab/avatar/controller/material.
     *
     * Steps are idempotent; "Run All" executes them in order. Unlike the legacy GLB path
     * (UOHD2DHumanoidRig + AvatarBuilder) the FBX ModelImporter produces the humanoid Avatar
     * itself - we only verify its bone map against the authoritative Tripo mapping and force
     * an explicit HumanDescription if the auto-mapper got it wrong.
     */
    public static class UOHD2DMaleRigImport
    {
        private const string SourceDir = @"C:\Users\david\OneDrive\Desktop\models";

        private const string RigRoot = "Assets/UOHD2D/Generated/Rig/base_male";
        private const string ModelPath = RigRoot + "/model.fbx";
        private const string IdlePath = RigRoot + "/idle.fbx";
        private const string WalkPath = RigRoot + "/walk.fbx";
        private const string TurnPath = RigRoot + "/turn.fbx";
        private const string TexturesDir = RigRoot + "/textures";
        private const string SkinMatPath = RigRoot + "/base_male_skin.mat";
        private const string PrefabPath = RigRoot + "/base_male.prefab";
        private const string ControllerPath = RigRoot + "/base_male_controller.controller";
        private const string TemplatePath = "Assets/UOHD2D/Generated/Templates/template_male.asset";

        /*
         * Humanoid mapping: TRUST UNITY'S AUTO-MAPPER (empty HumanDescription). It picks
         * Spine->Waist instead of Spine01 - that's anatomically fine (Waist IS the lower spine
         * bone) - and, critically, it computes its own correct T-pose reference. A hand-built
         * HumanDescription must supply the skeleton rest pose too, and these exports bind with
         * lowered arms; every attempt to enforce a T-pose by hand (bind-pose capture, arm
         * alignment) skewed the muscle reference and splayed the arms in every retargeted clip.
         * Auto-map retargets the Tripo walk/turn and the Mixamo idle cleanly. Verified by
         * measuring hand-vs-hip heights: idle hands 0.90 (at hips), walk mid-swing at hips.
         */

        [MenuItem("UO HD2D/Rig/Male/Run All")]
        public static void RunAll()
        {
            ImportFbxSet();
            BuildSkinMaterial();
            BuildPrefab();
            BuildAnimator();
            UpdateTemplate();
            Debug.Log("[UOHD2D] Male rig: Run All complete.");
        }

        // ------------------------------------------------------------------ 1 import

        [MenuItem("UO HD2D/Rig/Male/1 Import FBX Set")]
        public static void ImportFbxSet()
        {
            CopyIn("model.fbx");
            CopyIn("idle.fbx");
            CopyIn("walk.fbx");
            CopyIn("turn.fbx");
            AssetDatabase.Refresh();

            // Each FBX builds its OWN auto-mapped humanoid avatar: the exports carry slightly
            // different bind poses per file (CopyFromOther reported 20-28 deg rest-pose errors
            // on the Waist), and muscle-space retargeting absorbs per-file avatars cleanly.
            ConfigureModel();
            ConfigureAnimFbx(IdlePath, loop: true);
            ConfigureAnimFbx(WalkPath, loop: true);
            ConfigureAnimFbx(TurnPath, loop: false);

            ExtractModelTextures();
            Debug.Log("[UOHD2D] Male rig: FBX set imported and configured.");
        }

        private static void CopyIn(string file)
        {
            var dst = Path.Combine(RigRoot, file);
            if (File.Exists(dst))
                return;

            var src = Path.Combine(SourceDir, file);
            if (!File.Exists(src))
            {
                Debug.LogError("[UOHD2D] Male rig: source file missing: " + src);
                return;
            }

            Directory.CreateDirectory(RigRoot);
            File.Copy(src, dst);
        }

        private static void ConfigureModel()
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError("[UOHD2D] Male rig: no ModelImporter at " + ModelPath);
                return;
            }

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;          // the pose FBX; clips come from the anim FBXs
            importer.isReadable = true;                // wardrobe tools read UVs + bone weights
            importer.materialImportMode = ModelImporterMaterialImportMode.None; // we build our own skin material
            ResetToAutoMap(importer);
            importer.SaveAndReimport();

            CheckAvatar(ModelPath);
        }

        // Empty human + skeleton arrays = full auto-map with Unity's own T-pose enforcement
        // (see the class comment - hand-built descriptions splayed the arms).
        private static void ResetToAutoMap(ModelImporter importer)
        {
            var desc = importer.humanDescription;
            desc.human = new HumanBone[0];
            desc.skeleton = new SkeletonBone[0];
            importer.humanDescription = desc;
        }

        private static void CheckAvatar(string path)
        {
            var avatar = LoadAvatarAt(path);
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                Debug.LogError("[UOHD2D] Male rig: avatar invalid after import of " + path + " (valid=" +
                               (avatar != null && avatar.isValid) + ", human=" + (avatar != null && avatar.isHuman) + ").");
                return;
            }

            var spine = "?";
            foreach (var hb in avatar.humanDescription.human)
                if (hb.humanName == "Spine")
                    spine = hb.boneName;

            Debug.Log("[UOHD2D] Male rig: humanoid avatar OK for " + path + " (auto-map, Spine->" + spine + ").");
        }

        private static void ConfigureAnimFbx(string path, bool loop)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError("[UOHD2D] Male rig: no ModelImporter at " + path);
                return;
            }

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = null;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            ResetToAutoMap(importer);

            // Root yaw and XZ travel stay in root motion, which the runtime discards
            // (applyRootMotion=false): walk plays in place while tiles scroll, and the turn
            // clip only shuffles the feet while CharacterRig supplies the actual rotation -
            // baking either into the pose would double-apply it.
            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                clip.loopTime = loop;
                clip.lockRootRotation = false;
                clip.keepOriginalOrientation = true;
                clip.lockRootPositionXZ = false;
                clip.lockRootHeightY = true;          // keep hip height in the pose
                clip.keepOriginalPositionY = true;
            }

            importer.clipAnimations = clips;
            importer.SaveAndReimport();

            CheckAvatar(path);
        }

        private static void ExtractModelTextures()
        {
            var abs = Path.GetFullPath(TexturesDir);
            if (Directory.Exists(abs) && Directory.GetFiles(abs).Length > 0)
                return; // already extracted

            Directory.CreateDirectory(abs);
            AssetDatabase.Refresh();

            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer != null && importer.ExtractTextures(TexturesDir))
            {
                AssetDatabase.Refresh();
                Debug.Log("[UOHD2D] Male rig: extracted embedded textures to " + TexturesDir);
            }
            else
            {
                Debug.LogWarning("[UOHD2D] Male rig: texture extraction reported nothing to extract.");
            }
        }

        private static Avatar LoadModelAvatar()
        {
            return LoadAvatarAt(ModelPath);
        }

        private static Avatar LoadAvatarAt(string path)
        {
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
        }

        // ------------------------------------------------------------------ 2 skin material

        [MenuItem("UO HD2D/Rig/Male/2 Build Skin Material")]
        public static void BuildSkinMaterial()
        {
            var color = FindExtractedTexture("Color_");
            var normal = FindExtractedTexture("NormalGL_");

            if (color == null)
                Debug.LogWarning("[UOHD2D] Male rig: no Color_* texture found under " + TexturesDir + " - skin material will be untextured.");

            var mat = AssetDatabase.LoadAssetAtPath<Material>(SkinMatPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, SkinMatPath);
            }

            mat.SetTexture("_BaseMap", color);
            if (normal != null)
            {
                mat.SetTexture("_BumpMap", normal);
                mat.EnableKeyword("_NORMALMAP");
            }
            mat.SetFloat("_Smoothness", 0.1f);
            mat.SetFloat("_Metallic", 0f);

            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            Debug.Log("[UOHD2D] Male rig: skin material at " + SkinMatPath + " (base=" + (color != null ? color.name : "none") + ").");
        }

        private static Texture2D FindExtractedTexture(string namePrefix)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TexturesDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileName(path).StartsWith(namePrefix))
                    return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }

            return null;
        }

        // ------------------------------------------------------------------ 3 prefab

        [MenuItem("UO HD2D/Rig/Male/3 Build Prefab")]
        public static void BuildPrefab()
        {
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (modelAsset == null)
            {
                Debug.LogError("[UOHD2D] Male rig: model not imported yet - run '1 Import FBX Set' first.");
                return;
            }

            // Wrapper prefab: shifting the WHOLE FBX node keeps the humanoid Animator and its
            // avatar skeleton internally consistent (moving nodes between the Animator and the
            // hips would desync them - the old GLB half-buried-hips bug).
            var root = new GameObject("base_male");
            var child = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
            child.transform.SetParent(root.transform, false);
            child.transform.localPosition = Vector3.zero;
            child.transform.localRotation = Quaternion.identity;

            var skinMat = AssetDatabase.LoadAssetAtPath<Material>(SkinMatPath);
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                for (var i = 1; i < renderers.Length; i++)
                    bounds.Encapsulate(renderers[i].bounds);

                // Feet bake: soles at the wrapper origin so runtime scaling keeps them on the tile.
                child.transform.localPosition = new Vector3(0f, -bounds.min.y, 0f);

                if (skinMat != null)
                    foreach (var r in renderers)
                        r.sharedMaterial = skinMat;
            }

            var rig = root.AddComponent<Game.CharacterRig>();
            // Verified visually (4-angle render of the posed rig, 2026-07-07): this model faces
            // +Z at identity, so no offset. The bind pose's foot->toe axis reads -X, but that is
            // the foot bone's local axis convention, not the visual facing - don't trust it.
            rig.ModelYawOffset = 0f;

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            Debug.Log("[UOHD2D] Male rig: prefab at " + PrefabPath + " (feet baked to origin).");
        }

        // ------------------------------------------------------------------ 4 animator

        [MenuItem("UO HD2D/Rig/Male/4 Build Animator")]
        public static void BuildAnimator()
        {
            var idle = LoadFirstClip(IdlePath);
            var walk = LoadFirstClip(WalkPath);
            var turn = LoadFirstClip(TurnPath);

            if (idle == null || walk == null)
            {
                Debug.LogError("[UOHD2D] Male rig: missing clips (idle=" + (idle != null) + ", walk=" + (walk != null) + ").");
                return;
            }

            AssetDatabase.DeleteAsset(ControllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("Moving", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Turn", AnimatorControllerParameterType.Trigger);

            var sm = controller.layers[0].stateMachine;

            var idleState = sm.AddState("Idle");
            idleState.motion = idle;
            sm.defaultState = idleState;

            var walkState = sm.AddState("Walk");
            walkState.motion = walk;

            var toWalk = idleState.AddTransition(walkState);
            toWalk.AddCondition(AnimatorConditionMode.If, 0f, "Moving");
            toWalk.hasExitTime = false;
            toWalk.duration = 0.25f;

            var toIdle = walkState.AddTransition(idleState);
            toIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "Moving");
            toIdle.hasExitTime = false;
            toIdle.duration = 0.25f;

            if (turn != null)
            {
                var turnState = sm.AddState("Turn");
                turnState.motion = turn;

                var idleToTurn = idleState.AddTransition(turnState);
                idleToTurn.AddCondition(AnimatorConditionMode.If, 0f, "Turn");
                idleToTurn.hasExitTime = false;
                idleToTurn.duration = 0.1f;

                // Movement always wins: if Moving flips mid-turn this transition fires
                // immediately, so the walk can never be blocked by the turn state.
                var turnToWalk = turnState.AddTransition(walkState);
                turnToWalk.AddCondition(AnimatorConditionMode.If, 0f, "Moving");
                turnToWalk.hasExitTime = false;
                turnToWalk.duration = 0.15f;
                turnToWalk.interruptionSource = TransitionInterruptionSource.Destination;

                var turnToIdle = turnState.AddTransition(idleState);
                turnToIdle.hasExitTime = true;
                turnToIdle.exitTime = 0.9f;
                turnToIdle.duration = 0.15f;
            }
            else
            {
                Debug.LogWarning("[UOHD2D] Male rig: no turn clip - controller built without a Turn state.");
            }

            EditorUtility.SetDirty(controller);

            // Wire controller + avatar onto the prefab's Animator (lives on the FBX child node).
            var avatar = LoadModelAvatar();
            var prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);

            try
            {
                var animator = prefabRoot.GetComponentInChildren<Animator>();
                if (animator == null)
                    animator = prefabRoot.AddComponent<Animator>();

                animator.runtimeAnimatorController = controller;
                if (avatar != null)
                    animator.avatar = avatar;
                animator.applyRootMotion = false; // tile/network logic owns position

                PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[UOHD2D] Male rig: controller at " + ControllerPath + " (Idle/Walk/Turn) wired onto prefab.");
        }

        private static AnimationClip LoadFirstClip(string path)
        {
            return AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        }

        // ------------------------------------------------------------------ 5 template

        [MenuItem("UO HD2D/Rig/Male/5 Update Template")]
        public static void UpdateTemplate()
        {
            var template = AssetDatabase.LoadAssetAtPath<Game.BodyTemplate>(TemplatePath);
            if (template == null)
            {
                Debug.LogError("[UOHD2D] Male rig: template not found at " + TemplatePath);
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            var skinMat = AssetDatabase.LoadAssetAtPath<Material>(SkinMatPath);
            var avatar = LoadModelAvatar();

            if (prefab == null || controller == null)
            {
                Debug.LogError("[UOHD2D] Male rig: prefab/controller missing - run steps 1-4 first.");
                return;
            }

            template.BodyPrefab = prefab;
            template.Avatar = avatar;
            template.Controller = controller;
            template.SkinMaterial = skinMat;
            template.GroundOffset = 0f; // feet are baked into the prefab; re-tune only if needed

            EditorUtility.SetDirty(template);
            AssetDatabase.SaveAssets();
            Debug.Log("[UOHD2D] Male rig: template_male now uses the new mannequin.");
        }
    }
}
#endif
