using Chisel.Formatter;
using Chisel.Utils;
using Engine.Compilation;
using Engine.Conditions;
using Engine.Console;
using Engine.Entities;
using Engine.Input;
using Engine.Physics;
using Engine.Rendering;
using Engine.SaveSystem;
using Engine.Scripting.Sound;
using Engine.Sound;
using Engine.UI;
using Engine.Utils;
using Engine.Utils.Settings;
using FontStashSharp;
using Gum.DataTypes;
using ImGuiNET;
using JoltPhysicsSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGame.ImGuiNet;
using MonoGameGum;
using Newtonsoft.Json;
using Rockwall;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

namespace Engine
{
    public class MainEngine : Game
    {
        public static string FullPath;
        public static string AppPath = $"{Path.GetDirectoryName(System.AppContext.BaseDirectory)}";

        public static int Width = 1600, Height = 900;
        public static float AspectRatio;

        public SpriteBatch SpriteBatch;
        public GameConsole Console
        {
            get; protected set;
        }
        public GameConsoleWindow ConsoleWindow
        {
            get; protected set;
        }
        public OptionsWindow OptionsWindow
        {
            get; protected set;
        }
        public SaveViewMenu LoadMenu
        {
            get; protected set;
        }
        public SaveViewMenu SaveMenu
        {
            get; protected set;
        }

        internal static Queue<CommandBinding> commands = new Queue<CommandBinding>();
        internal static bool commandsDirty = true;
        public static FixedList<Vector3> DebugDrawPositions = new FixedList<Vector3>(512,true);
        public static FixedList<(Vector3 a, Vector3 b)> DebugDrawRays = new FixedList<(Vector3 a, Vector3 b)>(512, true);

        public bool IsLoading;
        protected float timeScale, desiredTimeScale = 1;
        public int PostLoadFrameSleep;
        public static bool IsPaused = false, IsConsoleOpen = false, DisableLight = false, ShowLightmap = false, DefaultCursorLockState = true;
        public static bool PauseWhenMenusOpen = true;
        public static bool IsCursorLocked = true;
        public static bool RawInput = false;
        private bool willMapBeLoaded = false;
        public bool IsMapLoaded;
        public string ActiveMapPath;

        public static float ResolutionScalar = 1f;
        public static Vector2 PixelSize;

        private bool isFullscreen = true;
        public bool IsFullscreen
        {
            get
            {
                return isFullscreen;
            }
            set
            {
                isFullscreen = value;
                RenderEngine.RebuildDisplay();
            }
        }

        public static float PreviousFrameDelta;
        public static uint FrameCount;
        public static MainEngine Instance { get; protected set; }

        public float Timescale => timeScale;

        public Vector3 CameraPosition => RenderEngine.CameraPosition;
        public Vector3 CameraForward => RenderEngine.CameraForward;

        public Vector3 DirectionalLightDirection = new Vector3(0.25f, 2, 0.5f);
        public Vector3 DirectionalLightRightVector = Vector3.Right;
        public Vector3 DirectionalLightForwardVector = Vector3.Forward;
        public Quaternion DirectionalLightRotation;
        public Color DirectionalLightColor = new Color(255, 240, 245), AmbientSkyColor = new Color(255, 255, 255), FogColor = new Color(0,0,0);
        public float DirectionalLightStrength = 0.9f, AmbientSkylightStrength = 0.1f, FogBeginDepth = 1f, FogEndDepth = 10f, FogStrength = 0f;

        public static List<Light> ActiveStaticLights
        {
            get;
            private set;
        } = new();

        public const int MaxRealtimeLights = 8;
        public static FixedList<Light> CurrentRealtimeLights = new FixedList<Light>(MaxRealtimeLights, true);
        public static Vector4[] ShaderRealtimeLightPositions;
        public static Vector4[] ShaderRealtimeLightColors;
        public static Vector4[] ShaderRealtimeLightSpotData;
        public static int ShaderRealtimeLightCount;

        public static bool LoadedMapHasVis;

        public ShaderHandle WorldShader, TerrainShader, PropModelShader;

        //private RenderTarget2D albedo, normal, position, lightmap, mixLightmap, discardPixels;
        protected static ImGuiRenderer guiRenderer;

        public ShaderHandle PostProcessingShader;

        private volatile PendingMapLoad pendingMapLoad;
        private volatile Exception loadingException;
        private volatile string loadingStatusText;
        private Action postLoadCallback;

        #region Commands

