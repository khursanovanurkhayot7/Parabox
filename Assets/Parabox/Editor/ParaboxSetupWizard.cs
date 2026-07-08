using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Parabox.EditorTools
{
    // ONE-TIME setup wizard (URP / WebGL, polished sprite look).
    // Run "Tools > Parabox > Create Everything (Run Once)".
    //
    // It sets up URP, generates rounded-rectangle sprites procedurally, builds
    // sprite tile prefabs, 5 level prefabs, the MainMenu and Game scenes and adds
    // them to Build Settings. Safe to run again — it regenerates everything.
    //
    // After running it you can delete the Assets/Parabox/Editor folder.
    // DO NOT delete Assets/Parabox/Scripts — the game needs it!
    public static class ParaboxSetupWizard
    {
        const string Root = "Assets/Parabox";
        const string SpriteDir = Root + "/Sprites";
        const string PrefabDir = Root + "/Prefabs";
        const string LevelDir = Root + "/Prefabs/Levels";
        const string SceneDir = Root + "/Scenes";
        const string SettingsDir = Root + "/Settings";

        // ---------------------------------------------------------- palette
        static readonly Color BG = Hex("0B0E17");
        static readonly Color Accent = Hex("FF9E5E");
        static readonly Color DimText = Hex("8A92B5");
        static readonly Color ButtonCol = Hex("2A3050");

        // Room floor colors, indexed by room id. Bright & saturated like the real game.
        static readonly Color[] RoomColors =
        {
            Hex("2E6FD8"), // 0 blue   (main rooms)
            Hex("C43F82"), // 1 magenta (interiors)
            Hex("E0912F"), // 2 orange
            Hex("3FA06A"), // 3 green
            Hex("7E57D8"), // 4 purple
        };

        static readonly Color BoxColor = Hex("F2A03E");
        static readonly Color PlayerColor = Hex("FF5FA0");
        static readonly Color EyeColor = Hex("241019");
        static readonly Color WallColor = Hex("141A28");
        static readonly Color ShadowCol = new Color(0f, 0f, 0f, 0.30f);

        // Sorting orders.
        const int OrderBackingInBox = -50;
        const int OrderFloor = 0;
        const int OrderBorder = 1;
        const int OrderGoal = 2;
        const int OrderWall = 4;
        const int OrderBox = 6;
        const int OrderPlayer = 8;
        const int OrderFrameInBox = 50;

        const int Res = 256; // sprite resolution (also pixels-per-unit → 1 sprite = 1 world unit)

        class Sprites { public Sprite fill, tile, grid, ringThin, ringThick, shaded, shadow; }
        class Tiles { public GameObject floor, grid, border, wall, box, metaBox, player, boxGoal, playerGoal; }

        [MenuItem("Tools/Parabox/Create Everything (Run Once)")]
        public static void CreateEverything()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CleanGenerated();
            CreateFolders();

            SetupUrpPipeline();

            var spr = CreateSprites();
            var tiles = CreateTilePrefabs(spr);
            var levels = CreateLevelPrefabs(tiles);
            string gamePath = CreateGameScene(spr, tiles, levels);
            string menuPath = CreateMainMenuScene(spr, levels.Length);

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(menuPath, true),
                new EditorBuildSettingsScene(gamePath, true)
            };

            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(menuPath);

            EditorUtility.DisplayDialog("Parabox",
                "Setup complete!\n\n" +
                "URP assigned, rounded sprites generated, tile prefabs, 5 level\n" +
                "prefabs, MainMenu + Game scenes created and added to Build Settings.\n\n" +
                "You can now DELETE the folder Assets/Parabox/Editor.\n" +
                "Keep Assets/Parabox/Scripts — the game needs it!",
                "OK");
        }

        // ================================================= URP pipeline
        static void SetupUrpPipeline()
        {
            string rendererPath = SettingsDir + "/Parabox_URP_Renderer.asset";
            string pipelinePath = SettingsDir + "/Parabox_URP.asset";

            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                TryReloadResources(rendererData);
                AssetDatabase.CreateAsset(rendererData, rendererPath);
            }

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(pipeline, pipelinePath);
            }

            GraphicsSettings.defaultRenderPipeline = pipeline;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(current, false);
            AssetDatabase.SaveAssets();
        }

        static void TryReloadResources(Object target)
        {
            try
            {
                var type = System.Type.GetType(
                    "UnityEngine.Rendering.ResourceReloader, Unity.RenderPipelines.Core.Editor");
                var method = type?.GetMethod("ReloadAllNullIn",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                method?.Invoke(null, new object[] { target, "Packages/com.unity.render-pipelines.universal" });
            }
            catch { /* URP fills most of this in automatically */ }
        }

        // ================================================= folders / cleanup
        static void CleanGenerated()
        {
            AssetDatabase.DeleteAsset(SpriteDir);
            AssetDatabase.DeleteAsset(PrefabDir);
            AssetDatabase.DeleteAsset(SceneDir);
            AssetDatabase.DeleteAsset(SettingsDir);
            AssetDatabase.DeleteAsset(Root + "/Materials"); // leftover from the first HDRP run
        }

        static void CreateFolders()
        {
            EnsureFolder(Root);
            EnsureFolder(SpriteDir);
            EnsureFolder(PrefabDir);
            EnsureFolder(LevelDir);
            EnsureFolder(SceneDir);
            EnsureFolder(SettingsDir);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int i = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, i));
            AssetDatabase.CreateFolder(path.Substring(0, i), path.Substring(i + 1));
        }

        // ================================================= procedural sprites
        static Sprites CreateSprites()
        {
            return new Sprites
            {
                // filled rounded square — boxes, player (sliced border for the floor base)
                fill = MakeSprite("Fill", 0.22f, 0f, Mathf.RoundToInt(0.22f * Res) + 10, true),
                // barely-rounded square — walls (solid blocks that fill their cell)
                tile = MakeSprite("Tile", 0.11f, 0f, 0, false),
                // hairline grid, tiled over the flat floor (thin lines on cell edges)
                grid = MakeGridSprite(),
                // thin rounded ring — room border (sliced to room size)
                ringThin = MakeSprite("RingThin", 0.18f, 0.05f, Mathf.RoundToInt(0.23f * Res) + 8, true),
                // thick rounded ring — goals and meta-box frame (uniform scale)
                ringThick = MakeSprite("RingThick", 0.24f, 0.13f, 0, false),
                // top-lit gradient rounded square — boxes & player look rounded / 3D
                shaded = MakeShadedSprite("Shaded", 0.22f, 1.0f, 0.72f),
                // soft dark blob — drop shadow under boxes & player
                shadow = MakeShadowSprite(),
            };
        }

        static Sprite MakeSprite(string name, float radiusFrac, float ringFrac, int borderPx, bool sliced)
        {
            var tex = RoundedTex(Res, radiusFrac, ringFrac);
            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);

            string assetPath = SpriteDir + "/" + name + ".png";
            string absPath = Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length));
            File.WriteAllBytes(absPath, png);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

            var imp = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.spritePixelsPerUnit = Res;
            imp.mipmapEnabled = false;
            imp.filterMode = FilterMode.Bilinear;
            imp.alphaIsTransparency = true;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.wrapMode = TextureWrapMode.Clamp;

            var settings = new TextureImporterSettings();
            imp.ReadTextureSettings(settings);
            settings.spriteBorder = sliced ? new Vector4(borderPx, borderPx, borderPx, borderPx) : Vector4.zero;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteExtrude = 0;
            imp.SetTextureSettings(settings);
            imp.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        // Signed-distance rounded rect, white with an anti-aliased alpha edge.
        // ringFrac <= 0 => filled; otherwise a ring of that thickness (fraction of size).
        static Texture2D RoundedTex(int s, float radiusFrac, float ringFrac)
        {
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            float c = s * 0.5f;
            float half = s * 0.5f - 2f;
            float r = Mathf.Min(radiusFrac * s, half);
            float ring = ringFrac * s;

            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float dx = Mathf.Abs(x + 0.5f - c) - (half - r);
                    float dy = Mathf.Abs(y + 0.5f - c) - (half - r);
                    float outside = Mathf.Sqrt(Mathf.Max(dx, 0f) * Mathf.Max(dx, 0f) +
                                               Mathf.Max(dy, 0f) * Mathf.Max(dy, 0f));
                    float d = outside + Mathf.Min(Mathf.Max(dx, dy), 0f) - r; // <0 inside

                    float a = Mathf.Clamp01(0.5f - d);
                    if (ring > 0f) a *= Mathf.Clamp01(0.5f + (d + ring)); // subtract inner disc
                    px[y * s + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        // A 1-cell sprite with thin opaque lines on its left and bottom edges. Tiled over
        // the floor it draws a hairline grid at every cell boundary — no per-cell "plates".
        static Sprite MakeGridSprite()
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat };
            int lw = Mathf.Max(2, Mathf.RoundToInt(0.03f * s));
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                    px[y * s + x] = (x < lw || y < lw)
                        ? new Color32(255, 255, 255, 255)
                        : new Color32(255, 255, 255, 0);
            tex.SetPixels32(px);
            tex.Apply();

            string assetPath = SpriteDir + "/Grid.png";
            string absPath = Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length));
            File.WriteAllBytes(absPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

            var imp = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.spritePixelsPerUnit = Res;
            imp.mipmapEnabled = false;
            imp.filterMode = FilterMode.Point;
            imp.alphaIsTransparency = true;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.wrapMode = TextureWrapMode.Repeat;

            var settings = new TextureImporterSettings();
            imp.ReadTextureSettings(settings);
            settings.spriteBorder = Vector4.zero;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            imp.SetTextureSettings(settings);
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        // Rounded square with a top-to-bottom brightness gradient baked in (white top,
        // grey bottom). Tinted by the object's color it reads as a soft, rounded 3D piece.
        static Sprite MakeShadedSprite(string name, float radiusFrac, float topMul, float botMul)
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            float c = s * 0.5f, half = s * 0.5f - 2f, r = Mathf.Min(radiusFrac * s, half);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = Mathf.Abs(x + 0.5f - c) - (half - r);
                    float dy = Mathf.Abs(y + 0.5f - c) - (half - r);
                    float outside = Mathf.Sqrt(Mathf.Max(dx, 0f) * Mathf.Max(dx, 0f) +
                                               Mathf.Max(dy, 0f) * Mathf.Max(dy, 0f));
                    float d = outside + Mathf.Min(Mathf.Max(dx, dy), 0f) - r;
                    float a = Mathf.Clamp01(0.5f - d);
                    float m = Mathf.Lerp(botMul, topMul, y / (s - 1f)); // y=0 bottom, y=s-1 top
                    byte v = (byte)Mathf.RoundToInt(Mathf.Clamp01(m) * 255f);
                    px[y * s + x] = new Color32(v, v, v, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return SaveTexAsSprite(tex, name, false, 0, FilterMode.Bilinear);
        }

        // Soft dark blob for a drop shadow (black, feathered edges).
        static Sprite MakeShadowSprite()
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            float c = s * 0.5f, half = s * 0.5f - 2f, r = Mathf.Min(0.30f * s, half);
            float feather = 0.16f * s;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = Mathf.Abs(x + 0.5f - c) - (half - r);
                    float dy = Mathf.Abs(y + 0.5f - c) - (half - r);
                    float outside = Mathf.Sqrt(Mathf.Max(dx, 0f) * Mathf.Max(dx, 0f) +
                                               Mathf.Max(dy, 0f) * Mathf.Max(dy, 0f));
                    float d = outside + Mathf.Min(Mathf.Max(dx, dy), 0f) - r;
                    float a = Mathf.Clamp01(0.5f - d / feather);
                    px[y * s + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return SaveTexAsSprite(tex, "Shadow", false, 0, FilterMode.Bilinear);
        }

        // Shared texture -> Sprite importer used by the generators above.
        static Sprite SaveTexAsSprite(Texture2D tex, string name, bool sliced, int borderPx, FilterMode filter)
        {
            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            string assetPath = SpriteDir + "/" + name + ".png";
            string absPath = Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length));
            File.WriteAllBytes(absPath, png);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

            var imp = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.spritePixelsPerUnit = Res;
            imp.mipmapEnabled = false;
            imp.filterMode = filter;
            imp.alphaIsTransparency = true;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.wrapMode = TextureWrapMode.Clamp;

            var settings = new TextureImporterSettings();
            imp.ReadTextureSettings(settings);
            settings.spriteBorder = sliced ? new Vector4(borderPx, borderPx, borderPx, borderPx) : Vector4.zero;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteExtrude = 0;
            imp.SetTextureSettings(settings);
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        // ================================================= tile prefabs
        static Tiles CreateTilePrefabs(Sprites s)
        {
            var t = new Tiles();

            // Floor — sliced sprite sized to the room at runtime.
            var floor = new GameObject("Floor");
            var fsr = floor.AddComponent<SpriteRenderer>();
            fsr.sprite = s.fill; fsr.color = RoomColors[0];
            fsr.drawMode = SpriteDrawMode.Sliced; fsr.size = Vector2.one; fsr.sortingOrder = OrderFloor;
            t.floor = SavePrefab(floor, PrefabDir + "/Floor.prefab");

            // Grid — a hairline overlay tiled over the flat floor at runtime. Gives the
            // room a subtle grid WITHOUT a rounded plate under every object.
            var grid = new GameObject("Grid");
            var gsr = grid.AddComponent<SpriteRenderer>();
            gsr.sprite = s.grid; gsr.color = new Color(0f, 0f, 0f, 0.12f);
            gsr.drawMode = SpriteDrawMode.Tiled; gsr.size = Vector2.one; gsr.sortingOrder = OrderFloor + 1;
            t.grid = SavePrefab(grid, PrefabDir + "/Grid.prefab");

            // Room border — thin ring, sliced to the room.
            var border = new GameObject("Border");
            var bsr = border.AddComponent<SpriteRenderer>();
            bsr.sprite = s.ringThin; bsr.color = Color.white;
            bsr.drawMode = SpriteDrawMode.Sliced; bsr.size = Vector2.one; bsr.sortingOrder = OrderBorder;
            t.border = SavePrefab(border, PrefabDir + "/Border.prefab");

            // Wall — dark block that fills its cell (reads as a solid barrier).
            var wall = new GameObject("Wall");
            SpriteChild("Sprite", wall.transform, s.tile, WallColor, OrderWall, Vector3.zero, V(1.0f));
            t.wall = SavePrefab(wall, PrefabDir + "/Wall.prefab");

            // Box — a soft drop shadow behind a top-lit rounded square (reads as 3D).
            var box = new GameObject("Box");
            box.AddComponent<EntityView>();
            SpriteChild("Shadow", box.transform, s.shadow, ShadowCol, OrderBox - 1, new Vector3(0f, -0.06f, 0f), V(1.0f));
            SpriteChild("Sprite", box.transform, s.shaded, BoxColor, OrderBox, Vector3.zero, V(0.92f));
            t.box = SavePrefab(box, PrefabDir + "/Box.prefab");

            // MetaBox — dark backing + colored frame. Interior room is nested at runtime;
            // a SortingGroup isolates the contents so nesting sorts correctly.
            var meta = new GameObject("MetaBox");
            meta.AddComponent<EntityView>();
            meta.AddComponent<SortingGroup>().sortingOrder = OrderBox;
            SpriteChild("Shadow", meta.transform, s.shadow, ShadowCol, OrderBackingInBox - 10, new Vector3(0f, -0.06f, 0f), V(1.0f));
            SpriteChild("Backing", meta.transform, s.shaded, Darken(RoomColors[1], 0.5f), OrderBackingInBox, Vector3.zero, V(0.9f));
            SpriteChild("Frame", meta.transform, s.ringThick, Lighten(RoomColors[1], 0.3f), OrderFrameInBox, Vector3.zero, V(0.9f));
            t.metaBox = SavePrefab(meta, PrefabDir + "/MetaBox.prefab");

            // Player — a shadow behind a top-lit rounded box with two eyes; eyes blink.
            var player = new GameObject("Player");
            player.AddComponent<EntityView>();
            SpriteChild("Shadow", player.transform, s.shadow, ShadowCol, OrderPlayer - 1, new Vector3(0f, -0.06f, 0f), V(0.98f));
            SpriteChild("Body", player.transform, s.shaded, PlayerColor, OrderPlayer, Vector3.zero, V(0.88f));
            var eyeL = SpriteChild("EyeL", player.transform, s.fill, EyeColor, OrderPlayer + 1, new Vector3(-0.17f, 0.07f, 0f), new Vector3(0.14f, 0.21f, 1f));
            var eyeR = SpriteChild("EyeR", player.transform, s.fill, EyeColor, OrderPlayer + 1, new Vector3(0.17f, 0.07f, 0f), new Vector3(0.14f, 0.21f, 1f));
            var blink = player.AddComponent<Blinker>();
            blink.eyeL = eyeL.transform;
            blink.eyeR = eyeR.transform;
            t.player = SavePrefab(player, PrefabDir + "/Player.prefab");

            // Goals — thick rounded outline in the target's color.
            var boxGoal = new GameObject("BoxGoal");
            SpriteChild("Ring", boxGoal.transform, s.ringThick, BoxColor, OrderGoal, Vector3.zero, V(0.72f));
            t.boxGoal = SavePrefab(boxGoal, PrefabDir + "/BoxGoal.prefab");

            var playerGoal = new GameObject("PlayerGoal");
            SpriteChild("Ring", playerGoal.transform, s.ringThick, PlayerColor, OrderGoal, Vector3.zero, V(0.72f));
            t.playerGoal = SavePrefab(playerGoal, PrefabDir + "/PlayerGoal.prefab");

            return t;
        }

        static GameObject SpriteChild(string name, Transform parent, Sprite sprite, Color color,
                                      int order, Vector3 localPos, Vector3 scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            return go;
        }

        static Vector3 V(float f) => new Vector3(f, f, 1f);

        static GameObject SavePrefab(GameObject go, string path)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        // ================================================= level definitions
        // Rooms are borderless — the grid edge IS the boundary (no perimeter walls).
        //   # wall   . floor   P player   b box
        //   1..9 meta-box containing that room id
        //   x box goal   p player goal
        class LevelDef { public string name; public string[][] rooms; }

        static LevelDef[] Levels() => new[]
        {
            new LevelDef { name = "First Steps", rooms = new[] { new[]
            {
                ".....",
                "..P..",
                "..b..",
                "..x..",
                ".p...",
            }}},
            new LevelDef { name = "Chain Reaction", rooms = new[] { new[]
            {
                "......",
                "Pbb.xx",
                "......",
                "..p...",
            }}},
            new LevelDef { name = "Think Inside the Box", rooms = new[]
            {
                new[]
                {
                    ".....",
                    "Pb..1",
                    ".....",
                },
                new[]
                {
                    "...",
                    ".x.",
                    "...",
                },
            }},
            new LevelDef { name = "Breaking Out", rooms = new[]
            {
                new[]
                {
                    ".....",
                    ".1.x.",
                    ".....",
                },
                new[]
                {
                    "...",
                    "Pb.",
                    "...",
                },
            }},
            new LevelDef { name = "Home Inside", rooms = new[]
            {
                new[]
                {
                    "......",
                    "Pb.x.1",
                    "......",
                },
                new[]
                {
                    "...",
                    ".p.",
                    "...",
                },
            }},
            // ---- levels below are new; all verified solvable by a BFS solver ----
            new LevelDef { name = "Detour", rooms = new[] { new[]   // introduces walls (7)
            {
                "..x..",
                "..#..",
                ".b...",
                ".....",
                "..P..",
            }}},
            new LevelDef { name = "Housemates", rooms = new[]       // box + you both inside (8)
            {
                new[]
                {
                    ".....",
                    "Pb..1",
                    ".....",
                },
                new[]
                {
                    "...",
                    "xp.",
                    "...",
                },
            }},
            new LevelDef { name = "Box in a Box", rooms = new[]     // 2-deep nesting (5)
            {
                new[]
                {
                    ".....",
                    "Pb..1",
                    ".....",
                },
                new[]
                {
                    "...",
                    ".2#",
                    "...",
                },
                new[]
                {
                    "...",
                    ".x.",
                    "...",
                },
            }},
            new LevelDef { name = "Deep Dive", rooms = new[]        // you travel two rooms deep (7)
            {
                new[]
                {
                    "P...1",
                    ".....",
                    ".....",
                },
                new[]
                {
                    "..2",
                    "...",
                    "...",
                },
                new[]
                {
                    "...",
                    "p..",
                    "...",
                },
            }},
            new LevelDef { name = "Twin Rooms", rooms = new[]       // one box into each room (9)
            {
                new[]
                {
                    ".......",
                    "1.bPb.2",
                    ".......",
                },
                new[]
                {
                    "...",
                    ".x.",
                    "...",
                },
                new[]
                {
                    "...",
                    ".x.",
                    "...",
                },
            }},
        };

        // ================================================= level prefabs
        static GameObject[] CreateLevelPrefabs(Tiles t)
        {
            var defs = Levels();
            var result = new GameObject[defs.Length];
            for (int i = 0; i < defs.Length; i++)
                result[i] = BuildLevelPrefab(i, defs[i], t);
            return result;
        }

        static GameObject BuildLevelPrefab(int index, LevelDef def, Tiles t)
        {
            var root = new GameObject("Level_" + (index + 1));
            root.AddComponent<ParaboxLevel>().levelName = def.name;

            float offsetX = 0f;
            for (int r = 0; r < def.rooms.Length; r++)
            {
                string[] rows = def.rooms[r];
                int h = rows.Length;
                int w = rows[0].Length;

                var roomGO = new GameObject("Room_" + r);
                roomGO.transform.SetParent(root.transform, false);
                roomGO.transform.localPosition = new Vector3(offsetX + w * 0.5f, 0f, 0f);
                offsetX += w + 2f;

                var rm = roomGO.AddComponent<RoomMarker>();
                rm.roomId = r; rm.width = w; rm.height = h;

                Color floorC = RoomColors[r % RoomColors.Length];

                var floor = (GameObject)PrefabUtility.InstantiatePrefab(t.floor, roomGO.transform);
                floor.transform.localPosition = Vector3.zero;
                var fsr = floor.GetComponent<SpriteRenderer>();
                fsr.size = new Vector2(w, h); fsr.color = floorC;

                var border = (GameObject)PrefabUtility.InstantiatePrefab(t.border, roomGO.transform);
                border.transform.localPosition = Vector3.zero;
                var bsr = border.GetComponent<SpriteRenderer>();
                bsr.size = new Vector2(w, h); bsr.color = Lighten(floorC, 0.32f);

                for (int row = 0; row < h; row++)
                {
                    for (int col = 0; col < rows[row].Length; col++)
                    {
                        char ch = rows[row][col];
                        if (ch == '.') continue;

                        int x = col;
                        int y = h - 1 - row; // top row of the string = highest y
                        var pos = new Vector3(x - (w - 1) * 0.5f, y - (h - 1) * 0.5f, 0f);

                        if (ch == '#')
                        {
                            var go = Place(t.wall, roomGO.transform, pos);
                            var mk = go.AddComponent<WallMarker>(); mk.x = x; mk.y = y;
                        }
                        else if (ch == 'b')
                        {
                            var go = Place(t.box, roomGO.transform, pos);
                            var mk = go.AddComponent<BoxMarker>(); mk.x = x; mk.y = y; mk.containsRoomId = -1;
                        }
                        else if (ch >= '1' && ch <= '9')
                        {
                            var go = Place(t.metaBox, roomGO.transform, pos);
                            var mk = go.AddComponent<BoxMarker>(); mk.x = x; mk.y = y; mk.containsRoomId = ch - '0';
                            Color inC = RoomColors[(ch - '0') % RoomColors.Length];
                            var back = go.transform.Find("Backing"); if (back) back.GetComponent<SpriteRenderer>().color = Darken(inC, 0.5f);
                            var frame = go.transform.Find("Frame"); if (frame) frame.GetComponent<SpriteRenderer>().color = Lighten(inC, 0.3f);
                        }
                        else if (ch == 'P')
                        {
                            var go = Place(t.player, roomGO.transform, pos);
                            var mk = go.AddComponent<PlayerMarker>(); mk.x = x; mk.y = y;
                        }
                        else if (ch == 'x')
                        {
                            var go = Place(t.boxGoal, roomGO.transform, pos);
                            var mk = go.AddComponent<GoalMarker>(); mk.x = x; mk.y = y; mk.forPlayer = false;
                        }
                        else if (ch == 'p')
                        {
                            var go = Place(t.playerGoal, roomGO.transform, pos);
                            var mk = go.AddComponent<GoalMarker>(); mk.x = x; mk.y = y; mk.forPlayer = true;
                        }
                    }
                }
            }

            return SavePrefab(root, LevelDir + "/Level_" + (index + 1) + ".prefab");
        }

        static GameObject Place(GameObject prefab, Transform parent, Vector3 pos)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.localPosition = pos;
            return go;
        }

        // ================================================= scenes
        static string CreateGameScene(Sprites spr, Tiles t, GameObject[] levels)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cam = CreateCamera(true);
            var follow = cam.gameObject.AddComponent<CameraFollow>();
            CreateEventSystem();

            var canvas = CreateCanvas("HUDCanvas");

            var levelLabel = MakeText(canvas, "LevelLabel", "Level 1", 30, Color.white,
                new Vector2(24, -24), new Vector2(800, 40), FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1));

            var movesLabel = MakeText(canvas, "MovesLabel", "Moves: 0", 22, DimText,
                new Vector2(24, -64), new Vector2(400, 30), FontStyle.Normal, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1));

            MakeText(canvas, "Hint", "WASD / Arrows — move    Z — undo    R — restart    M — mute    Esc — menu",
                20, DimText, new Vector2(0, 22), new Vector2(1400, 30), FontStyle.Normal, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0));

            // win panel
            var panelGO = new GameObject("WinPanel", typeof(RectTransform));
            panelGO.transform.SetParent(canvas, false);
            var panelRT = (RectTransform)panelGO.transform;
            panelRT.anchorMin = Vector2.zero; panelRT.anchorMax = Vector2.one; panelRT.sizeDelta = Vector2.zero;
            panelGO.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

            var windowRT = MakeRect(panelGO.transform, "Window", Vector2.zero, new Vector2(560, 340));
            var windowImg = windowRT.gameObject.AddComponent<Image>();
            windowImg.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            windowImg.type = Image.Type.Sliced;
            windowImg.color = Hex("1C2130");
            windowRT.gameObject.AddComponent<UIPopIn>(); // pops in when the win panel shows

            var winTitle = MakeText(windowRT, "WinTitle", "Level Complete!", 42, Accent,
                new Vector2(0, 104), new Vector2(520, 56), FontStyle.Bold);
            var winStats = MakeText(windowRT, "WinStats", "Solved in 0 moves", 22, Color.white,
                new Vector2(0, 52), new Vector2(520, 56));
            MakeText(windowRT, "WinHint", "press Space to continue", 18, DimText,
                new Vector2(0, 14), new Vector2(520, 28));
            var nextBtn = MakeButton(windowRT, "NextButton", "Next Level", new Vector2(-115, -62), new Vector2(210, 64), 26, RoomColors[3]);
            var menuBtn = MakeButton(windowRT, "MenuButton", "Menu", new Vector2(115, -62), new Vector2(210, 64), 26, ButtonCol);

            panelGO.SetActive(false);

            var gm = new GameObject("GameManager").AddComponent<GameManager>();
            gm.levelPrefabs = levels;
            gm.floorPrefab = t.floor;
            gm.borderPrefab = t.border;
            gm.wallPrefab = t.wall;
            gm.boxPrefab = t.box;
            gm.metaBoxPrefab = t.metaBox;
            gm.playerPrefab = t.player;
            gm.boxGoalPrefab = t.boxGoal;
            gm.playerGoalPrefab = t.playerGoal;
            gm.gridPrefab = t.grid;
            gm.roomColors = RoomColors;
            gm.boxColor = BoxColor;
            gm.playerColor = PlayerColor;
            gm.pieceSprite = spr.fill;
            gm.ringSprite = spr.ringThick;
            gm.cameraFollow = follow;
            gm.levelLabel = levelLabel;
            gm.movesLabel = movesLabel;
            gm.winPanel = panelGO;
            gm.winTitle = winTitle;
            gm.winStats = winStats;
            gm.nextButton = nextBtn;
            gm.menuButton = menuBtn;

            CreateFade(canvas);

            string path = SceneDir + "/Game.unity";
            EditorSceneManager.SaveScene(scene, path);
            return path;
        }

        static string CreateMainMenuScene(Sprites spr, int levelCount)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateCamera(false);
            CreateEventSystem();

            var canvas = CreateCanvas("MenuCanvas");
            var ui = canvas.gameObject.AddComponent<MainMenuUI>();

            // Mascot: the player box (with eyes) nudging an orange box — a little scene
            // that gently bobs.
            var mascot = MakeRect(canvas, "Mascot", new Vector2(0, 372), new Vector2(400, 120));
            mascot.gameObject.AddComponent<Bobber>();
            UIImage(mascot, "MascotBox", spr.fill, BoxColor, new Vector2(70, 0), new Vector2(76, 76));
            UIImage(mascot, "MascotBody", spr.fill, PlayerColor, new Vector2(-40, 0), new Vector2(86, 86));
            UIImage(mascot, "MascotEyeL", spr.fill, EyeColor, new Vector2(-53, 8), new Vector2(12, 19));
            UIImage(mascot, "MascotEyeR", spr.fill, EyeColor, new Vector2(-27, 8), new Vector2(12, 19));

            MakeText(canvas, "Title", "PARABOX", 96, Accent,
                new Vector2(0, 250), new Vector2(1000, 120), FontStyle.Bold);
            MakeText(canvas, "Subtitle", "a recursive puzzle game — inspired by Patrick's Parabox", 26, DimText,
                new Vector2(0, 168), new Vector2(1200, 40));

            ui.playButton = MakeButton(canvas, "PlayButton", "PLAY", new Vector2(0, 66), new Vector2(340, 78), 34, RoomColors[0]);

            MakeText(canvas, "SelectLabel", "SELECT LEVEL", 22, DimText,
                new Vector2(0, -34), new Vector2(600, 30));

            // Level buttons in rows of 5, each colored by the room palette, with a
            // (hidden-by-default) "beaten" checkmark badge.
            ui.levelButtons = new Button[levelCount];
            ui.levelChecks = new GameObject[levelCount];
            const int perRow = 5;
            const float gap = 108f;
            for (int i = 0; i < levelCount; i++)
            {
                int row = i / perRow;
                int col = i % perRow;
                int inThisRow = Mathf.Min(perRow, levelCount - row * perRow);
                float startX = -(inThisRow - 1) * gap * 0.5f;
                float y = -100f - row * 80f;
                ui.levelButtons[i] = MakeButton(canvas, "LevelButton" + (i + 1), (i + 1).ToString(),
                    new Vector2(startX + col * gap, y), new Vector2(92, 66), 28, RoomColors[i % RoomColors.Length]);
                ui.levelChecks[i] = MakeCheck(ui.levelButtons[i].transform, spr);
            }

            int rows = (levelCount + perRow - 1) / perRow;
            float quitY = -100f - rows * 80f - 24f;

            ui.progressLabel = MakeText(canvas, "Progress", "Completed 0 / " + levelCount, 20, Accent,
                new Vector2(0, quitY + 40f), new Vector2(700, 28));

            ui.quitButton = MakeButton(canvas, "QuitButton", "QUIT", new Vector2(0, quitY), new Vector2(220, 58), 24, ButtonCol);

            MakeText(canvas, "Hint", "WASD / Arrows — move    Z — undo    R — restart    M — mute    Esc — menu",
                20, DimText, new Vector2(0, 22), new Vector2(1400, 30), FontStyle.Normal, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0));

            CreateFade(canvas);

            string path = SceneDir + "/MainMenu.unity";
            EditorSceneManager.SaveScene(scene, path);
            return path;
        }

        // ================================================= scene building blocks
        static Camera CreateCamera(bool ortho)
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            go.transform.position = new Vector3(0f, 0f, -10f);
            cam.orthographic = ortho;
            cam.orthographicSize = 6f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = BG;

            var urpCam = go.AddComponent<UniversalAdditionalCameraData>();
            urpCam.renderPostProcessing = false;

            go.AddComponent<AudioListener>();
            return cam;
        }

        static void CreateEventSystem()
        {
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        static Transform CreateCanvas(string name)
        {
            var go = new GameObject(name);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return go.transform;
        }

        // Full-screen black overlay that fades out on scene load (level transitions).
        // Added last so it draws on top of everything else on the canvas.
        static void CreateFade(Transform canvas)
        {
            var rt = MakeRect(canvas, "ScreenFade", Vector2.zero, Vector2.zero,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            var img = rt.gameObject.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f); // transparent in the editor;
            img.raycastTarget = false;             // ScreenFade turns it black at runtime, then fades out
            rt.gameObject.AddComponent<ScreenFade>().image = img;
        }

        static RectTransform MakeRect(Transform parent, string name, Vector2 pos, Vector2 size,
            Vector2? anchorMin = null, Vector2? anchorMax = null, Vector2? pivot = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin ?? new Vector2(0.5f, 0.5f);
            rt.anchorMax = anchorMax ?? new Vector2(0.5f, 0.5f);
            rt.pivot = pivot ?? new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        static Text MakeText(Transform parent, string name, string content, int size, Color color,
            Vector2 pos, Vector2 dim, FontStyle style = FontStyle.Normal,
            TextAnchor align = TextAnchor.MiddleCenter,
            Vector2? anchorMin = null, Vector2? anchorMax = null, Vector2? pivot = null)
        {
            var rt = MakeRect(parent, name, pos, dim, anchorMin, anchorMax, pivot);
            var text = rt.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.fontStyle = style;
            text.alignment = align;
            text.raycastTarget = false;
            return text;
        }

        static Button MakeButton(Transform parent, string name, string label,
            Vector2 pos, Vector2 size, int fontSize)
            => MakeButton(parent, name, label, pos, size, fontSize, ButtonCol);

        static Button MakeButton(Transform parent, string name, string label,
            Vector2 pos, Vector2 size, int fontSize, Color color)
        {
            var rt = MakeRect(parent, name, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            img.type = Image.Type.Sliced;
            img.color = color;

            var btn = rt.gameObject.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = Color.white;                          // tints the image (= its own color)
            colors.highlightedColor = new Color(0.9f, 0.9f, 0.9f, 1f); // subtle darken on hover
            colors.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
            colors.selectedColor = Color.white;
            colors.fadeDuration = 0.08f;
            btn.colors = colors;

            rt.gameObject.AddComponent<UIHoverScale>();
            MakeText(rt, "Label", label, fontSize, Color.white, Vector2.zero, size);
            return btn;
        }

        // A non-interactive rounded UI image (menu decoration / mascot).
        static Image UIImage(Transform parent, string name, Sprite sprite, Color color, Vector2 pos, Vector2 size)
        {
            var rt = MakeRect(parent, name, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        // "Beaten" checkmark badge for a level button; hidden until MainMenuUI shows it.
        static GameObject MakeCheck(Transform button, Sprites spr)
        {
            var rt = MakeRect(button, "Check", new Vector2(32, 20), new Vector2(30, 30));
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = spr.fill;
            img.color = Hex("34C759");
            img.raycastTarget = false;
            MakeText(rt, "Tick", "✓", 22, Color.white, Vector2.zero, new Vector2(30, 30), FontStyle.Bold);
            rt.gameObject.SetActive(false);
            return rt.gameObject;
        }

        // ================================================= color helpers
        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }

        static Color Lighten(Color c, float t) => Color.Lerp(c, Color.white, t);
        static Color Darken(Color c, float t) => Color.Lerp(c, Color.black, t);
    }
}
