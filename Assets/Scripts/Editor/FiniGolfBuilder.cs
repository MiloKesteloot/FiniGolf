using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace FiniGolf.EditorTools
{
    /// <summary>
    /// One-click project setup: FiniGolf > Build Game.
    /// Configures the Kenney models, generates materials / textures / sprites, and builds the
    /// MainMenu and Game scenes (lighting, camera, managers, UI) plus the build settings.
    /// Safe to run again at any time; it regenerates everything it owns.
    /// </summary>
    public static class FiniGolfBuilder
    {
        private const string ModelsFolder = "Assets/Kenney/Minigolf/Models";
        private const string Root = "Assets/FiniGolf";
        private const string MaterialsFolder = Root + "/Materials";
        private const string TexturesFolder = Root + "/Textures";
        private const string DataFolder = Root + "/Data";
        private const string ScenesFolder = "Assets/Scenes";
        private static string MenuScenePath => $"{ScenesFolder}/{GameManager.MenuScene}.unity";
        private static string GameScenePath => $"{ScenesFolder}/{GameManager.GameScene}.unity";

        [MenuItem("FiniGolf/Build Game", priority = 0)]
        public static void BuildAll()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            try
            {
                EditorUtility.DisplayProgressBar("FiniGolf", "Configuring Kenney models...", 0.1f);
                EnsureFolder(MaterialsFolder);
                EnsureFolder(TexturesFolder);
                EnsureFolder(DataFolder);
                EnsureFolder(ScenesFolder);
                ConfigureModels();

                EditorUtility.DisplayProgressBar("FiniGolf", "Creating materials and textures...", 0.3f);
                var assets = BuildGameAssets();
                var sky = BuildSkybox();
                var profile = BuildVolumeProfile();

                EditorUtility.DisplayProgressBar("FiniGolf", "Building Main Menu scene...", 0.6f);
                BuildMenuScene(assets, sky, profile);

                EditorUtility.DisplayProgressBar("FiniGolf", "Building Game scene...", 0.8f);
                BuildGameScene(assets, sky, profile);

                EditorBuildSettings.scenes = new[]
                {
                    new EditorBuildSettingsScene(MenuScenePath, true),
                    new EditorBuildSettingsScene(GameScenePath, true),
                };
                PlayerSettings.productName = "FiniGolf";

                AssetDatabase.SaveAssets();
                EditorSceneManager.OpenScene(MenuScenePath);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            EditorUtility.DisplayDialog("FiniGolf",
                "All set!\n\nScenes 'MainMenu' and 'Game' were built and added to Build Settings.\n" +
                "The MainMenu scene is now open: press Play to start.", "Let's golf");
        }

        [MenuItem("FiniGolf/Play From Main Menu", priority = 1)]
        public static void PlayFromMenu()
        {
            if (!File.Exists(MenuScenePath))
            {
                BuildAll();
                if (!File.Exists(MenuScenePath)) return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(MenuScenePath);
            EditorApplication.isPlaying = true;
        }

        // ------------------------------------------------------------------
        // Assets
        // ------------------------------------------------------------------

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static void ConfigureModels()
        {
            var guids = AssetDatabase.FindAssets("t:Model", new[] { ModelsFolder });
            var toReimport = new List<ModelImporter>();
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is ModelImporter imp)) continue;
                bool dirty = false;
                // MeshColliders are created at runtime, which needs readable meshes in builds.
                if (!imp.isReadable) { imp.isReadable = true; dirty = true; }
                if (imp.addCollider) { imp.addCollider = false; dirty = true; }
                if (imp.importAnimation) { imp.importAnimation = false; dirty = true; }
                if (imp.importCameras) { imp.importCameras = false; dirty = true; }
                if (imp.importLights) { imp.importLights = false; dirty = true; }
                if (dirty) toReimport.Add(imp);
            }

            if (toReimport.Count > 0)
            {
                AssetDatabase.StartAssetEditing();
                try
                {
                    foreach (var imp in toReimport) imp.SaveAndReimport();
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }
            }

            var colormapPath = ModelsFolder + "/Textures/colormap.png";
            if (AssetImporter.GetAtPath(colormapPath) is TextureImporter tex)
            {
                if (tex.mipmapEnabled || tex.filterMode != FilterMode.Bilinear)
                {
                    tex.mipmapEnabled = false;
                    tex.filterMode = FilterMode.Bilinear;
                    tex.SaveAndReimport();
                }
            }
        }

        private static Sprite KnobSprite => AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

        private static Shader LitShader => Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        private static Shader UnlitShader => Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");

        private static Material MakeMaterial(string name, Shader shader, System.Action<Material> setup)
        {
            var path = $"{MaterialsFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = shader;
            }
            setup(mat);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        private static void SetTexture(Material m, Texture t)
        {
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", t);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", t);
        }

        private static GameAssets BuildGameAssets()
        {
            var colormap = AssetDatabase.LoadAssetAtPath<Texture2D>(ModelsFolder + "/Textures/colormap.png");
            var beachTex = MakeBeachBallTexture();
            var heart = MakeHeartSprite();

            var tileMat = MakeMaterial("KenneyColormap", LitShader, m =>
            {
                SetTexture(m, colormap);
                SetColor(m, Color.white);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.18f);
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            });
            var arrowMat = MakeMaterial("AimArrow", UnlitShader, m => SetColor(m, Color.white));
            var beachMat = MakeMaterial("BeachBall", LitShader, m =>
            {
                SetTexture(m, beachTex);
                SetColor(m, Color.white);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.6f);
            });
            var hazardMat = MakeMaterial("Hazard", LitShader, m =>
            {
                SetColor(m, new Color(0.95f, 0.32f, 0.36f));
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.45f);
            });
            var pickupMat = MakeMaterial("Pickup", LitShader, m =>
            {
                SetColor(m, Color.white);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.7f);
                m.EnableKeyword("_EMISSION");
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.white * 0.6f);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            });

            var particles = Shader.Find(MaterialUtil.ParticlesUnlit) ?? UnlitShader;
            var pathMat = MakeMaterial("PathLine", particles, m =>
            {
                if (particles.name == MaterialUtil.ParticlesUnlit) MaterialUtil.SetupTransparent(m);
                SetColor(m, Color.white);
            });

            var fadedTileMat = MakeMaterial("KenneyColormapFaded", LitShader, m =>
            {
                m.CopyPropertiesFromMaterial(tileMat);
                MaterialUtil.SetupTransparentLit(m);
            });

            var path = DataFolder + "/GameAssets.asset";
            var ga = AssetDatabase.LoadAssetAtPath<GameAssets>(path);
            if (ga == null)
            {
                ga = ScriptableObject.CreateInstance<GameAssets>();
                AssetDatabase.CreateAsset(ga, path);
            }

            ga.models.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { ModelsFolder }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (go != null)
                    ga.models.Add(new GameAssets.NamedModel { name = Path.GetFileNameWithoutExtension(p), model = go });
            }
            ga.models.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            ga.tileMaterial = tileMat;
            ga.arrowMaterial = arrowMat;
            ga.beachBallMaterial = beachMat;
            ga.hazardMaterial = hazardMat;
            ga.pickupMaterial = pickupMat;
            ga.pathMaterial = pathMat;
            ga.fadedTileMaterial = fadedTileMat;
            ga.heartSprite = heart;
            ga.panelSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            EditorUtility.SetDirty(ga);
            AssetDatabase.SaveAssets();

            if (ga.models.Count == 0)
                Debug.LogError($"[FiniGolf] No models found in {ModelsFolder}. Is the Kenney kit imported?");
            return ga;
        }

        private static Texture2D MakeBeachBallTexture()
        {
            const int w = 512, h = 256;
            var colors = new[]
            {
                new Color(0.93f, 0.2f, 0.22f), Color.white, new Color(0.15f, 0.45f, 0.95f),
                Color.white, new Color(1f, 0.82f, 0.15f), Color.white,
            };
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++)
            {
                float v = y / (float)(h - 1);
                for (int x = 0; x < w; x++)
                {
                    float u = x / (float)w;
                    float seg = u * colors.Length;
                    int i = Mathf.FloorToInt(seg) % colors.Length;
                    var c = colors[i];
                    float edge = Mathf.Min(seg - Mathf.Floor(seg), Mathf.Ceil(seg) - seg);
                    if (edge < 0.02f) c *= 0.85f;
                    if (v < 0.08f || v > 0.92f) c = Color.white;
                    c.a = 1f;
                    tex.SetPixel(x, y, c);
                }
            }
            return SaveTexture(tex, TexturesFolder + "/BeachBall.png", false);
        }

        private static Sprite MakeHeartSprite()
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            const int ss = 4;
            for (int py = 0; py < size; py++)
            {
                for (int px = 0; px < size; px++)
                {
                    int inside = 0;
                    for (int sy = 0; sy < ss; sy++)
                    {
                        for (int sx = 0; sx < ss; sx++)
                        {
                            float x = ((px + (sx + 0.5f) / ss) / size - 0.5f) * 2.7f;
                            float y = ((py + (sy + 0.5f) / ss) / size - 0.47f) * 2.7f;
                            float a = x * x + y * y - 1f;
                            if (a * a * a - x * x * y * y * y <= 0f) inside++;
                        }
                    }
                    tex.SetPixel(px, py, new Color(1f, 1f, 1f, inside / (float)(ss * ss)));
                }
            }
            SaveTexture(tex, TexturesFolder + "/Heart.png", true);
            return AssetDatabase.LoadAssetAtPath<Sprite>(TexturesFolder + "/Heart.png");
        }

        private static Texture2D SaveTexture(Texture2D tex, string assetPath, bool sprite)
        {
            tex.Apply();
            File.WriteAllBytes(Path.GetFullPath(assetPath), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            if (AssetImporter.GetAtPath(assetPath) is TextureImporter imp)
            {
                imp.mipmapEnabled = !sprite;
                imp.alphaIsTransparency = true;
                imp.wrapMode = sprite ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                if (sprite)
                {
                    imp.textureType = TextureImporterType.Sprite;
                    imp.spriteImportMode = SpriteImportMode.Single;
                }
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        private static Material BuildSkybox()
        {
            var shader = Shader.Find("Skybox/Procedural");
            if (shader == null) return null;
            return MakeMaterial("Sky", shader, m =>
            {
                m.SetFloat("_SunDisk", 2f);
                m.SetFloat("_SunSize", 0.035f);
                m.SetFloat("_SunSizeConvergence", 6f);
                m.SetFloat("_AtmosphereThickness", 0.7f);
                m.SetColor("_SkyTint", new Color(0.42f, 0.62f, 1f));
                m.SetColor("_GroundColor", new Color(0.45f, 0.62f, 0.72f));
                m.SetFloat("_Exposure", 1.35f);
            });
        }

        private static VolumeProfile BuildVolumeProfile()
        {
            var path = DataFolder + "/PostProcessing.asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.45f);
            bloom.threshold.Override(1.0f);
            bloom.scatter.Override(0.65f);

            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.Neutral);

            var color = profile.Add<ColorAdjustments>(true);
            color.saturation.Override(14f);
            color.contrast.Override(8f);
            color.postExposure.Override(0.15f);

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.24f);
            vignette.smoothness.Override(0.45f);

            foreach (var component in profile.components)
            {
                component.name = component.GetType().Name;
                AssetDatabase.AddObjectToAsset(component, profile);
            }
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        // ------------------------------------------------------------------
        // Scene building blocks
        // ------------------------------------------------------------------

        private static void SetupEnvironment(Material sky)
        {
            if (sky != null) RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.78f, 0.87f, 1f);
            RenderSettings.ambientEquatorColor = new Color(0.68f, 0.74f, 0.8f);
            RenderSettings.ambientGroundColor = new Color(0.36f, 0.4f, 0.45f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.74f, 0.85f, 0.97f);
            RenderSettings.fogStartDistance = 35f;
            RenderSettings.fogEndDistance = 140f;
        }

        private static void CreateSun()
        {
            var go = new GameObject("Sun");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.3f;
            light.color = new Color(1f, 0.95f, 0.86f);
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.75f;
            go.transform.rotation = Quaternion.Euler(52f, -32f, 0f);
            RenderSettings.sun = light;
        }

        private static Camera CreateCamera()
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 400f;
            go.AddComponent<AudioListener>();
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            go.transform.SetPositionAndRotation(new Vector3(0f, 4f, -5f), Quaternion.Euler(35f, 0f, 0f));
            return cam;
        }

        private static void CreateVolume(VolumeProfile profile)
        {
            var go = new GameObject("Global Volume");
            var v = go.AddComponent<Volume>();
            v.isGlobal = true;
            v.priority = 1f;
            v.sharedProfile = profile;
        }

        private static void CreateEventSystem()
        {
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        private static CourseManager CreateCourseManager(GameObject managers, GameAssets assets)
        {
            var course = managers.AddComponent<CourseManager>();
            course.assets = assets;
            course.courseRoot = new GameObject("Course").transform;
            return course;
        }

        private static void CreateFader()
        {
            var canvas = UIFactory.Canvas("Fader", 100);
            var img = UIFactory.Image("Black", canvas.transform, new Color(0.03f, 0.04f, 0.08f, 1f));
            img.rectTransform.Stretch();
            img.raycastTarget = true;
            canvas.gameObject.AddComponent<CanvasGroup>();
            var fader = canvas.gameObject.AddComponent<ScreenFader>();
            fader.fadeInOnStart = false;
        }

        // ------------------------------------------------------------------
        // Main Menu
        // ------------------------------------------------------------------

        private static void BuildMenuScene(GameAssets assets, Material sky, VolumeProfile profile)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupEnvironment(sky);
            CreateSun();
            var cam = CreateCamera();
            CreateVolume(profile);

            var managers = new GameObject("Managers");
            CreateCourseManager(managers, assets);
            managers.AddComponent<Sfx>();
            var backdrop = managers.AddComponent<MenuBackdrop>();
            backdrop.cam = cam;

            CreateEventSystem();
            CreateFader();

            var panelSprite = assets.panelSprite;
            var canvas = UIFactory.Canvas("Menu Canvas", 10);
            var menu = canvas.gameObject.AddComponent<MainMenuUI>();

            // Soft gradient strip on the left so text stays readable over the 3D backdrop.
            var strip = UIFactory.Image("Backdrop", canvas.transform, new Color(0.04f, 0.07f, 0.16f, 0.55f));
            strip.rectTransform.anchorMin = new Vector2(0f, 0f);
            strip.rectTransform.anchorMax = new Vector2(0f, 1f);
            strip.rectTransform.pivot = new Vector2(0f, 0.5f);
            strip.rectTransform.sizeDelta = new Vector2(760f, 0f);
            strip.rectTransform.anchoredPosition = Vector2.zero;
            strip.raycastTarget = false;

            // Title
            var titleRoot = UIFactory.Rect("Title", canvas.transform).At(new Vector2(0f, 1f), new Vector2(90f, -90f), new Vector2(640f, 240f));
            var title = UIFactory.Text("Logo", titleRoot, "FINIGOLF", 130, Color.white, TextAnchor.UpperLeft);
            title.rectTransform.Stretch();
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            var outline = title.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.1f, 0.35f, 0.2f, 0.9f);
            outline.effectDistance = new Vector2(4f, -4f);
            var tagline = UIFactory.Text("Tagline", titleRoot, "infinite minigolf", 40, UIFactory.Accent, TextAnchor.LowerLeft, FontStyle.BoldAndItalic);
            tagline.rectTransform.Stretch(8f, 0f, 0f, 30f);
            menu.title = titleRoot;

            // Main panel with buttons
            var main = UIFactory.Rect("Main Panel", canvas.transform).At(new Vector2(0f, 0.5f), new Vector2(90f, -110f), new Vector2(520f, 600f));
            menu.mainPanel = main.gameObject;
            var size = new Vector2(440f, 96f);
            menu.playButton = UIFactory.Button("Play", main, "PLAY", UIFactory.Green, panelSprite, size, 50);
            menu.playButton.GetComponent<RectTransform>().At(new Vector2(0f, 1f), new Vector2(0f, 0f), size);
            menu.optionsButton = UIFactory.Button("Options", main, "OPTIONS", UIFactory.Blue, panelSprite, size, 40);
            menu.optionsButton.GetComponent<RectTransform>().At(new Vector2(0f, 1f), new Vector2(0f, -120f), size);
            menu.creditsButton = UIFactory.Button("Credits", main, "CREDITS", UIFactory.Blue, panelSprite, size, 40);
            menu.creditsButton.GetComponent<RectTransform>().At(new Vector2(0f, 1f), new Vector2(0f, -240f), size);
            menu.quitButton = UIFactory.Button("Quit", main, "QUIT", new Color(0.35f, 0.38f, 0.5f), panelSprite, size, 40);
            menu.quitButton.GetComponent<RectTransform>().At(new Vector2(0f, 1f), new Vector2(0f, -360f), size);
            menu.bestText = UIFactory.Text("Best", main, "", 30, UIFactory.TextDim, TextAnchor.UpperLeft, FontStyle.Bold);
            menu.bestText.rectTransform.At(new Vector2(0f, 1f), new Vector2(6f, -480f), new Vector2(520f, 60f));

            // Options panel
            var options = UIFactory.Image("Options Panel", canvas.transform, UIFactory.PanelSolid, panelSprite);
            options.rectTransform.At(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820f, 460f));
            menu.optionsPanel = options.gameObject;
            var oTitle = UIFactory.Text("Title", options.transform, "OPTIONS", 72, UIFactory.Accent);
            oTitle.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -36f), new Vector2(760f, 100f));
            var oVolume = UIFactory.VolumeRow(options.transform, new Vector2(660f, 110f), panelSprite, KnobSprite);
            oVolume.At(new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(660f, 110f));
            menu.optionsBackButton = UIFactory.Button("Back", options.transform, "BACK", UIFactory.Green, panelSprite, new Vector2(300f, 90f), 40);
            menu.optionsBackButton.GetComponent<RectTransform>().At(new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(300f, 90f));

            // Credits panel
            var credits = UIFactory.Image("Credits Panel", canvas.transform, UIFactory.PanelSolid, panelSprite);
            credits.rectTransform.At(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 700f));
            menu.creditsPanel = credits.gameObject;
            var cTitle = UIFactory.Text("Title", credits.transform, "CREDITS", 72, UIFactory.Accent);
            cTitle.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(800f, 100f));
            var body = UIFactory.Text("Body", credits.transform,
                "<size=30><color=#FFFFFFAA>GAME DESIGN & DEVELOPMENT</color></size>\n" +
                "<size=56>Milo Kesteloot</size>\n\n" +
                "<size=30><color=#FFFFFFAA>3D ASSETS</color></size>\n" +
                "<size=56>Kenney</size>\n" +
                "<size=30>Minigolf Kit  ·  www.kenney.nl  ·  CC0</size>\n\n" +
                "<size=28><color=#FFFFFF88>Thanks for playing!</color></size>",
                40, Color.white);
            body.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(820f, 460f));
            menu.backButton = UIFactory.Button("Back", credits.transform, "BACK", UIFactory.Green, panelSprite, new Vector2(300f, 90f), 40);
            menu.backButton.GetComponent<RectTransform>().At(new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(300f, 90f));

            EditorSceneManager.SaveScene(scene, MenuScenePath);
        }

        // ------------------------------------------------------------------
        // Game
        // ------------------------------------------------------------------

        private static void BuildGameScene(GameAssets assets, Material sky, VolumeProfile profile)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupEnvironment(sky);
            CreateSun();
            var cam = CreateCamera();
            cam.gameObject.AddComponent<CameraController>();
            CreateVolume(profile);

            var managers = new GameObject("Managers");
            managers.AddComponent<GameManager>();
            CreateCourseManager(managers, assets);
            var ball = managers.AddComponent<BallManager>();
            ball.assets = assets;
            ball.cam = cam;
            managers.AddComponent<Sfx>();

            CreateEventSystem();
            CreateFader();
            BuildHUD(assets);

            EditorSceneManager.SaveScene(scene, GameScenePath);
        }

        private static void BuildHUD(GameAssets assets)
        {
            var sprite = assets.panelSprite;
            var canvas = UIFactory.Canvas("HUD Canvas", 10);
            var hud = canvas.gameObject.AddComponent<GameHUD>();
            var t = canvas.transform;

            // ---- Hole (top-left) ----
            var holePanel = UIFactory.Image("Hole Info", t, UIFactory.Panel, sprite);
            holePanel.rectTransform.At(new Vector2(0f, 1f), new Vector2(30f, -30f), new Vector2(420f, 130f));
            hud.holeText = UIFactory.Text("Hole", holePanel.transform, "HOLE 1", 56, Color.white, TextAnchor.UpperLeft);
            hud.holeText.rectTransform.Stretch(28f, 20f, 14f, 50f);
            hud.holeSubText = UIFactory.Text("Sub", holePanel.transform, "", 26, UIFactory.TextDim, TextAnchor.LowerLeft, FontStyle.Normal);
            hud.holeSubText.rectTransform.Stretch(30f, 20f, 0f, 16f);

            // ---- Par / strokes (top-centre) ----
            var score = UIFactory.Image("Score", t, UIFactory.Panel, sprite);
            score.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(520f, 130f));
            hud.parValue = ScoreColumn(score.transform, "PAR", -130f);
            hud.strokesValue = ScoreColumn(score.transform, "STROKES", 130f);
            var divider = UIFactory.Image("Divider", score.transform, new Color(1f, 1f, 1f, 0.15f));
            divider.rectTransform.At(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(3f, 90f));

            // ---- Hearts (top-right) ----
            var heartPanel = UIFactory.Image("Hearts", t, UIFactory.Panel, sprite);
            heartPanel.rectTransform.At(new Vector2(1f, 1f), new Vector2(-30f, -30f), new Vector2(330f, 130f));
            hud.hearts = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                var heart = UIFactory.Image($"Heart {i + 1}", heartPanel.transform, hud.heartFull, assets.heartSprite);
                heart.preserveAspect = true;
                heart.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2((i - 1) * 96f, 0f), new Vector2(86f, 86f));
            }
            for (int i = 0; i < 3; i++) hud.hearts[i] = heartPanel.transform.GetChild(i).GetComponent<Image>();

            // ---- Upgrade banner (bottom-centre) ----
            var upgrade = UIFactory.Image("Upgrade", t, UIFactory.Panel, sprite);
            upgrade.rectTransform.At(new Vector2(0.5f, 0f), new Vector2(0f, 96f), new Vector2(820f, 116f));
            hud.upgradePanel = upgrade.gameObject;
            hud.upgradeAccent = UIFactory.Image("Accent", upgrade.transform, Color.white, sprite);
            hud.upgradeAccent.rectTransform.At(new Vector2(0f, 0.5f), new Vector2(18f, 0f), new Vector2(18f, 80f));
            hud.upgradeTitle = UIFactory.Text("Title", upgrade.transform, "NEXT SHOT: BEACH BALL", 38, Color.white, TextAnchor.UpperLeft);
            hud.upgradeTitle.rectTransform.Stretch(56f, 20f, 14f, 50f);
            hud.upgradeDescription = UIFactory.Text("Description", upgrade.transform, "", 26, UIFactory.TextDim, TextAnchor.LowerLeft, FontStyle.Normal);
            hud.upgradeDescription.rectTransform.Stretch(58f, 20f, 0f, 18f);

            // ---- Power bar ----
            var power = UIFactory.Image("Power Bar", t, new Color(0f, 0f, 0f, 0.55f), sprite);
            power.rectTransform.At(new Vector2(0.5f, 0f), new Vector2(0f, 236f), new Vector2(460f, 30f));
            hud.powerBar = power.gameObject;
            var fill = UIFactory.Image("Fill", power.transform, Color.green, sprite);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            fill.rectTransform.pivot = new Vector2(0f, 0.5f);
            fill.rectTransform.offsetMin = new Vector2(4f, 4f);
            fill.rectTransform.offsetMax = new Vector2(-4f, -4f);
            hud.powerFill = fill.rectTransform;
            hud.powerFillImage = fill;
            var powerLabel = UIFactory.Text("Label", power.transform, "POWER", 22, Color.white);
            powerLabel.rectTransform.Stretch();

            // ---- Hint ----
            hud.hintText = UIFactory.Text("Hint", t,
                "Click & drag to aim  ·  Release to shoot  ·  Right-drag or A/D to orbit  ·  Scroll to zoom  ·  Esc to pause",
                24, new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleCenter, FontStyle.Normal);
            hud.hintText.rectTransform.At(new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(1600f, 50f));

            // ---- Toast ----
            var toastRect = UIFactory.Rect("Toast", t).At(new Vector2(0.5f, 0.5f), new Vector2(0f, 250f), new Vector2(1500f, 100f));
            hud.toastGroup = toastRect.gameObject.AddComponent<CanvasGroup>();
            hud.toastGroup.blocksRaycasts = false;
            hud.toastGroup.interactable = false;
            hud.toastText = UIFactory.Text("Text", toastRect, "", 54, Color.white);
            hud.toastText.rectTransform.Stretch();
            var toastOutline = hud.toastText.gameObject.AddComponent<Outline>();
            toastOutline.effectColor = new Color(0f, 0f, 0f, 0.55f);
            toastOutline.effectDistance = new Vector2(3f, -3f);

            // ---- Hole complete ----
            var hp = UIFactory.Image("Hole Complete", t, UIFactory.PanelSolid, sprite);
            hp.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(780f, 340f));
            hp.raycastTarget = false;
            hud.holePanel = hp.gameObject;
            hud.holeTitle = UIFactory.Text("Title", hp.transform, "Birdie!", 92, UIFactory.Accent);
            hud.holeTitle.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(740f, 130f));
            hud.holeDetail = UIFactory.Text("Detail", hp.transform, "", 40, Color.white, TextAnchor.UpperCenter, FontStyle.Normal);
            hud.holeDetail.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -190f), new Vector2(740f, 130f));

            // ---- Game over ----
            var go = UIFactory.Image("Game Over", t, new Color(0.02f, 0.03f, 0.08f, 0.7f));
            go.rectTransform.Stretch();
            hud.gameOverPanel = go.gameObject;
            var goCard = UIFactory.Image("Card", go.transform, UIFactory.PanelSolid, sprite);
            goCard.rectTransform.At(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(860f, 600f));
            var goTitle = UIFactory.Text("Title", goCard.transform, "GAME OVER", 100, UIFactory.Red);
            goTitle.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(820f, 130f));
            hud.gameOverReason = UIFactory.Text("Reason", goCard.transform, "", 40, Color.white, TextAnchor.MiddleCenter, FontStyle.Normal);
            hud.gameOverReason.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -175f), new Vector2(820f, 60f));
            hud.gameOverStats = UIFactory.Text("Stats", goCard.transform, "", 38, UIFactory.TextDim, TextAnchor.UpperCenter, FontStyle.Normal);
            hud.gameOverStats.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -255f), new Vector2(820f, 170f));
            var btnSize = new Vector2(330f, 96f);
            hud.retryButton = UIFactory.Button("Retry", goCard.transform, "PLAY AGAIN", UIFactory.Green, sprite, btnSize, 40);
            hud.retryButton.GetComponent<RectTransform>().At(new Vector2(0.5f, 0f), new Vector2(-185f, 44f), btnSize);
            hud.gameOverMenuButton = UIFactory.Button("Menu", goCard.transform, "MAIN MENU", UIFactory.Blue, sprite, btnSize, 40);
            hud.gameOverMenuButton.GetComponent<RectTransform>().At(new Vector2(0.5f, 0f), new Vector2(185f, 44f), btnSize);

            // ---- Pause ----
            var pause = UIFactory.Image("Pause", t, new Color(0.02f, 0.03f, 0.08f, 0.6f));
            pause.rectTransform.Stretch();
            hud.pausePanel = pause.gameObject;
            var pCard = UIFactory.Image("Card", pause.transform, UIFactory.PanelSolid, sprite);
            pCard.rectTransform.At(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640f, 600f));
            var pTitle = UIFactory.Text("Title", pCard.transform, "PAUSED", 86, Color.white);
            pTitle.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(560f, 120f));
            var pSize = new Vector2(400f, 96f);
            hud.resumeButton = UIFactory.Button("Resume", pCard.transform, "RESUME", UIFactory.Green, sprite, pSize, 40);
            hud.resumeButton.GetComponent<RectTransform>().At(new Vector2(0.5f, 0f), new Vector2(0f, 156f), pSize);
            hud.pauseMenuButton = UIFactory.Button("Menu", pCard.transform, "MAIN MENU", UIFactory.Blue, sprite, pSize, 40);
            hud.pauseMenuButton.GetComponent<RectTransform>().At(new Vector2(0.5f, 0f), new Vector2(0f, 40f), pSize);
            var pVolume = UIFactory.VolumeRow(pCard.transform, new Vector2(520f, 110f), sprite, KnobSprite);
            pVolume.At(new Vector2(0.5f, 1f), new Vector2(0f, -170f), new Vector2(520f, 110f));
        }

        private static Text ScoreColumn(Transform parent, string label, float x)
        {
            var l = UIFactory.Text(label + " Label", parent, label, 24, UIFactory.TextDim, TextAnchor.MiddleCenter, FontStyle.Bold);
            l.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(x, -14f), new Vector2(240f, 34f));
            var v = UIFactory.Text(label + " Value", parent, "0", 66, Color.white);
            v.rectTransform.At(new Vector2(0.5f, 0f), new Vector2(x, 10f), new Vector2(240f, 80f));
            return v;
        }
    }
}
