#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;

using UnityEditor;
using UnityEngine;

namespace UOHD2D
{
    /*
     * Tooling for the 2D wardrobe (clothing painted onto the male mannequin's UV atlas):
     *
     *   Export UV Authoring Reference - the base color atlas with the mesh's UV wireframe drawn
     *   over it. This PNG is the canvas template every clothing layer is painted against
     *   (by hand or AI) - paint opaque cloth where it covers the body, leave the rest transparent.
     *
     *   Build POC Clothing - proves the composite pipeline end to end without hand-made art:
     *   generates a tunic and pants by filling the UV triangles whose skin weights belong to the
     *   torso / leg bones, shaded by the underlying skin luminance so the baked lighting reads
     *   through. Creates the matching EquippableItem assets (Kind=ClothingLayer), adds them to
     *   base_char_profile's default gear, and maps the classic UO tunic/pants item ids in the
     *   gear catalog so the server-driven path exercises the compositor too.
     *
     *   Build Appearance Options - builds the CharacterAppearanceOptions catalog the creation
     *   wizard reads: bakes two POC hair layers (scalp bands of the head-dominated UV region),
     *   fills the skin/hair/cloth color lists, points the starting tunic/pants at the POC
     *   clothing art, and assigns the asset to the scene's GameBootstrap.
     */
    public static class UOHD2DWardrobeTools
    {
        private const string ModelPath = "Assets/UOHD2D/Generated/Rig/base_male/model.fbx";
        private const string SkinMatPath = "Assets/UOHD2D/Generated/Rig/base_male/base_male_skin.mat";
        private const string WardrobeRoot = "Assets/UOHD2D/Generated/Wardrobe";
        private const string UvRefPath = WardrobeRoot + "/base_male_uv_reference.png";
        private const string ProfilePath = "Assets/UOHD2D/Generated/Rig/base_char/base_char_profile.asset";
        private const string OptionsPath = WardrobeRoot + "/appearance_options.asset";
        private const string CatalogPath = "Assets/UOHD2D/Generated/Gear/gear_catalog.asset";

        // Matched by prefix: the mesh's dominant weights sit on TWIST bones (L_ThighTwist01,
        // R_CalfTwist02, ...), so "L_Thigh" must catch its twist chain too. The pelvis is
        // dominated by "Waist" (never "Hip").
        private static readonly string[] TunicBones = { "Spine01", "Spine02" };
        private static readonly string[] PantsBones = { "Waist", "L_Thigh", "R_Thigh", "L_Calf", "R_Calf" };
        private static readonly string[] ArmsBones = { "L_Upperarm", "R_Upperarm", "L_Forearm", "R_Forearm" };
        private static readonly string[] GloveBones = { "L_Hand", "R_Hand" };
        private static readonly string[] GorgetBones = { "NeckTwist" };
        private static readonly string[] BootBones = { "L_Foot", "R_Foot", "L_ToeBase", "R_ToeBase" };

        [MenuItem("UO HD2D/Wardrobe/Export UV Authoring Reference")]
        public static void ExportUvReference()
        {
            var smr = LoadBodySkin();
            if (smr == null)
                return;

            var basePixels = ReadTexturePixels(LoadBaseSkinTexture(), out var w, out var h);
            if (basePixels == null)
                return;

            var mesh = smr.sharedMesh;
            var uv = mesh.uv;
            var tris = mesh.triangles;
            var wire = new Color(0f, 0.9f, 0.9f, 1f);

            for (var i = 0; i < tris.Length; i += 3)
            {
                DrawUvLine(basePixels, w, h, uv[tris[i]], uv[tris[i + 1]], wire);
                DrawUvLine(basePixels, w, h, uv[tris[i + 1]], uv[tris[i + 2]], wire);
                DrawUvLine(basePixels, w, h, uv[tris[i + 2]], uv[tris[i]], wire);
            }

            SavePng(basePixels, w, h, UvRefPath);
            Debug.Log("[UOHD2D] Wardrobe: UV authoring reference at " + UvRefPath + " (" + w + "x" + h + ").");
        }

