using System.Collections.Generic;
using UnityEngine;

namespace UOHD2D.Game
{
    /*
     * One character (player or NPC). Built on a BodyTemplate (male/female) so the skeleton and
     * proportions are shared - individuals differ only by an optional skin/face texture and their
     * default gear. Equipment fitted to the template fits this character automatically.
     *
     * The legacy direct fields (RigPrefab/Avatar/Controller) are a fallback for characters authored
     * before templates; when Template is set it wins.
     */
    [CreateAssetMenu(menuName = "UO HD2D/Character Profile")]
    public class CharacterProfile : ScriptableObject
    {
        [Header("Template (preferred)")]
        [Tooltip("Body template this character is built on. When set, it supplies the body/avatar/controller.")]
        public BodyTemplate Template;

        [Tooltip("Optional skin/face texture override applied to the body's base map. Null = template default.")]
        public Texture2D SkinTexture;

        [Tooltip("Gear equipped on spawn.")]
        public List<EquippableItem> DefaultGear = new List<EquippableItem>();

        [Header("Legacy direct body (fallback when Template is null)")]
        public GameObject RigPrefab;
        public RuntimeAnimatorController Controller;
        public Avatar Avatar;
        public float TargetHeight = 1.8f;
        public float GroundOffset = 0f;

        // Resolved accessors: prefer the template, fall back to the direct fields.
        public GameObject ResolvedPrefab => Template != null ? Template.BodyPrefab : RigPrefab;
        public RuntimeAnimatorController ResolvedController => Template != null ? Template.Controller : Controller;
        public Avatar ResolvedAvatar => Template != null ? Template.Avatar : Avatar;
        public float ResolvedHeight => Template != null ? Template.TargetHeight : TargetHeight;
        public float ResolvedGroundOffset => Template != null ? Template.GroundOffset : GroundOffset;
    }
}
