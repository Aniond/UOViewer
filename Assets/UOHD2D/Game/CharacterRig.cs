using System.Collections.Generic;
using UnityEngine;

namespace UOHD2D.Game
{
    // Owns a live skinned character: the body SkinnedMeshRenderer + its skeleton, plus
    // any bone-shared armor pieces. Armor is equipped by cloning a piece and rebinding
    // its bones to THIS body's skeleton (matched by bone name), so pieces deform with the
    // same animation - the standard modular-character technique. Everything stays live:
    // add / remove / swap at runtime.
    //
    // Also owns facing: UO movement is 8-directional, so SetFacing turns the whole rig to
    // point at the movement direction (the camera yaw is fixed north, so a world-space yaw
    // maps straight onto the UO direction byte).
    public class CharacterRig : MonoBehaviour
    {
        // The body skin. Its bones[] + rootBone define the skeleton every armor piece shares.
        public SkinnedMeshRenderer Body;

        // Composites 2D clothing layers onto the body's skin atlas (the wardrobe). Resolved in
        // Bind(); ClothingLayer-kind items route here instead of spawning a mesh.
        public ClothingCompositor Compositor;

        // How fast the rig turns to face a new direction while already moving (degrees/sec).
        // Starting from rest snaps instantly instead - blending the walk cycle while still
        // rotating toward the travel direction reads as a false backstep.
        public float TurnSpeed = 1080f;

        // Facing changes from rest at or beyond this angle play the Turn animation (with the
        // rig rotating at IdleTurnSpeed) instead of snapping. Requires a controller with a
        // "Turn" trigger; smaller changes and older controllers snap as before.
        public float TurnAnimThreshold = 90f;

        // Yaw speed while the idle turn animation plays (degrees/sec). Tune to the turn clip
        // so the feet shuffle roughly matches the rotation.
        public float IdleTurnSpeed = 360f;

        // Added to the computed facing yaw. base_char's chest faces -Z (south, toward camera) at
        // identity, so an offset of 180 makes yaw=dir*45+180 face the true travel direction for
        // all 8 UO dirs (verified by measuring the rest pose). Tune per source model.
        public float ModelYawOffset = 180f;

        // Text-to-motion clips hold the hands too high / in front (best available idle parks
        // them at chest-front). This post-pose correction swings each upper arm toward hanging
        // straight down, applied AFTER the Animator writes the pose each frame. Full-ish weight
        // when idle; light while walking so the arm swing survives.
        // Post-pose arm/head corrections were band-aids for a hunched text-to-motion idle.
        // With upright source clips they're off by default; dial up per model only if needed.
        [Range(0f, 1f)] public float ArmHangIdleWeight = 0f;
        [Range(0f, 1f)] public float ArmHangWalkWeight = 0f;
        [Range(-30f, 30f)] public float HeadTiltUp = 0f;

        private Transform _rootBone;
        private readonly Dictionary<string, Transform> _boneByName = new Dictionary<string, Transform>();
        private readonly Dictionary<ArmorSlot, GameObject> _equipped = new Dictionary<ArmorSlot, GameObject>();

        private Animator _animator;
        private static readonly int MovingHash = Animator.StringToHash("Moving");
        private static readonly int TurnHash = Animator.StringToHash("Turn");

        private Quaternion _targetYaw;
        private bool _bound;
        private bool _lastMoving;
        private bool _hasTurnParam;
        private bool _idleTurning;

        private void Awake()
        {
            Bind();
        }