        [MenuItem("UO HD2D/Wardrobe/Build POC Clothing")]
        public static void BuildPocClothing()
        {
            var smr = LoadBodySkin();
            if (smr == null)
                return;

            var basePixels = ReadTexturePixels(LoadBaseSkinTexture(), out var w, out var h);
            if (basePixels == null)
                return;

            var tunicPath = WardrobeRoot + "/poc_tunic.png";
            var pantsPath = WardrobeRoot + "/poc_pants.png";

            BakeBoneRegionLayer(smr, basePixels, w, h, TunicBones, tunicPath);
            BakeBoneRegionLayer(smr, basePixels, w, h, PantsBones, pantsPath);

            var tunicTex = AssetDatabase.LoadAssetAtPath<Texture2D>(tunicPath);
            var pantsTex = AssetDatabase.LoadAssetAtPath<Texture2D>(pantsPath);

            // UO hues: a plain red tunic over blue pants - unmistakable in a capture.
            var tunicItem = UpsertClothingItem("poc_tunic", "POC Tunic", tunicTex,
                Game.UOLayer.MiddleTorso, new Color(0.75f, 0.18f, 0.15f, 1f));
            var pantsItem = UpsertClothingItem("poc_pants", "POC Pants", pantsTex,
                Game.UOLayer.Pants, new Color(0.2f, 0.3f, 0.65f, 1f));

            AddToDefaultGear(tunicItem);
            AddToDefaultGear(pantsItem);

            MapCatalogId(0x1FA1, tunicItem);  // tunic
            MapCatalogId(0x152E, pantsItem);  // short pants
            MapCatalogId(0x1539, pantsItem);  // long pants

            AssetDatabase.SaveAssets();
            Debug.Log("[UOHD2D] Wardrobe: POC tunic + pants built, added to default gear and gear catalog.");
        }

        // The Ranger kit (and any studded/leather armor the server hands out): bakes a bone-
        // region layer per armor piece and maps every UO graphic id that shares the art.
        // Server hues (e.g. ranger green 68) tint at composite time via the catalog hue table.
        [MenuItem("UO HD2D/Wardrobe/Build Studded Armor Set")]
        public static void BuildStuddedArmorSet()
        {
            var smr = LoadBodySkin();
            if (smr == null)
                return;

            var basePixels = ReadTexturePixels(LoadBaseSkinTexture(), out var w, out var h);
            if (basePixels == null)
                return;

            var leather = new Color(0.42f, 0.33f, 0.22f, 1f); // authored tint when the server sends no hue

            var pieces = new[]
            {
                new { slug = "studded_chest",  name = "Studded Chest",  bones = TunicBones,  layer = Game.UOLayer.InnerTorso },
                new { slug = "studded_legs",   name = "Studded Legs",   bones = PantsBones,  layer = Game.UOLayer.Pants },
                new { slug = "studded_arms",   name = "Studded Arms",   bones = ArmsBones,   layer = Game.UOLayer.Arms },
                new { slug = "studded_gloves", name = "Studded Gloves", bones = GloveBones,  layer = Game.UOLayer.Gloves },
                new { slug = "studded_gorget", name = "Studded Gorget", bones = GorgetBones, layer = Game.UOLayer.Neck },
                new { slug = "boots",          name = "Boots",          bones = BootBones,   layer = Game.UOLayer.Shoes },
            };

            var items = new System.Collections.Generic.Dictionary<string, Game.EquippableItem>();

            foreach (var p in pieces)
            {
                var path = WardrobeRoot + "/" + p.slug + ".png";
                BakeBoneRegionLayer(smr, basePixels, w, h, p.bones, path);
                items[p.slug] = UpsertClothingItem(p.slug, p.name,
                    AssetDatabase.LoadAssetAtPath<Texture2D>(path), p.layer, leather);
            }

            // Studded armor graphics (also worn by the AOS "Ranger set" artifacts).
            MapCatalogId(0x13DB, items["studded_chest"]);
            MapCatalogId(0x13DA, items["studded_legs"]);
            MapCatalogId(0x13DC, items["studded_arms"]);
            MapCatalogId(0x13D5, items["studded_gloves"]);
            MapCatalogId(0x13D6, items["studded_gorget"]);

            // Footwear graphics that all render as the baked boots layer for now.
            MapCatalogId(0x170B, items["boots"]);   // boots
            MapCatalogId(0x170F, items["boots"]);   // shoes
            MapCatalogId(0x170D, items["boots"]);   // sandals
            MapCatalogId(0x1711, items["boots"]);   // thigh boots

            AssetDatabase.SaveAssets();
            Debug.Log("[UOHD2D] Wardrobe: studded armor set baked (chest/legs/arms/gloves/gorget/boots) and mapped in the gear catalog.");
        }