        public readonly static CommandBinding cTimescale = new CommandBinding("timescale", (string[] arg) =>
        {
            Instance.desiredTimeScale = float.Parse(arg[0], CultureInfo.InvariantCulture);
        });
        public readonly static CommandBinding cMapchange = new CommandBinding("map", (string[] arg) =>
        {
            SaveManager.ClearSessionMapStates();
            if (arg[0] == "unload")
            {
                SoundscapeManager.StopSoundscape();
                Instance.UnloadMap();

                return;
            }
            if (Path.IsPathFullyQualified(arg[0]))
            {
                SoundscapeManager.StopSoundscape();
                Instance.LoadMap($"{arg[0]}.crm");
            }
            else
            {
                SoundscapeManager.StopSoundscape();
                Instance.LoadMap($"{FullPath}/Maps/{arg[0]}.crm");
            }
        });
        public readonly static CommandBinding cQuitGame = new CommandBinding("quit", (string[] arg) =>
        {
            Instance.Exit();
        });
        public readonly static CommandBinding cEntityManipulate = new CommandBinding("ent", (string[] arg) =>
        {
            var entity = Array.Find(EntityManager.entities.GetValues().ToArray(),e => e.Name == arg[1]);
            if (entity == null && arg[0] != "create")
            {
                throw new Exception($"Entity {arg[1]} does not exist. (Did you name it properly?)");
            }

            switch(arg[0])
            {
                case "fire":
                    {
                        switch (arg[2])
                        {
                            case "remove":

                                EntityManager.DespawnEntity(entity);

                                break;

                            case "set":

                                float x = float.Parse(arg[4].Split(',')[0], CultureInfo.InvariantCulture);
                                float y = float.Parse(arg[4].Split(',')[1], CultureInfo.InvariantCulture);
                                float z = float.Parse(arg[4].Split(',')[2], CultureInfo.InvariantCulture);

                                Vector3 input = new Vector3(x, y, z);

                                switch (arg[3])
                                {
                                    case "pos":

                                        entity.Position = input;

                                        break;
                                    case "rot":

                                        entity.Rotation = CMath.ToQuaternion(new Vector3(MathHelper.ToRadians(input.X), MathHelper.ToRadians(input.Y), MathHelper.ToRadians(input.Z)));

                                        break;
                                    case "scale":

                                        entity.Scale = input;

                                        break;
                                }

                                break;
                            case "read":

                                switch (arg[3])
                                {
                                    case "pos":

                                        Instance.Console.WriteDirect("{" + $"X: {entity.Position.X:0.00}, Y: {entity.Position.Y:0.00}, Z: {entity.Position.Z:0.00}" + "}");

                                        break;
                                    case "rot":

                                        Instance.Console.WriteDirect($"Quaternion: {entity.Rotation}");
                                        Instance.Console.WriteDirect($"Euler Angles (readability): {CMath.ToEulerAngles(entity.Rotation)}");

                                        break;
                                    case "scale":

                                        Instance.Console.WriteDirect(entity.Scale.ToString());

                                        break;
                                }

                                break;
                        }
                    }
                    break;

                case "create":
                    {
                        var ent = (WorldEntity)Activator.CreateInstance(EntityCompiler.EntityLookupTable[arg[1]]);

                        float x = float.Parse(arg[2].Split(',')[0], CultureInfo.InvariantCulture);
                        float y = float.Parse(arg[2].Split(',')[1], CultureInfo.InvariantCulture);
                        float z = float.Parse(arg[2].Split(',')[2], CultureInfo.InvariantCulture);

                        Vector3 input = new Vector3(x, y, z);
                        ent.Position = input;

                        ent.Scale = Vector3.One;
                        ent.Rotation = Quaternion.Identity;

                        EntityManager.SpawnEntity(ent);
                    }
                    break;
            }
        });
        public readonly static CommandBinding cListCMD = new CommandBinding("listcmd", (string[] arg) =>
        {
            Instance.Console.ListCommands();
        });
        public readonly static CommandBinding cOptionsWindow = new CommandBinding("opt_wnd", (string[] arg) =>
        {
            if(!Instance.OptionsWindow.Opened) Instance.OptionsWindow.Open();
        });
        public readonly static CommandBinding cLoadMenu = new CommandBinding("load_wnd", (string[] arg) =>
        {
            if (!Instance.LoadMenu.Opened)
            {
                Instance.LoadMenu.Initialize(false);
            }
        });
        public readonly static CommandBinding cSaveMenu = new CommandBinding("save_wnd", (string[] arg) =>
        {
            if (!Instance.SaveMenu.Opened)
            {
                Instance.SaveMenu.Initialize(true);
            }
        });
        public readonly static CVarInt DeveloperMode = new CVarInt("developer",0);
        public readonly static CVarInt MaxFPS = new CVarInt("max_fps", 250);
        #endregion

        internal SplashScreen? splashScreen;
        public static GumService Gum => GumService.Default;
        public static GumProjectSave GumProject;
        public static ScreenSave ActiveGumScreen;
        protected static string gumProjectPath = $"Gum/gumproj.gumx";

        private static int prevMaxFPS;