        // Discover the body skin + index the skeleton by bone name. Safe to call again after
        // the body is swapped. Robust to import layout: we find the SMR in children rather
        // than assuming a node path.
        public void Bind()
        {
            if (Body == null)
                Body = GetComponentInChildren<SkinnedMeshRenderer>();

            _boneByName.Clear();
            _bound = false;

            if (Body == null)
            {
                Debug.LogWarning("[UOHD2D] CharacterRig on " + name + " has no SkinnedMeshRenderer to bind.");
                return;
            }

            _rootBone = Body.rootBone;

            // Index every bone the body skins to.
            foreach (var b in Body.bones)
            {
                if (b != null && !_boneByName.ContainsKey(b.name))
                    _boneByName[b.name] = b;
            }

            // Also index the whole skeleton hierarchy under the root, so armor bones the body
            // mesh happens not to weight (e.g. extra twist bones) still resolve.
            if (_rootBone != null)
            {
                foreach (var t in _rootBone.GetComponentsInChildren<Transform>(true))
                    if (!_boneByName.ContainsKey(t.name))
                        _boneByName[t.name] = t;
            }

            _animator = GetComponentInChildren<Animator>();

            if (Compositor == null)
                Compositor = GetComponent<ClothingCompositor>();

            // Only controllers that define a "Turn" trigger get the idle turn animation; the
            // legacy Idle/Walk controllers (female, stand-ins) keep snapping silently.
            _hasTurnParam = false;
            if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                foreach (var p in _animator.parameters)
                {
                    if (p.type == AnimatorControllerParameterType.Trigger && p.nameHash == TurnHash)
                    {
                        _hasTurnParam = true;
                        break;
                    }
                }
            }

            _targetYaw = transform.rotation;
            _bound = true;
        }

        // Drive the locomotion state (Idle <-> Walk). Only pushes the param on change so we
        // don't thrash the animator every frame.
        public void SetMoving(bool moving)
        {
            if (moving == _lastMoving)
                return;

            _lastMoving = moving;

            // Movement interrupts a pending idle turn: the first stride must already point at
            // the travel direction (no backstep), and the Turn trigger must not fire later.
            if (moving && _idleTurning)
            {
                transform.rotation = _targetYaw;
                _idleTurning = false;

                if (_animator != null && _hasTurnParam)
                    _animator.ResetTrigger(TurnHash);
            }

            if (_animator != null && _animator.runtimeAnimatorController != null)
                _animator.SetBool(MovingHash, moving);
        }

        // DEPRECATED for worn clothing: clothing/armor is now 2D texture layers composited by
        // ClothingCompositor. Retained for legacy skinned pieces authored against the shared
        // skeleton; no catalog path routes here anymore.
        //
        // Equip a bone-shared armor piece. piece is a prefab/instance whose SkinnedMeshRenderer
        // was authored against a skeleton with the same bone names as this body. Returns the
        // spawned instance (or null on failure). Any existing piece in the slot is removed first.
        public GameObject EquipArmor(SkinnedMeshRenderer piece, ArmorSlot slot)
        {
            if (!_bound)
                Bind();

            if (Body == null || piece == null)
                return null;

            UnequipArmor(slot);

            var inst = Instantiate(piece.gameObject, transform);
            inst.name = piece.name + "_" + slot;
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;
            inst.transform.localScale = Vector3.one;

            var smr = inst.GetComponent<SkinnedMeshRenderer>();
            if (smr == null)
                smr = inst.GetComponentInChildren<SkinnedMeshRenderer>();

            if (smr == null)
            {
                Debug.LogWarning("[UOHD2D] EquipArmor: piece " + piece.name + " has no SkinnedMeshRenderer.");
                Destroy(inst);
                return null;
            }

            // Rebind the piece's bones to the body's identically-named transforms. Any bone we
            // can't match falls back to the root so the mesh stays attached rather than exploding.
            var src = smr.bones;
            var dst = new Transform[src.Length];
            var unmatched = 0;

            for (var i = 0; i < src.Length; i++)
            {
                var boneName = src[i] != null ? src[i].name : null;

                if (boneName != null && _boneByName.TryGetValue(boneName, out var mapped))
                {
                    dst[i] = mapped;
                }
                else
                {
                    dst[i] = _rootBone;
                    unmatched++;
                }
            }

            smr.bones = dst;
            smr.rootBone = _rootBone;
            smr.localBounds = Body.localBounds;   // share the body's bounds so the piece isn't frustum-culled mid-animation
            SetLayerRecursive(inst, gameObject.layer);

            if (unmatched > 0)
                Debug.LogWarning("[UOHD2D] EquipArmor " + slot + " (" + piece.name + "): " + unmatched + "/" + src.Length + " bones did not match the body skeleton by name - piece may deform incorrectly.");

            _equipped[slot] = inst;
            return inst;
        }