        [MenuItem("UO HD2D/Wardrobe/Build Appearance Options")]
        public static void BuildAppearanceOptions()
        {
            var smr = LoadBodySkin();
            if (smr == null)
                return;

            var basePixels = ReadTexturePixels(LoadBaseSkinTexture(), out var w, out var h);
            if (basePixels == null)
                return;

            // POC hair: scalp bands of the head-dominated region. "Short" is the crown cap only;
            // "Long" adds the back of the head down toward the nape (face stays bare).
            var shortPath = WardrobeRoot + "/poc_hair_short.png";
            var longPath = WardrobeRoot + "/poc_hair_long.png";
            BakeHairLayer(smr, basePixels, w, h, 0.62f, 1f, shortPath);
            BakeHairLayer(smr, basePixels, w, h, 0.62f, 0.2f, longPath);

            var tunicTex = AssetDatabase.LoadAssetAtPath<Texture2D>(WardrobeRoot + "/poc_tunic.png");
            var pantsTex = AssetDatabase.LoadAssetAtPath<Texture2D>(WardrobeRoot + "/poc_pants.png");
            if (tunicTex == null || pantsTex == null)
                Debug.LogWarning("[UOHD2D] Wardrobe: POC tunic/pants art missing - run 'Build POC Clothing' first; the wizard's clothing toggles will be no-ops until then.");

            var options = AssetDatabase.LoadAssetAtPath<Game.CharacterAppearanceOptions>(OptionsPath);
            if (options == null)
            {
                options = ScriptableObject.CreateInstance<Game.CharacterAppearanceOptions>();
                EnsureWardrobeFolder();
                AssetDatabase.CreateAsset(options, OptionsPath);
            }

            // UoHue = the authentic UO hue number sent at character creation (the server clips
            // to skin 1002-1058 / hair 1102-1149 / cloth 2-1001); Color = how we render it.
            options.SkinTones = new[]
            {
                Named("Pale", new Color(1f, 0.94f, 0.88f), 1002),
                Named("Fair", Color.white, 1009),
                Named("Tan", new Color(0.87f, 0.72f, 0.55f), 1023),
                Named("Bronze", new Color(0.72f, 0.53f, 0.38f), 1040),
                Named("Dark", new Color(0.45f, 0.32f, 0.24f), 1058),
            };

            options.HairStyles = new[]
            {
                new Game.CharacterAppearanceOptions.HairStyle { Name = "Short", Texture = AssetDatabase.LoadAssetAtPath<Texture2D>(shortPath), UoItemId = 0x203B },
                new Game.CharacterAppearanceOptions.HairStyle { Name = "Long", Texture = AssetDatabase.LoadAssetAtPath<Texture2D>(longPath), UoItemId = 0x203C },
            };

            options.HairColors = new[]
            {
                Named("Black", new Color(0.13f, 0.11f, 0.10f), 1102),
                Named("Brown", new Color(0.35f, 0.22f, 0.12f), 1114),
                Named("Blonde", new Color(0.85f, 0.70f, 0.38f), 1117),
                Named("Red", new Color(0.55f, 0.20f, 0.10f), 1140),
                Named("Gray", new Color(0.62f, 0.62f, 0.62f), 1147),
            };

            options.ClothHues = new[]
            {
                Named("Red", new Color(0.75f, 0.18f, 0.15f), 33),
                Named("Blue", new Color(0.2f, 0.3f, 0.65f), 99),
                Named("Green", new Color(0.2f, 0.5f, 0.25f), 68),
                Named("Purple", new Color(0.45f, 0.25f, 0.55f), 16),
                Named("Brown", new Color(0.45f, 0.33f, 0.2f), 743),
                Named("Undyed", new Color(0.85f, 0.82f, 0.75f), 1001),
            };

            options.TunicTexture = tunicTex;
            options.PantsTexture = pantsTex;
            EditorUtility.SetDirty(options);

            WireServerCreatedGear(options, shortPath, longPath);
            AssetDatabase.SaveAssets();

            var boot = Object.FindFirstObjectByType<Game.GameBootstrap>(FindObjectsInactive.Include);
            if (boot != null && boot.AppearanceOptions != options)
            {
                boot.AppearanceOptions = options;
                EditorUtility.SetDirty(boot);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(boot.gameObject.scene);
            }

            Debug.Log("[UOHD2D] Wardrobe: appearance options built at " + OptionsPath
                + (boot != null ? " and assigned to GameBootstrap." : " - no GameBootstrap in the open scene, assign it manually."));
        }