        public MainEngine()
        {
            Content.RootDirectory = "Content";
            IsMouseVisible = true;
            Instance = this;

            SoundDevice.Initialize();
            RenderEngine.ConstructRenderEngine();
            ConditionManager.RegisterAll();

            FullPath = Path.GetFullPath(Content.RootDirectory);

            Console = new GameConsole();

            if (!Foundation.Init())
            {
                throw new Exception("Physics engine initialization failed.");
            }
            PhysicsEngine.Init();

            // Default material path is game/Content/Materials. Mount your own if they're stored somewhere else
            MaterialLoader.MountMaterials($"{FullPath}/Materials");

            // Default sound script path is game/Scripts/sound.
            SoundScriptManager.LoadAll($"{FullPath}/Scripts/sound");
            SoundscapeManager.LoadAll($"{FullPath}/Scripts/soundscape");
        }
        protected override void Initialize()
        {
            AspectRatio = (float)Width / Height;

            GameSettings.Settings.Add("resScale", (double)ResolutionScalar);
            GameSettings.Settings.Add("aaMode", (Int64)2);
            GameSettings.Settings.Add("screenSize", (Int64)(RenderEngine.ValidDisplayModes.FindIndex(e => e == GraphicsDevice.DisplayMode)));

            GameSettings.Settings.Add("shadowQual", (Int64)2);
            GameSettings.Settings.Add("shaderQual", (Int64)2);
            GameSettings.Settings.Add("textureQual", (Int64)2);
            GameSettings.Settings.Add("lodBias", (Int64)2);
            GameSettings.Settings.Add("reflectQual", (Int64)2);

            GameSettings.Settings.Add("verticalSync", false);
            GameSettings.Settings.Add("windowedMode", false);

            GameSettings.Settings.Add("rawInput", true);

            GameSettings.RegisterOption(OptionsTab.Controls, new ToggleOption
            {
                OptionLabel = "Use Raw Input",
                GameOption = "rawInput",
                OnLabel = "On",
                OffLabel = "Off",
                GetCurrentValue = () => GameSettings.Settings.TryGetValue("rawInput", out var v)
                                        && v is bool b && b,
                OnChanged = val =>
                {
                    RawInput = val;
                    GameSettings.Settings["rawInput"] = val;
                }
            });

            RenderEngine.CreateListedOptions();

            GameSettings.Settings.Add("masterVol", 1d);
            GameSettings.Settings.Add("sfxVol", 1d);
            GameSettings.Settings.Add("musicVol", 1d);

            SoundDevice.CreateListedOptions();

            RenderEngine.InitRenderEngine();
            DecalManager.Init();

            EnsureCommandsUpdated();

            guiRenderer = new ImGuiRenderer(this);

            GumProject = Gum.Initialize(this, gumProjectPath);

            ConsoleWindow = new GameConsoleWindow();
            ConsoleWindow.Initialize();
            ConsoleWindow.Close();

            OptionsWindow = new OptionsWindow();
            OptionsWindow.Initialize();
            OptionsWindow.Close();

            LoadMenu = new SaveViewMenu();
            SaveMenu = new SaveViewMenu();

            ShaderRealtimeLightPositions = new Vector4[MaxRealtimeLights];
            ShaderRealtimeLightColors = new Vector4[MaxRealtimeLights];
            ShaderRealtimeLightSpotData = new Vector4[MaxRealtimeLights];

            Window.ClientSizeChanged += (s,arg) =>
            {
                RenderEngine.WindowResized(Window.ClientBounds.Width,Window.ClientBounds.Height);
            };

            base.Initialize();
        }
        protected override void LoadContent()
        {
            bool skipSplash = Environment.GetCommandLineArgs().Contains("-map") || Environment.GetCommandLineArgs().Contains("-nosplash");

            if (!skipSplash)
            {
                splashScreen = new SplashScreen(GraphicsDevice, TimeSpan.FromSeconds(2f));

                splashScreen.LoadContent();

                splashScreen.OnComplete = () =>
                {
                    splashScreen.Dispose();
                };
            }

            SpriteBatch = new SpriteBatch(GraphicsDevice);

            ShaderBuilder.RegisterEmbeddedLibraries();

            WorldShader = ShaderBuilder.BuildWorldShader(GraphicsDevice);
            PropModelShader = ShaderBuilder.BuildPropModelShader(GraphicsDevice);
            TerrainShader = ShaderBuilder.BuildTerrainShader(GraphicsDevice);

            LightGroupRuntime.SetCompositeShader(ShaderBuilder.BuildLightGroupCompositeShader(GraphicsDevice));

            RenderEngine.OnMaterialsMounted();

            CModelDisplay.DecalShader ??= ShaderBuilder.BuildRTShadowsShader(GraphicsDevice);
            CModelDisplay.DecalShader.Param("BlobTex").SetValue(RenderEngine.BlobShadowTexture);

            RenderEngine.BlobShadowTexture = AssetManager.LoadAsset<Texture2D>("Textures/blobShadow", "blobAOShadow");

            Skybox.Init(Content);

            AssetManager.AddAsset("worldDefaultShader", WorldShader);
            AssetManager.AddAsset("propModelShader", PropModelShader);
            AssetManager.AddAsset("terrainDefaultShader", TerrainShader);
            AssetManager.AddAsset("modelDefaultShader", (ShaderHandle)ShaderBuilder.BuildModelShader(GraphicsDevice));
            AssetManager.AddAsset("skinnedModelDefaultShader", (ShaderHandle)ShaderBuilder.BuildSkinnedModelShader(GraphicsDevice));
            AssetManager.AddAsset("eyeShader", (ShaderHandle)ShaderBuilder.BuildEyeShader(GraphicsDevice));
            AssetManager.AddAsset("eyeGenerationShader", (ShaderHandle)ShaderBuilder.BuildEyeGenerationShader(GraphicsDevice));

            GameSettings.LoadSetting = LoadSetting;
            GameSettings.LoadSettings();

            guiRenderer.RebuildFontAtlas();

            foreach (var entityEntry in EntityCompiler.EntityLookupTable)
            {
                var ent = Activator.CreateInstance(entityEntry.Value) as WorldEntity;

                ent.Controller?.Prefetch();
            }

            // Weird, but it might fix the weird thing with some settings not applying on load...
            OptionsWindow.Open();
            OptionsWindow.Close();
        }

        protected virtual void LoadSetting(string arg1, object arg2)
        {
            switch(arg1)
            {
                default: break;
                case "aaMode":  RenderEngine.ChangeAAMode(true,(int)Convert.ChangeType(arg2, typeof(int))); break;
                case "resScale":  ResolutionScalar = (float)Convert.ChangeType(arg2, typeof(float)); break;
                case "masterVol":  SoundDevice.Device.SoundLevels[SoundCategory.Master] = (float)Convert.ChangeType(arg2, typeof(float)); break;
                case "sfxVol":     SoundDevice.Device.SoundLevels[SoundCategory.SFX] = (float)Convert.ChangeType(arg2, typeof(float)); break;
                case "musicVol":   SoundDevice.Device.SoundLevels[SoundCategory.Music] = (float)Convert.ChangeType(arg2, typeof(float)); break;
                case "screenSize": RenderEngine.ChangeDisplayMode(RenderEngine.ValidDisplayModes[int.Clamp((int)Convert.ChangeType(arg2, typeof(int)), 0, RenderEngine.ValidDisplayModes.Count-1)]); break;
                case "shadowQual": RenderEngine.ChangeShadowQuality((QualityLevel)Convert.ChangeType(arg2, typeof(int))); break;
                case "shaderQual": RenderEngine.ChangeShaderQuality((QualityLevel)Convert.ChangeType(arg2, typeof(int))); break;
                case "textureQual": RenderEngine.ChangeTextureQuality((QualityLevel)Convert.ChangeType(arg2, typeof(int))); break;
                case "verticalSync": RenderEngine.ChangeVsync((bool)arg2); break;
                case "windowedMode": IsFullscreen = !(bool)Convert.ChangeType(arg2,typeof(bool)); break;
                case "cursorWarp": RawInput = (bool)Convert.ChangeType(arg2,typeof(bool)); break;
            }
        }