        // Equip a RIGID prop (helmet, weapon, shield, hat, backpack) by parenting it to a named
        // body bone so it rides along with the animation. Unlike EquipArmor this needs no shared
        // skeleton - the piece is any GameObject/mesh. localOffset/localEuler/scale position it on
        // the bone. Returns the spawned instance, or null if the bone isn't found.
        public GameObject EquipProp(GameObject piecePrefab, string boneName, ArmorSlot slot,
            Vector3 localOffset, Vector3 localEuler, float scale = 1f)
        {
            if (!_bound)
                Bind();

            if (piecePrefab == null)
                return null;

            if (!_boneByName.TryGetValue(boneName, out var bone) || bone == null)
            {
                Debug.LogWarning("[UOHD2D] EquipProp: bone '" + boneName + "' not found on " + name + ".");
                return null;
            }

            UnequipArmor(slot);

            var inst = Instantiate(piecePrefab, bone);
            inst.name = piecePrefab.name + "_" + slot;
            inst.transform.localPosition = localOffset;
            inst.transform.localRotation = Quaternion.Euler(localEuler);
            inst.transform.localScale = Vector3.one * scale;
            SetLayerRecursive(inst, gameObject.layer);

            _equipped[slot] = inst;
            return inst;
        }

        // Equip an item from its saved fit definition - the one-call path for the catalog.
        // Clothing-kind items paint onto the body atlas (no GameObject); props spawn a mesh.
        public GameObject EquipItem(EquippableItem item)
        {
            if (item == null)
                return null;

            if (item.Kind == EquippableItem.EquipKind.ClothingLayer)
            {
                if (!_bound)
                    Bind();

                if (Compositor != null && item.ClothingTexture != null)
                    Compositor.SetLayer(item.UoLayer, item.ClothingTexture, item.ClothingTint);

                return null;
            }

            if (item.Prefab == null)
                return null;

            return EquipProp(item.Prefab, item.BoneName, item.Slot, item.LocalOffset, item.LocalEuler, item.Scale);
        }

        // Apply the server's worn-gear list: map each UO item id via the catalog, then paint
        // clothing-kind items onto the body atlas and spawn prop-kind items on their bones.
        // Anything the character wore but the server no longer reports is removed - prop slots
        // are cleared and stale clothing layers are dropped from the composite. Unmapped ids
        // are logged so it's clear which gear still needs art.
        public void ApplyEquipment(IEnumerable<Network.EquipEntry> equipment, GearCatalog catalog)
        {
            if (catalog == null)
                return;

            var filled = new HashSet<ArmorSlot>();
            var wornLayers = new HashSet<byte>();

            if (equipment != null)
            {
                foreach (var e in equipment)
                {
                    var item = catalog.Resolve(e.ItemId);

                    if (item != null && item.Kind == EquippableItem.EquipKind.ClothingLayer)
                    {
                        // Clothing keys on the server's layer byte, not an ArmorSlot - layers
                        // like Waist that collapse to no slot still paint and stack correctly.
                        if (Compositor != null && item.ClothingTexture != null)
                        {
                            Compositor.SetLayer(e.Layer, item.ClothingTexture, item.ClothingTint);
                            wornLayers.Add(e.Layer);
                        }

                        continue;
                    }

                    if (!UOLayer.ToSlot(e.Layer, out var slot))
                        continue; // a layer we don't render (rings, hair, ...)

                    if (item == null)
                    {
                        Debug.Log("[UOHD2D] no gear art for UO item 0x" + e.ItemId.ToString("X4") + " (layer 0x" + e.Layer.ToString("X2") + ") - slot " + slot + " left empty.");
                        continue;
                    }

                    EquipItem(item);
                    filled.Add(item.Slot);
                }
            }

            // Remove only what the SERVER previously equipped and no longer reports. Locally
            // equipped default gear is left alone (the server never knew about it).
            _clearScratch.Clear();
            foreach (var slot in _serverSlots)
                if (!filled.Contains(slot))
                    _clearScratch.Add(slot);

            foreach (var slot in _clearScratch)
                UnequipArmor(slot);

            if (Compositor != null)
                foreach (var layer in _serverClothingLayers)
                    if (!wornLayers.Contains(layer))
                        Compositor.ClearLayer(layer);

            _serverSlots.Clear();
            _serverSlots.UnionWith(filled);
            _serverClothingLayers.Clear();
            _serverClothingLayers.UnionWith(wornLayers);
        }

        private readonly List<ArmorSlot> _clearScratch = new List<ArmorSlot>();