        private static Game.CharacterAppearanceOptions.NamedColor Named(string name, Color color, ushort uoHue)
        {
            return new Game.CharacterAppearanceOptions.NamedColor { Name = name, Color = color, UoHue = uoHue };
        }

        // Server-created characters wear REAL items (ServUO CharacterCreation: a random
        // shirt/fancy shirt/doublet, long/short pants, shoes, plus the hair layer). Map every
        // id the server can hand out to our art, and teach the catalog the render color of
        // each hue we offer, so a server-dressed spawn matches the wizard preview.
        private static void WireServerCreatedGear(Game.CharacterAppearanceOptions options, string shortHairPath, string longHairPath)
        {
            var tunicItem = AssetDatabase.LoadAssetAtPath<Game.EquippableItem>(WardrobeRoot + "/poc_tunic.asset");
            var pantsItem = AssetDatabase.LoadAssetAtPath<Game.EquippableItem>(WardrobeRoot + "/poc_pants.asset");

            MapCatalogId(0x1517, tunicItem); // shirt
            MapCatalogId(0x1EFD, tunicItem); // fancy shirt
            MapCatalogId(0x1F7B, tunicItem); // doublet
            MapCatalogId(0x1539, pantsItem); // long pants
            MapCatalogId(0x152E, pantsItem); // short pants

            var shortHair = UpsertClothingItem("poc_hair_short", "Short Hair",
                AssetDatabase.LoadAssetAtPath<Texture2D>(shortHairPath), Game.UOLayer.Hair, Color.white);
            var longHair = UpsertClothingItem("poc_hair_long", "Long Hair",
                AssetDatabase.LoadAssetAtPath<Texture2D>(longHairPath), Game.UOLayer.Hair, Color.white);

            MapCatalogId(0x203B, shortHair);
            MapCatalogId(0x203C, longHair);

            var catalog = AssetDatabase.LoadAssetAtPath<Game.GearCatalog>(CatalogPath);
            if (catalog == null)
                return;

            catalog.Hues.Clear();
            AddHues(catalog, options.SkinTones);
            AddHues(catalog, options.HairColors);
            AddHues(catalog, options.ClothHues);
            catalog.Invalidate();
            EditorUtility.SetDirty(catalog);
        }