        protected override void Update(GameTime gameTime)
        {
            if(prevMaxFPS != MaxFPS)
            {
                LimitVariableFrameRate = MaxFPS > 0;
                if(LimitVariableFrameRate) MaxElapsedTime = TimeSpan.FromSeconds(1f / (MaxFPS));
                prevMaxFPS = MaxFPS;
            }

            using (RenderTimings.Section(TimingSection.Update))
            {
                FrameCount++;

                if (loadingException != null)
                {
                    var ex = loadingException;
                    loadingException = null;
                    IsLoading = false;
                    UnloadMap();
                    Console.WriteDirect(ex.ToString());
                }

                if (pendingMapLoad != null)
                {
                    var pending = pendingMapLoad;
                    pendingMapLoad = null;
                    Console.WriteDirect("Finishing map load...");
                    FinishMapLoad(pending);
                }

                if (IsLoading)
                {
                    PreviousFrameDelta = (float)gameTime.ElapsedGameTime.TotalSeconds;
                    base.Update(gameTime);
                    return;
                }

                //  Update the splash screen if it is active.
                if (splashScreen != null && splashScreen.IsActive)
                {
                    splashScreen.Update(gameTime);
                }

                gameTime.ElapsedGameTime *= timeScale;

                PreviousFrameDelta = (float)gameTime.ElapsedGameTime.TotalSeconds;

                SaveManager.Handle();
                SoundDevice.Device.UpdateWorld();
                InputCapture.Update();

                if (PostLoadFrameSleep > 0)
                {
                    PostLoadFrameSleep--;
                    PreviousFrameDelta = 0;
                }

                timeScale = IsPaused ? 0 : desiredTimeScale;
                SoundDevice.Device.TimeScale = timeScale;

                if (PauseWhenMenusOpen)
                {
                    if (KeyboardManager.HasBeenPressed(Keys.OemTilde))
                    {
                        IsPaused = !IsPaused;
                        IsConsoleOpen = IsPaused;
                        if (IsPaused) UnlockMouse(); else SetMouseToDefaultLockState();
                    }
                }
                else
                {
                    if (KeyboardManager.HasBeenPressed(Keys.OemTilde))
                    {
                        IsConsoleOpen = !IsConsoleOpen;
                    }
                }

                if (KeyboardManager.HasBeenPressed(Keys.Escape) && IsMapLoaded)
                {
                    IsPaused = !IsPaused;

                    if (IsPaused) UnlockMouse(); else SetMouseToDefaultLockState();
                }
                if (!IsActive && !IsPaused && IsMapLoaded)
                {
                    IsPaused = true;
                    UnlockMouse();
                }

                if (IsConsoleOpen && !IsPaused && PauseWhenMenusOpen) IsConsoleOpen = false;
                if (OptionsWindow.Opened && !IsPaused && PauseWhenMenusOpen) OptionsWindow.Close();
                if (LoadMenu.Opened && !IsPaused && PauseWhenMenusOpen) LoadMenu.Close();
                if (SaveMenu.Opened && !IsPaused && PauseWhenMenusOpen) SaveMenu.Close();

                if (IsConsoleOpen && !ConsoleWindow.Opened)
                {
                    ConsoleWindow.Open();
                }
                if (!IsConsoleOpen && ConsoleWindow.Opened)
                {
                    ConsoleWindow.Close();
                }

                if (IsConsoleOpen)
                {
                    ConsoleWindow.EnsureConsoleOnTop();
                }

                if (IsActive)
                {
                    BetterMouse.GetState(false);
                    KeyboardManager.GetState();

                    if (IsPaused) UnlockMouse(); else SetMouseToDefaultLockState();

                    if (!RawInput && IsCursorLocked)
                    {
                        var newPos = (Window.ClientBounds.Size.ToVector2() * 0.5f).ToPoint();
                        Mouse.SetPosition(newPos.X, newPos.Y);
                        BetterMouse.GetState(true);
                    }
                    else
                    {
                        SDLMouse.SetRelativeMode(IsCursorLocked);
                    }
                }

                EntityManager.UpdateEntities(gameTime);
                PhysicsEngine.Update();
                EntityManager.PostUpdateEntities(gameTime);
                ParticleManager.UpdateSystems();

                DebugDraw.Update(PreviousFrameDelta);

                SoundscapeManager.Update(PreviousFrameDelta, SoundDevice.Device.ListenerPosition);

                base.Update(gameTime);

                if (GraphicsDevice.Viewport.Width != Gum.CanvasWidth)
                    Gum.CanvasWidth = GraphicsDevice.Viewport.Width;

                if (GraphicsDevice.Viewport.Height != Gum.CanvasHeight)
                    Gum.CanvasHeight = GraphicsDevice.Viewport.Height;

                RenderEngine.Handle();

                CGUI.Flush();

                Gum.Update(gameTime);

                // Defer this by one frame so that all of the setup should hopefully happen before we render.
                if (willMapBeLoaded && !IsMapLoaded) IsMapLoaded = willMapBeLoaded;
            }
        }
        protected override void EndDraw()
        {
            using (RenderTimings.Section(TimingSection.Present))
            {
                base.EndDraw();
            }
        }
        protected void PrepWorldRender()
        {
            RenderEngine.PrepareRenderEngine();
            var vals = CurrentRealtimeLights.GetValues();
            ShaderRealtimeLightCount = int.Min(vals.Length, MaxRealtimeLights);

            for (int i = 0; i < ShaderRealtimeLightCount; i++)
            {
                ShaderRealtimeLightPositions[i] = new Vector4(vals[i].position, vals[i].range);
                ShaderRealtimeLightColors[i] = new Vector4(vals[i].color.ToVector3(), vals[i].intensity);
                ShaderRealtimeLightSpotData[i] = new Vector4(vals[i].rotation, MathHelper.ToRadians(vals[i].type == Light.LightType.Point ? -1 : vals[i].angle));
            }

            foreach (var shader in RenderEngine.LoadedWorldShaders)
            {
                shader.Param("realtimeLightCount")?.SetValue(ShaderRealtimeLightCount);
                shader.Param("realtimeLightPositions")?.SetValue(ShaderRealtimeLightPositions);
                shader.Param("realtimeLightColors")?.SetValue(ShaderRealtimeLightColors);
                shader.Param("realtimeLightSpotData")?.SetValue(ShaderRealtimeLightSpotData);
            }

            TerrainShader.Param("realtimeLightCount").SetValue(ShaderRealtimeLightCount);
            TerrainShader.Param("realtimeLightPositions").SetValue(ShaderRealtimeLightPositions);
            TerrainShader.Param("realtimeLightColors").SetValue(ShaderRealtimeLightColors);
            TerrainShader.Param("realtimeLightSpotData").SetValue(ShaderRealtimeLightSpotData);

            PropModelShader.Param("realtimeLightCount").SetValue(ShaderRealtimeLightCount);
            PropModelShader.Param("realtimeLightPositions").SetValue(ShaderRealtimeLightPositions);
            PropModelShader.Param("realtimeLightColors").SetValue(ShaderRealtimeLightColors);
            PropModelShader.Param("realtimeLightSpotData").SetValue(ShaderRealtimeLightSpotData);

            DecalManager.Shader.Param("realtimeLightCount")?.SetValue(ShaderRealtimeLightCount);
            DecalManager.Shader.Param("realtimeLightPositions")?.SetValue(ShaderRealtimeLightPositions);
            DecalManager.Shader.Param("realtimeLightColors")?.SetValue(ShaderRealtimeLightColors);
            DecalManager.Shader.Param("realtimeLightSpotData")?.SetValue(ShaderRealtimeLightSpotData);
        }
        protected void UpdateMapSpecificShaderVariables()
        {
            foreach(var shader in RenderEngine.LoadedWorldShaders)
            {
                shader.Param("fogColor")?.SetValue(FogColor.ToVector4());
                shader.Param("fogStart")?.SetValue(FogBeginDepth);
                shader.Param("fogEnd")?.SetValue(FogEndDepth);
                shader.Param("fogIntensity")?.SetValue(FogStrength);
            }

            TerrainShader.Param("fogColor")?.SetValue(FogColor.ToVector4());
            TerrainShader.Param("fogStart")?.SetValue(FogBeginDepth);
            TerrainShader.Param("fogEnd")?.SetValue(FogEndDepth);
            TerrainShader.Param("fogIntensity")?.SetValue(FogStrength);

            PropModelShader.Param("fogColor")?.SetValue(FogColor.ToVector4());
            PropModelShader.Param("fogStart")?.SetValue(FogBeginDepth);
            PropModelShader.Param("fogEnd")?.SetValue(FogEndDepth);
            PropModelShader.Param("fogIntensity")?.SetValue(FogStrength);
        }
        protected void EndWorldRender()
        {
            PostProcessingShader?.Param("screenSize")?.SetValue(GraphicsDevice.Viewport.Bounds.Size.ToVector2());

            RenderEngine.DisplayImageToScreen();

            Gum.Draw();

            guiRenderer.BeginLayout(new GameTime { ElapsedGameTime = TimeSpan.FromSeconds(PreviousFrameDelta) });

            DrawDebugUI();

            guiRenderer.EndLayout();

            if (splashScreen != null && splashScreen.IsActive)
            {
                splashScreen.Draw(SpriteBatch);
            }
        }