        // What the SERVER currently has on us (clothing layers + prop slots). Server sync may
        // only remove its own contributions - DefaultGear stays on unless the server explicitly
        // covers the same layer/slot.
        private readonly HashSet<byte> _serverClothingLayers = new HashSet<byte>();
        private readonly HashSet<ArmorSlot> _serverSlots = new HashSet<ArmorSlot>();

        public void UnequipArmor(ArmorSlot slot)
        {
            if (_equipped.TryGetValue(slot, out var inst) && inst != null)
                Destroy(inst);

            _equipped.Remove(slot);
        }

        public bool IsEquipped(ArmorSlot slot)
        {
            return _equipped.TryGetValue(slot, out var inst) && inst != null;
        }

        // Turn the rig to face a UO direction (0=N .. 7=NW clockwise). Camera yaw is fixed
        // north, so world yaw = direction * 45 degrees. Called every step / when facing changes.
        public void SetFacing(byte uoDirection)
        {
            // Solved from the model's measured rest-facing (chest points -Z / south at identity):
            // yaw = dir*45 + 180 faces the travel direction for all 8 UO directions.
            var yaw = (uoDirection & 0x07) * 45f + ModelYawOffset;
            var target = Quaternion.Euler(0f, yaw, 0f);

            // Called every frame with the current direction - only a facing CHANGE may start
            // a turn, else the trigger would re-fire while the rig is still rotating.
            if (target == _targetYaw)
                return;

            _targetYaw = target;

            if (_lastMoving)
                return;

            // From rest: a big turn plays the turn animation while the rig rotates; a small
            // adjustment (or a controller without a Turn state) snaps immediately - the first
            // walk stride must already point where we're going, or it reads as a backstep.
            var delta = Quaternion.Angle(transform.rotation, _targetYaw);

            if (_hasTurnParam && delta >= TurnAnimThreshold)
            {
                _idleTurning = true;
                _animator.SetTrigger(TurnHash);
            }
            else
            {
                transform.rotation = _targetYaw;
            }
        }

        private void Update()
        {
            // Smoothly rotate toward the target facing so turns read rather than snap-pop.
            // Idle turns move at the (slower) animation-matched speed.
            if (transform.rotation != _targetYaw)
            {
                var speed = _idleTurning ? IdleTurnSpeed : TurnSpeed;
                transform.rotation = Quaternion.RotateTowards(transform.rotation, _targetYaw, speed * Time.deltaTime);
            }
            else if (_idleTurning)
            {
                _idleTurning = false;
            }
        }

        private void LateUpdate()
        {
            // Runs after the Animator has written the pose for this frame.
            ApplyArmHang();
            ApplyHeadTilt();
        }

        // Lift the chin by HeadTiltUp degrees around the rig's right axis. Public so tooling
        // that drives the Animator manually can apply it per pose.
        public void ApplyHeadTilt()
        {
            if (!_bound || Mathf.Abs(HeadTiltUp) < 0.01f)
                return;

            if (_boneByName.TryGetValue("Head", out var head))
                head.rotation = Quaternion.AngleAxis(-HeadTiltUp, transform.right) * head.rotation;
        }

        // Swing each upper arm toward pointing straight down by the configured weight. Public so
        // tooling that drives the Animator manually (Animator.Update) can apply it per pose.
        public void ApplyArmHang()
        {
            if (!_bound)
                return;

            var weight = _lastMoving ? ArmHangWalkWeight : ArmHangIdleWeight;
            if (weight <= 0f)
                return;

            LowerArm("L_Upperarm", "L_Hand", weight);
            LowerArm("R_Upperarm", "R_Hand", weight);
        }

        private void LowerArm(string upperArmBone, string handBone, float weight)
        {
            if (!_boneByName.TryGetValue(upperArmBone, out var upper) || !_boneByName.TryGetValue(handBone, out var hand))
                return;

            var armDir = hand.position - upper.position;
            if (armDir.sqrMagnitude < 0.0001f)
                return;

            var toDown = Quaternion.FromToRotation(armDir.normalized, Vector3.down);
            upper.rotation = Quaternion.Slerp(Quaternion.identity, toDown, weight) * upper.rotation;
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;

            foreach (Transform child in go.transform)
                SetLayerRecursive(child.gameObject, layer);
        }
    }
}