        private static void AddHues(Game.GearCatalog catalog, Game.CharacterAppearanceOptions.NamedColor[] list)
        {
            if (list == null)
                return;

            foreach (var c in list)
                if (c.UoHue != 0 && !catalog.Hues.Exists(h => h.UoHue == c.UoHue))
                    catalog.Hues.Add(new Game.GearCatalog.HueEntry { UoHue = c.UoHue, Color = c.Color });
        }

        // ------------------------------------------------------------------ baking

        // Fill every UV triangle whose three vertices are all dominated by one of the given
        // bones. The cloth is a light gray shaded by the underlying skin luminance (so the
        // baked lighting survives); the runtime tint supplies the actual hue.
        private static void BakeBoneRegionLayer(SkinnedMeshRenderer smr, Color[] basePixels, int w, int h,
            string[] boneNames, string outPath)
        {
            var mesh = smr.sharedMesh;
            var uv = mesh.uv;
            var tris = mesh.triangles;
            var weights = mesh.boneWeights;
            var bones = smr.bones;

            var targetIndices = new HashSet<int>();
            for (var i = 0; i < bones.Length; i++)
                if (bones[i] != null && boneNames.Any(p => bones[i].name.StartsWith(p)))
                    targetIndices.Add(i);

            if (targetIndices.Count == 0)
            {
                Debug.LogWarning("[UOHD2D] Wardrobe: none of [" + string.Join(", ", boneNames) + "] found in the body skeleton.");
                return;
            }

            var inRegion = new bool[weights.Length];
            for (var i = 0; i < weights.Length; i++)
                inRegion[i] = targetIndices.Contains(DominantBone(weights[i]));

            var pixels = new Color[w * h]; // transparent black

            for (var i = 0; i < tris.Length; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                if (!inRegion[a] || !inRegion[b] || !inRegion[c])
                    continue;

                FillUvTriangle(pixels, basePixels, w, h, uv[a], uv[b], uv[c]);
            }

            SavePng(pixels, w, h, outPath);
        }

        // Like BakeBoneRegionLayer for the head bones, but only the scalp: vertices above
        // crownFrac of the head's height span, plus - for longer styles - the band down to
        // napeFrac restricted to the back half of the head (behind the head centroid), so the
        // face stays bare. Positions are measured in root space via the SMR node's matrix
        // (the mesh's own axes are NOT Y-up on this Tripo import). NOTE: the BIND pose faces
        // -X in root space even though the ANIMATED rig faces +Z - the bake samples bind-pose
        // vertices, so "behind the face" is the +X side here (verified against the baked mask
        // 2026-07-07: the -X half contains the face's UV islands - eye/nose/lips visible).
        private static void BakeHairLayer(SkinnedMeshRenderer smr, Color[] basePixels, int w, int h,
            float crownFrac, float napeFrac, string outPath)
        {
            var mesh = smr.sharedMesh;
            var uv = mesh.uv;
            var tris = mesh.triangles;
            var weights = mesh.boneWeights;
            var verts = mesh.vertices;
            var bones = smr.bones;
            var toRoot = smr.transform.localToWorldMatrix;

            var headIndices = new HashSet<int>();
            for (var i = 0; i < bones.Length; i++)
                if (bones[i] != null && bones[i].name.StartsWith("Head"))
                    headIndices.Add(i);

            if (headIndices.Count == 0)
            {
                Debug.LogWarning("[UOHD2D] Wardrobe: no Head bone found in the body skeleton - hair not baked.");
                return;
            }

            var inRegion = new bool[weights.Length];
            var pos = new Vector3[weights.Length];
            float minY = float.MaxValue, maxY = float.MinValue;
            var centroid = Vector3.zero;
            var headCount = 0;

            for (var i = 0; i < weights.Length; i++)
            {
                inRegion[i] = headIndices.Contains(DominantBone(weights[i]));
                if (!inRegion[i])
                    continue;

                pos[i] = toRoot.MultiplyPoint3x4(verts[i]);
                minY = Mathf.Min(minY, pos[i].y);
                maxY = Mathf.Max(maxY, pos[i].y);
                centroid += pos[i];
                headCount++;
            }

            centroid /= headCount;
            var span = Mathf.Max(maxY - minY, 0.0001f);

            var included = new bool[weights.Length];
            for (var i = 0; i < weights.Length; i++)
            {
                if (!inRegion[i])
                    continue;

                var frac = (pos[i].y - minY) / span;
                included[i] = frac >= crownFrac || (frac >= napeFrac && pos[i].x >= centroid.x);
            }

            var pixels = new Color[w * h];

            for (var i = 0; i < tris.Length; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                if (!included[a] || !included[b] || !included[c])
                    continue;

                FillUvTriangle(pixels, basePixels, w, h, uv[a], uv[b], uv[c]);
            }

            SavePng(pixels, w, h, outPath);
        }