        public virtual void DrawDebugUI()
        {
            RenderEngine.RenderDebugUI();
            DrawDeveloperUI();
        }
        public void DrawDeveloperUI()
        {
            if (DeveloperMode != 0)
            {
                float vw = GraphicsDevice.Viewport.Width;
                float vh = GraphicsDevice.Viewport.Height;

                ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 10));
                ImGui.SetNextWindowSize(new System.Numerics.Vector2(vw, vh));
                ImGui.Begin("##devlog",
                    ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs |
                    ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoMove |
                    ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBackground);

                foreach (var item in Console.GetRecentLogs())
                {
                    if (item.level == GameConsole.LogLevel.Warning && DeveloperMode < 2) continue;

                    System.Numerics.Vector3 baseColor = item.level switch
                    {
                        GameConsole.LogLevel.Error => new(1, 0.1f, 0.1f),
                        GameConsole.LogLevel.Warning => new(0.8f, 0.8f, 0.1f),
                        _ => new(1, 1, 1)
                    };

                    float timeFade = 1 - (float.Max(item.time - 2, 0) * 0.5f);

                    var textSize = ImGui.CalcTextSize(item.log);
                    var pos = ImGui.GetCursorScreenPos();
                    var drawList = ImGui.GetWindowDrawList();
                    uint bgColor = ImGui.ColorConvertFloat4ToU32(new(0, 0, 0, 0.5f * timeFade));
                    drawList.AddRectFilled(pos, pos + textSize, bgColor);

                    ImGui.TextColored(new(baseColor, timeFade), item.log);
                }

                ImGui.End();

                Console.TickRecentLogs();
            }