        private static int DominantBone(BoneWeight bw)
        {
            var index = bw.boneIndex0;
            var weight = bw.weight0;

            if (bw.weight1 > weight) { index = bw.boneIndex1; weight = bw.weight1; }
            if (bw.weight2 > weight) { index = bw.boneIndex2; weight = bw.weight2; }
            if (bw.weight3 > weight) { index = bw.boneIndex3; }

            return index;
        }

        private static void FillUvTriangle(Color[] pixels, Color[] basePixels, int w, int h,
            Vector2 uvA, Vector2 uvB, Vector2 uvC)
        {
            var a = new Vector2(uvA.x * (w - 1), uvA.y * (h - 1));
            var b = new Vector2(uvB.x * (w - 1), uvB.y * (h - 1));
            var c = new Vector2(uvC.x * (w - 1), uvC.y * (h - 1));

            var minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, b.x, c.x)), 0, w - 1);
            var maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, b.x, c.x)), 0, w - 1);
            var minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, b.y, c.y)), 0, h - 1);
            var maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, b.y, c.y)), 0, h - 1);

            var area = Cross(b - a, c - a);
            if (Mathf.Abs(area) < 0.0001f)
                return;

            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    var w0 = Cross(b - p, c - p) / area;
                    var w1 = Cross(c - p, a - p) / area;
                    var w2 = 1f - w0 - w1;

                    if (w0 < -0.001f || w1 < -0.001f || w2 < -0.001f)
                        continue;

                    var skin = basePixels[y * w + x];
                    var lum = 0.299f * skin.r + 0.587f * skin.g + 0.114f * skin.b;
                    var shade = 0.45f + 0.55f * lum;
                    pixels[y * w + x] = new Color(shade, shade, shade, 1f);
                }
            }
        }

        private static float Cross(Vector2 lhs, Vector2 rhs)
        {
            return lhs.x * rhs.y - lhs.y * rhs.x;
        }

        private static void DrawUvLine(Color[] pixels, int w, int h, Vector2 uvA, Vector2 uvB, Color color)
        {
            var x0 = Mathf.Clamp(Mathf.RoundToInt(uvA.x * (w - 1)), 0, w - 1);
            var y0 = Mathf.Clamp(Mathf.RoundToInt(uvA.y * (h - 1)), 0, h - 1);
            var x1 = Mathf.Clamp(Mathf.RoundToInt(uvB.x * (w - 1)), 0, w - 1);
            var y1 = Mathf.Clamp(Mathf.RoundToInt(uvB.y * (h - 1)), 0, h - 1);

            var dx = Mathf.Abs(x1 - x0);
            var dy = -Mathf.Abs(y1 - y0);
            var sx = x0 < x1 ? 1 : -1;
            var sy = y0 < y1 ? 1 : -1;
            var err = dx + dy;

            while (true)
            {
                pixels[y0 * w + x0] = color;

                if (x0 == x1 && y0 == y1)
                    break;

                var e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        // ------------------------------------------------------------------ assets

        private static Game.EquippableItem UpsertClothingItem(string slug, string displayName,
            Texture2D tex, byte uoLayer, Color tint)
        {
            var path = WardrobeRoot + "/" + slug + ".asset";
            var item = AssetDatabase.LoadAssetAtPath<Game.EquippableItem>(path);

            if (item == null)
            {
                item = ScriptableObject.CreateInstance<Game.EquippableItem>();
                EnsureWardrobeFolder();
                AssetDatabase.CreateAsset(item, path);
            }

            item.DisplayName = displayName;
            item.Kind = Game.EquippableItem.EquipKind.ClothingLayer;
            item.ClothingTexture = tex;
            item.ClothingTint = tint;
            item.UoLayer = uoLayer;
            EditorUtility.SetDirty(item);

            return item;
        }

        private static void AddToDefaultGear(Game.EquippableItem item)
        {
            if (item == null)
                return;

            var profile = AssetDatabase.LoadAssetAtPath<Game.CharacterProfile>(ProfilePath);
            if (profile == null)
            {
                Debug.LogWarning("[UOHD2D] Wardrobe: profile not found at " + ProfilePath);
                return;
            }

            if (!profile.DefaultGear.Contains(item))
            {
                profile.DefaultGear.Add(item);
                EditorUtility.SetDirty(profile);
            }
        }

        private static void MapCatalogId(int uoItemId, Game.EquippableItem item)
        {
            if (item == null)
                return;

            var catalog = AssetDatabase.LoadAssetAtPath<Game.GearCatalog>(CatalogPath);
            if (catalog == null)
            {
                Debug.LogWarning("[UOHD2D] Wardrobe: gear catalog not found at " + CatalogPath);
                return;
            }

            foreach (var e in catalog.Entries)
                if (e.UoItemId == uoItemId)
                    return;

            catalog.Entries.Add(new Game.GearCatalog.Entry { UoItemId = uoItemId, Item = item });
            catalog.Invalidate();
            EditorUtility.SetDirty(catalog);
        }

        // ------------------------------------------------------------------ shared

        private static SkinnedMeshRenderer LoadBodySkin()
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var smr = go != null ? go.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;

            if (smr == null || smr.sharedMesh == null)
                Debug.LogError("[UOHD2D] Wardrobe: body skin not found at " + ModelPath + " - run the male rig import first.");

            return smr;
        }

        private static Texture2D LoadBaseSkinTexture()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(SkinMatPath);
            var tex = mat != null ? mat.GetTexture("_BaseMap") as Texture2D : null;

            if (tex == null)
                Debug.LogError("[UOHD2D] Wardrobe: no base skin texture on " + SkinMatPath + " - run '2 Build Skin Material' first.");

            return tex;
        }

        // Reads pixels regardless of the texture's Read/Write setting by round-tripping
        // through a temporary RenderTexture.
        private static Color[] ReadTexturePixels(Texture2D tex, out int w, out int h)
        {
            w = h = 0;

            if (tex == null)
                return null;

            w = tex.width;
            h = tex.height;

            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;

            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;

            var readable = new Texture2D(w, h, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            readable.Apply();

            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            var pixels = readable.GetPixels();
            Object.DestroyImmediate(readable);
            return pixels;
        }

        private static void SavePng(Color[] pixels, int w, int h, string assetPath)
        {
            EnsureWardrobeFolder();

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels(pixels);
            tex.Apply();

            File.WriteAllBytes(Path.GetFullPath(assetPath), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(assetPath);
        }

        private static void EnsureWardrobeFolder()
        {
            if (!AssetDatabase.IsValidFolder(WardrobeRoot))
                AssetDatabase.CreateFolder("Assets/UOHD2D/Generated", "Wardrobe");
        }
    }
}
#endif