            if(IsConsoleOpen)
            {
                float vw = GraphicsDevice.Viewport.Width;
                float vh = GraphicsDevice.Viewport.Height;

                string text = $"Chisel Release Build: {BuildInfo.BuildNumber}";

                float textWidth = ImGui.CalcTextSize(text).X;

                ImGui.SetNextWindowPos(new System.Numerics.Vector2(vw - textWidth - 24, 3));
                ImGui.SetNextWindowSize(new System.Numerics.Vector2(vw, vh));
                ImGui.Begin("##buildnum",
                    ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs |
                    ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoMove |
                    ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBackground);

                ImGui.Text(text);

                ImGui.End();
            }
        }
        protected override void Draw(GameTime gameTime)
        {
            if (IsLoading)
            {
                DrawLoadingScreen();
            }
            else
            {
                RenderEngine.Render(gameTime);

                DebugDraw.Draw();
            }

            CameraControl.Clear();

            base.Draw(gameTime);
        }
        private void DrawLoadingScreen()
        {
            //GraphicsDevice.Clear(Color.Black);
            guiRenderer.BeginLayout(new GameTime
            {
                ElapsedGameTime = TimeSpan.FromSeconds(PreviousFrameDelta)
            });

            float vw = GraphicsDevice.Viewport.Width;
            float vh = GraphicsDevice.Viewport.Height;
            const float wndW = 360f, wndH = 52f;

            ImGui.SetNextWindowPos(new System.Numerics.Vector2((vw - wndW) * 0.5f, (vh - wndH) * 0.5f));
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(wndW, wndH));
            ImGui.Begin("##loadscreen",
                ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs |
                ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBackground);

            string text = loadingStatusText;
            ImGui.SetCursorPosX((wndW - ImGui.CalcTextSize(text).X) * 0.5f);
            ImGui.Text(text);
            ImGui.End();
            guiRenderer.EndLayout();
        }

        public void UnloadMap()
        {
            SkyCamera.activeSkyCamera = null;
            if(BSPRoot.nodes != null) Array.Clear(BSPRoot.nodes);
            BSPRoot.nodes = null;
            IsMapLoaded = false;
            willMapBeLoaded = false;
            EntityManager.DespawnAllEntities();
            GlobalMapData.activeMap = new Map();
            ActiveStaticLights.Clear();
            Blockmap.Clear();

            FogBeginDepth = 1f; FogEndDepth = 10f; FogStrength = 0f; FogColor = Color.Transparent;
        }
        
        public void EnsureCommandsUpdated()
        {
            if (!commandsDirty) return;

            while (commands.TryDequeue(out CommandBinding cmd))
            {
                Console.RegisterCommand(cmd.commandName, cmd.command);
            }

            commandsDirty = false;
        }

        public virtual void OnMapLoaded()
        {
            UpdateMapSpecificShaderVariables();

            // Some types aren't loaded until this point, for some reason, so we can just double check.
            EnsureCommandsUpdated();

            SetMouseToDefaultLockState();
        }
        public static void SetMouseToDefaultLockState()
        {
            bool wasLocked = IsCursorLocked;
            IsCursorLocked = DefaultCursorLockState;
            Instance.IsMouseVisible = !IsCursorLocked;

            if (IsCursorLocked != wasLocked) Mouse.SetPosition(Width / 2, Height / 2);
        }
        public static void UnlockMouse()
        {
            bool wasLocked = IsCursorLocked;
            IsCursorLocked = false;
            Instance.IsMouseVisible = true;

            if (IsCursorLocked != wasLocked) Mouse.SetPosition(Width / 2, Height / 2);
        }
        protected override void UnloadContent()
        {
            Collision.CleanUp();

            PhysicsEngine.Shutdown();
            Foundation.Shutdown();

            SoundDevice.Device.Dispose();

            GameSettings.SaveSettings();

            base.UnloadContent();
        }
        public virtual void LoadMap(string path, bool disableEntitySpawning = false, Action? onComplete = null)
        {
            postLoadCallback = onComplete;

            CubemapHandler.brushCubemaps.Clear();
            EnvCubemap.Cubemaps.Clear();
            DecalManager.ClearAllDecals();
            SkyCamera.activeSkyCamera = null;
            Blockmap.Clear();
            ActiveStaticLights.Clear();

            ActiveMapPath = path;
            IsLoading = true;
            loadingException = null;
            loadingStatusText = "Loading...";           

            EntityManager.DespawnAllEntities();
            PhysicsEngine.Clear();
            CurrentRealtimeLights = new FixedList<Light>(MaxRealtimeLights);

            Task.Run(() =>
            {
#if !DEBUG
                try
#endif
                {
                    var pending = new PendingMapLoad
                    {
                        DisableEntitySpawning = disableEntitySpawning,
                        Path = path,
                    };

                    var mapData = MapFormatter.ReadMapData(Path.ChangeExtension(path, ".cmap"));
                    GlobalMapData.activeMap = mapData.map;
                    BSPRoot.nodes = mapData.bspFile.nodes;
                    LoadedMapHasVis = false;
                    if (mapData.map.hasVis)
                    {
                        VisRoot.visLeaves = mapData.visFile.leaves;
                        VisRoot.visPortals = mapData.visFile.portals;
                        LoadedMapHasVis = true;
                    }
                    OctreeRoot.allNodes = GlobalMapData.activeMap.octreeNodes;

                    DirectionalLightColor = Color.Black;
                    DirectionalLightStrength = 0.0f;
                    AmbientSkyColor = Color.White;
                    AmbientSkylightStrength = 1.0f;

                    if (GlobalMapData.activeMap.terrains != null && GlobalMapData.activeMap.terrains.Length > 0)
                    {
                        for (int i = 0; i < GlobalMapData.activeMap.terrains.Length; i++)
                        {
                            GlobalMapData.activeMap.terrains[i].surface = GlobalMapData.materialNameToIndex[GlobalMapData.activeMap.terrains[i].surfaceName];
                            GlobalMapData.activeMap.terrains[i].blendedSurface = GlobalMapData.materialNameToIndex[GlobalMapData.activeMap.terrains[i].blendedSurfaceName];

                            pending.UsedMaterialIndices.Add(GlobalMapData.activeMap.terrains[i].surface);
                            pending.UsedMaterialIndices.Add(GlobalMapData.activeMap.terrains[i].blendedSurface);
                        }
                    }

                    if (GlobalMapData.activeMap.brushes.Length > 0)
                    {
                        for (int b = 0; b < GlobalMapData.activeMap.brushes.Length; b++)
                        {
                            Brush brush = GlobalMapData.activeMap.brushes[b];
                            var verts = new VertexLightmapped[brush.vertices.Length];
                            for (int i = 0; i < verts.Length; i++)
                                verts[i] = new VertexLightmapped(brush.vertices[i], Vector3.Forward,
                                    brush.uvs[i], brush.lightmapUvs == null ? Vector2.Zero : brush.lightmapUvs[i]);

                            var faceUploads = new List<(int faceIdx, ushort[] indices)>();
                            for (int f = 0; f < brush.faces.Length; f++)
                            {
                                if (!brush.faces[f].drawn) continue;
                                brush.faces[f].plane ??= new Plane(brush.faces[f].normal,
                                    brush.vertices[brush.faces[f].indices[0]] + brush.position);
                                if (!string.IsNullOrEmpty(brush.faces[f].materialName) &&
                                    GlobalMapData.loadedMaterials[brush.faces[f].surface].name != brush.faces[f].materialName)
                                {
                                    brush.faces[f].surface =
                                        GlobalMapData.materialNameToIndex.ContainsKey(brush.faces[f].materialName)
                                        ? GlobalMapData.materialNameToIndex[brush.faces[f].materialName]
                                        : int.MaxValue;
                                }

                                if (brush.faces[f].surface >= 0 && brush.faces[f].surface < GlobalMapData.loadedMaterials.Length)
                                {
                                    pending.UsedMaterialIndices.Add(brush.faces[f].surface);
                                }

                                for (int i = 0; i < brush.faces[f].indices.Length; i++)
                                {
                                    verts[brush.faces[f].indices[i]].Normal = brush.faces[f].normal;
                                    verts[brush.faces[f].indices[i]].Tangent = brush.faces[f].tangent;
                                    verts[brush.faces[f].indices[i]].Binormal = brush.faces[f].binormal;
                                }
                                faceUploads.Add((f, brush.faces[f].indices.Select(i => (ushort)i).ToArray()));
                            }
                            pending.BrushUploads.Add(new PendingMapLoad.BrushUpload(b, verts, faceUploads.ToArray()));
                        }
                    }

                    using (var fs = new FileStream(Path.ChangeExtension(path, ".clm"), FileMode.Open))
                    using (var zip = new ZipArchive(fs, ZipArchiveMode.Read, false))
                    {
                        pending.IndexB1Bytes = StreamToBytes(zip.GetEntry("index-b1.hdr").Open());
                        pending.IndexB2Bytes = StreamToBytes(zip.GetEntry("index-b2.hdr").Open());
                        pending.IndexB3Bytes = StreamToBytes(zip.GetEntry("index-b3.hdr").Open());

                        var boundsEntry = zip.GetEntry("lightgroups.json");
                        if (boundsEntry != null)
                        {
                            string json = new StreamReader(boundsEntry.Open()).ReadToEnd();
                            pending.GroupBounds = JsonConvert.DeserializeObject<List<LightGroupBounds>>(json) ?? new();

                            foreach (var bounds in pending.GroupBounds)
                            {
                                var groupEntry = zip.GetEntry($"lightgroup-{bounds.name}.hdr");
                                if (groupEntry == null) continue;
                                pending.GroupLayerBytes[bounds.name] = StreamToBytes(groupEntry.Open());
                            }
                        }
                    }

                    if (!disableEntitySpawning)
                    {
                        foreach (var entityRef in GlobalMapData.activeMap.entities)
                        {
                            if (entityRef.entityName == null) continue;
                            if (!EntityCompiler.EntityLookupTable.ContainsKey(entityRef.entityName)) continue;

                            pending.EntityRefs.Add(entityRef);
                        }
                    }

                    if (GlobalMapData.activeMap.leafPolygons != null)
                    {
                        foreach (var leaf in GlobalMapData.activeMap.leafPolygons)
                        {
                            if (!GlobalMapData.materialNameToIndex.TryGetValue(leaf.MaterialName, out int surf))
                            {
                                surf = 0;
                            }
                            leaf.MaterialID = surf;
                            pending.UsedMaterialIndices.Add(surf);
                        }
                    }

                    if (GlobalMapData.activeMap.mapModels != null)
                    {
                        foreach (var mdl in GlobalMapData.activeMap.mapModels)
                        {
                            if (GlobalMapData.materialNameToIndex.TryGetValue(mdl.Material, out int surf))
                            {
                                pending.UsedMaterialIndices.Add(surf);
                            }
                        }
                    }

                    MapModelManager.SetModels(GlobalMapData.activeMap.mapModels);

                    Array.Clear(GlobalMapData.activeMap.entities);
                    pendingMapLoad = pending;
                }
#if !DEBUG
                catch (Exception ex)
                {
                    loadingException = ex;
                    IsLoading = false;
                    IsMapLoaded = false;
                    willMapBeLoaded = false;
                }
#endif
            });
        }

        private void FinishMapLoad(PendingMapLoad pending)
        {
            foreach (var bu in pending.BrushUploads)
            {
                var vb = new VertexBuffer(GraphicsDevice, typeof(VertexLightmapped), bu.Verts.Length, BufferUsage.WriteOnly);
                vb.SetData(bu.Verts);
                GlobalMapData.activeMap.brushes[bu.BrushIdx].brushVertexBuffer = vb;
                var faces = GlobalMapData.activeMap.brushes[bu.BrushIdx].faces;

                foreach (var (faceIdx, indices) in bu.Faces)
                {
                    var ib = new IndexBuffer(GraphicsDevice, typeof(ushort), indices.Length, BufferUsage.WriteOnly);
                    ib.SetData(indices);
                    GlobalMapData.activeMap.brushes[bu.BrushIdx].faces[faceIdx].faceIndices = ib;

                    if (faces[faceIdx].surface > GlobalMapData.loadedMaterials.Length) continue;

                    // This may look weird, but brushes with ANY face that have
                    // planar reflections HAVE to render face-by-face.
                    GlobalMapData.activeMap.brushes[bu.BrushIdx].renderPiecewise |= 
                        GlobalMapData.loadedMaterials[faces[faceIdx].surface].GetFlag("receivePlanarReflection");
                }

                if (GlobalMapData.activeMap.brushes[bu.BrushIdx].renderPiecewise) continue;

                List<MatGroup> groups = new List<MatGroup>();

                var gface = bu.Faces.GroupBy(f => faces[f.FaceIdx].surface);
                foreach(var group in gface)
                {
                    var indices = group.SelectMany(f=>f.Indices).ToArray();
                    var mgroup = new MatGroup
                    {
                        MaterialID = group.Key,
                        IndexBuffer = new IndexBuffer(GraphicsDevice, IndexElementSize.SixteenBits, indices.Length, BufferUsage.WriteOnly)
                    };
                    mgroup.IndexBuffer.SetData(indices);
                    groups.Add(mgroup);
                }

                GlobalMapData.activeMap.brushes[bu.BrushIdx].matGroups = groups.ToArray();
            }

            for(int i = 0; i < GlobalMapData.activeMap.terrains.Length; i++)
            {
                ref var terrain = ref GlobalMapData.activeMap.terrains[i];

                terrain.leafBits ??= Bitset.Create(BSPRoot.nodes.Length);
                Array.Clear(terrain.leafBits);
                Collision.GatherLeaves(0, terrain.bounds, terrain.leafBits);
            }

            var basis1 = RadianceHdrLoader.FromStream(GraphicsDevice, new MemoryStream(pending.IndexB1Bytes));
            var basis2 = RadianceHdrLoader.FromStream(GraphicsDevice, new MemoryStream(pending.IndexB2Bytes));
            var basis3 = RadianceHdrLoader.FromStream(GraphicsDevice, new MemoryStream(pending.IndexB3Bytes));

            LightGroupRuntime.ResetForNewMap();
            LightGroupRuntime.SetGroupKeyOrder(GlobalMapData.activeMap.lightGroupKeys);
            LightGroupRuntime.SetIndexLayer(GraphicsDevice,basis1, basis3, basis2);

            foreach (var bounds in pending.GroupBounds)
            {
                if (!pending.GroupLayerBytes.TryGetValue(bounds.name, out var bytes)) continue;
                var groupTex = RadianceHdrLoader.FromStream(GraphicsDevice, new MemoryStream(bytes));
                LightGroupRuntime.LoadLayer(bounds.name, groupTex, bounds.uvMin, bounds.uvMax);
            }

            RenderEngine.CheckInWorldTextures(LightGroupRuntime.CurrentB1, LightGroupRuntime.CurrentB2, LightGroupRuntime.CurrentB3);

            if (!pending.DisableEntitySpawning)
            {
                var allConstructed = new List<WorldEntity>();

                for (int refIdx = 0; refIdx < pending.EntityRefs.Count; refIdx++)
                {
                    var entityRef = pending.EntityRefs[refIdx];
                    var ent = (WorldEntity)Activator.CreateInstance(EntityCompiler.EntityLookupTable[entityRef.entityName]);
                    ent.properties = entityRef.properties;
                    ent.Name = entityRef.name;
                    ent.EntityOutputs = BuildOutputDict(entityRef.entityOutputs);

                    allConstructed.Add(ent);

                    if (entityRef.brushIndices != null && entityRef.brushIndices.Count > 0)
                    {
                        var brushEnt = (BrushEntity)ent;
                        brushEnt.brushSet = entityRef.brushIndices.ToArray();

                        ent.Position = GlobalMapData.activeMap.brushes[brushEnt.PrimaryBrush].position;
                        ent.Rotation = Quaternion.Identity;
                        ent.Scale = Vector3.One;

                        foreach (var bi in brushEnt.brushSet)
                        {
                            if (bi < 0 || bi >= GlobalMapData.activeMap.brushes.Length) continue;
                            GlobalMapData.activeMap.brushes[bi].entity = brushEnt;
                        }

                        pending.BrushEntities.Enqueue(ent);
                    }
                    else
                    {
                        ent.Position = entityRef.position;
                        ent.Rotation = Quaternion.CreateFromYawPitchRoll(
                            MathHelper.ToRadians(entityRef.spawnRotation.X),
                            MathHelper.ToRadians(entityRef.spawnRotation.Y),
                            MathHelper.ToRadians(entityRef.spawnRotation.Z));
                        ent.SpawnRotation = entityRef.spawnRotation;
                        ent.Scale = entityRef.scale;
                        pending.PointEntities.Add(ent);
                    }
                }

                var nameToEntity = new Dictionary<string, WorldEntity>();
                foreach (var ent in allConstructed)
                {
                    if (!string.IsNullOrEmpty(ent.Name) && !nameToEntity.ContainsKey(ent.Name))
                        nameToEntity[ent.Name] = ent;
                }

                for (int refIdx = 0; refIdx < pending.EntityRefs.Count; refIdx++)
                {
                    var entityRef = pending.EntityRefs[refIdx];
                    if (entityRef.brushIndices == null || entityRef.brushIndices.Count == 0) continue;
                    if (string.IsNullOrEmpty(entityRef.entityMoveParentName)) continue;

                    if (nameToEntity.TryGetValue(entityRef.entityMoveParentName, out var parent))
                    {
                        allConstructed[refIdx].MoveParent = parent;
                    }
                    else
                    {
                        Instance.Console.WriteDirect($"MoveParent target '{entityRef.entityMoveParentName}' not found for entity '{entityRef.name}'", GameConsole.LogLevel.Warning);
                    }
                }

                EntityManager.ResolveMoveParentsAndConvertToLocal(allConstructed);

                foreach (var ent in pending.PointEntities)
                {
                    EntityManager.SpawnEntity(ent);
                }
                while (pending.BrushEntities.Count > 0)
                {
                    EntityManager.SpawnEntity(pending.BrushEntities.Dequeue());
                }
            }

            EntityManager.NewLoad = true;

            TextureMipGenerator.SetMapMaterials(pending.UsedMaterialIndices);

            RenderEngine.CreateStaticGeometryBuffer(GlobalMapData.activeMap.staticGeomVertices);

            RenderEngine.OnMapLoaded();
            PhysicsEngine.OnMapLoaded();
            PreviousFrameDelta = 0f;
            IsLoading = false;
            willMapBeLoaded = true;
            OnMapLoaded(); SetMouseToDefaultLockState();
            IsPaused = false; IsConsoleOpen = false;

            var cb = postLoadCallback;
            postLoadCallback = null;
            cb?.Invoke();
        }

        private static byte[] StreamToBytes(Stream stream)
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }

        private static Dictionary<string, EntityOutput[]> BuildOutputDict(List<(string, EntityOutput)>? outputs)
        {
            var dict = new Dictionary<string, EntityOutput[]>();
            if (outputs == null) return dict;
            foreach (var (key, val) in outputs)
            {
                var output = val;
                WorldEntity.TryParseOutputScript(key, ref output);
                if (!dict.ContainsKey(key)) dict[key] = new[] { output };
                else dict[key] = dict[key].Append(output).ToArray();
            }
            return dict;
        }

        private sealed class PendingMapLoad
        {
            public required bool DisableEntitySpawning;
            public required string Path;

            public record struct BrushUpload(
                int BrushIdx,
                VertexLightmapped[] Verts,
                (int FaceIdx, ushort[] Indices)[] Faces);

            public List<BrushUpload> BrushUploads = new();
            public byte[] IndexB1Bytes = Array.Empty<byte>();
            public byte[] IndexB2Bytes = Array.Empty<byte>();
            public byte[] IndexB3Bytes = Array.Empty<byte>();
            public List<LightGroupBounds> GroupBounds = new();
            public Dictionary<string, byte[]> GroupLayerBytes = new();
            public List<WorldEntity> PointEntities = new();
            public Queue<WorldEntity> BrushEntities = new();
            public List<EntityReference> EntityRefs = new();
            public HashSet<int> UsedMaterialIndices = new();
        }
    }
}