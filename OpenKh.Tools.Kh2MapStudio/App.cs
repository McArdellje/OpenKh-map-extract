using Assimp;
using ImGuiNET;
using Microsoft.Xna.Framework.Input;
using OpenKh.Engine;
using OpenKh.Kh2;
using OpenKh.Tools.Common.CustomImGui;
using OpenKh.Tools.Kh2MapStudio.Windows;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Windows;
using Xe.Tools.Wpf.Dialogs;
using static OpenKh.Tools.Common.CustomImGui.ImGuiEx;
using xna = Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenKh.Engine.MonoGame;
using Matrix4x4 = Assimp.Matrix4x4;

namespace OpenKh.Tools.Kh2MapStudio
{
    class App : IDisposable
    {
        private static readonly List<FileDialogFilter> MapFilter =
            FileDialogFilterComposer.Compose()
            .AddExtensions("MAP file", "map")
            .AddAllFiles();
        private static readonly List<FileDialogFilter> ArdFilter =
            FileDialogFilterComposer.Compose()
            .AddExtensions("ARD file", "ard")
            .AddAllFiles();
        private static readonly List<FileDialogFilter> ModelFilter =
            FileDialogFilterComposer.Compose()
            .AddExtensions("glTF file (GL Transmission Format)", "gltf")
            .AddExtensions("FBX file", "fbx")
            .AddExtensions("DAE file (Collada)  (might be unaccurate)", "dae")
            .AddExtensions("OBJ file (Wavefront)  (might lose some information)", "obj")
            .AddAllFiles();

        private const string SelectArdFilesCaption = "Select ard files";

        private readonly Vector4 BgUiColor = new Vector4(0.0f, 0.0f, 0.0f, 0.5f);
        private readonly MonoGameImGuiBootstrap _bootstrap;
        private bool _exitFlag = false;

        private readonly Dictionary<Keys, Action> _keyMapping = new Dictionary<Keys, Action>();
        private readonly MapRenderer _mapRenderer;
        private string _gamePath;
        private string _mapName;
        private string _region;
        private string _ardPath;
        private string _mapPath;
        private string _objPath;
        private List<MapArdsBefore> _mapArdsList = new List<MapArdsBefore>();
        private ObjEntryController _objEntryController;

        private xna.Point _previousMousePosition;
        private MapArdsBefore _before;
        private MapArdsAfter _after;
        private SelectArdFilesState _selectArdFilesState = new SelectArdFilesState();

        private record MapArdsBefore(string MapName, string MapFile, IEnumerable<string> ArdFilesRelative)
        {

        }

        private record MapArdsAfter(string MapName, string MapFile, string ArdFileRelativeInput, IEnumerable<string> ArdFilesRelativeOutput)
        {

        }

        private class SelectArdFilesState
        {
            public string InputArd { get; set; }
            public List<string> OutputArds { get; set; } = new List<string>();

            public void Reset()
            {
                InputArd = null;
                OutputArds.Clear();
            }
        }

        public string Title
        {
            get
            {
                var mapName = _mapName != null ? $"{_mapName}@" : string.Empty;
                return $"{mapName}{_gamePath ?? "unloaded"} | {MonoGameImGuiBootstrap.ApplicationName}";
            }
        }

        private string GamePath
        {
            get => _gamePath;
            set
            {
                _gamePath = value;
                UpdateTitle();
                EnumerateMapList();

                _objEntryController?.Dispose();
                _objEntryController = new ObjEntryController(
                    _bootstrap.GraphicsDevice,
                    _objPath,
                    Path.Combine(_gamePath, "00objentry.bin"));
                _mapRenderer.ObjEntryController = _objEntryController;

                Settings.Default.GamePath = value;
                Settings.Default.Save();

            }
        }

        private string MapName
        {
            get => _mapName;
            set
            {
                _mapName = value;
                UpdateTitle();
            }
        }

        private void LoadMapArd(MapArdsAfter after)
        {
            MapName = after.MapName;

            _mapRenderer.Close();
            _mapRenderer.OpenMap(after.MapFile);
            _mapRenderer.OpenArd(Path.Combine(_ardPath, after.ArdFileRelativeInput));
            _after = after;
        }

        private bool IsGameOpen => !string.IsNullOrEmpty(_gamePath);
        private bool IsMapOpen => !string.IsNullOrEmpty(_mapName);
        private bool IsOpen => IsGameOpen && IsMapOpen;

        public App(MonoGameImGuiBootstrap bootstrap, string gamePath = null)
        {
            _bootstrap = bootstrap;
            _bootstrap.Title = Title;
            _mapRenderer = new MapRenderer(bootstrap.Content, bootstrap.GraphicsDeviceManager);
            AddKeyMapping(Keys.O, MenuFileOpen);
            AddKeyMapping(Keys.S, MenuFileSave);
            AddKeyMapping(Keys.Q, MenuFileUnload);

            if (string.IsNullOrEmpty(gamePath))
                gamePath = Settings.Default.GamePath;

            if (!string.IsNullOrEmpty(gamePath))
                OpenFolder(gamePath);

            ImGui.PushStyleColor(ImGuiCol.MenuBarBg, BgUiColor);
        }

        public bool MainLoop()
        {
            _bootstrap.GraphicsDevice.Clear(xna.Color.CornflowerBlue);
            ProcessKeyMapping();
            if (!_bootstrap.ImGuiWantTextInput)
                ProcessKeyboardInput(Keyboard.GetState(), 1f / 60);
            if (!_bootstrap.ImGuiWantCaptureMouse)
                ProcessMouseInput(Mouse.GetState());

            ImGui.PushStyleColor(ImGuiCol.WindowBg, BgUiColor);
            ForControl(ImGui.BeginMainMenuBar, ImGui.EndMainMenuBar, MainMenu);

            MainWindow();

            ForWindow("Tools", () =>
            {
                if (_mapRenderer.CurrentArea.AreaSettingsMask is int areaSettingsMask)
                {
                    ImGui.Text($"AreaSettings 0 -1");

                    for (int x = 0; x < 32; x++)
                    {
                        if ((areaSettingsMask & (1 << x)) != 0)
                        {
                            ImGui.Text($"AreaSettings {x} -1");
                        }
                    }
                }

                if (EditorSettings.ViewCamera)
                    CameraWindow.Run(_mapRenderer.Camera);
                if (EditorSettings.ViewLayerControl)
                    LayerControllerWindow.Run(_mapRenderer);
                if (EditorSettings.ViewSpawnPoint)
                    SpawnPointWindow.Run(_mapRenderer);
                if (EditorSettings.ViewMeshGroup)
                    MeshGroupWindow.Run(_mapRenderer.MapMeshGroups);
                if (EditorSettings.ViewBobDescriptor)
                    BobDescriptorWindow.Run(_mapRenderer.BobDescriptors, _mapRenderer.BobMeshGroups.Count);
                if (EditorSettings.ViewSpawnScriptMap)
                    SpawnScriptWindow.Run("map", _mapRenderer.SpawnScriptMap);
                if (EditorSettings.ViewSpawnScriptBattle)
                    SpawnScriptWindow.Run("btl", _mapRenderer.SpawnScriptBattle);
                if (EditorSettings.ViewSpawnScriptEvent)
                    SpawnScriptWindow.Run("evt", _mapRenderer.SpawnScriptEvent);

                if (_mapRenderer.EventScripts != null)
                {
                    foreach (var eventScript in _mapRenderer.EventScripts)
                    {
                        EventScriptWindow.Run(eventScript.Name, eventScript);
                    }
                }
            });

            SelectArdFilesPopup();

            ImGui.PopStyleColor();

            return _exitFlag;
        }

        public void Dispose()
        {
            _objEntryController?.Dispose();
        }

        private void SelectArdFilesPopup()
        {
            var dummy = true;
            if (ImGui.BeginPopupModal(SelectArdFilesCaption, ref dummy,
                ImGuiWindowFlags.Popup | ImGuiWindowFlags.Modal | ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Text("Select one ard file:");
                ImGui.Separator();
                ImGui.Text("Load from ard:");
                ForChild("loadFromArds", 120, 120, true, () =>
                {
                    foreach (var ard in _before.ArdFilesRelative)
                    {
                        if (ImGui.Selectable(ard, _selectArdFilesState.InputArd == ard, ImGuiSelectableFlags.DontClosePopups))
                        {
                            _selectArdFilesState.InputArd = ard;
                        }
                    }
                });
                ImGui.Separator();
                ImGui.Text("Save to ards:");
                ForChild("saveToArds", 120, 120, true, () =>
                {
                    foreach (var ard in _before.ArdFilesRelative)
                    {
                        if (ImGui.Selectable($"{ard}##save", _selectArdFilesState.OutputArds.Contains(ard), ImGuiSelectableFlags.DontClosePopups))
                        {
                            if (!_selectArdFilesState.OutputArds.Remove(ard))
                            {
                                _selectArdFilesState.OutputArds.Add(ard);
                            }
                        }
                    }
                });
                if (ImGui.Button("Select all"))
                {
                    _selectArdFilesState.OutputArds.Clear();
                    _selectArdFilesState.OutputArds.AddRange(_before.ArdFilesRelative);
                }
                ImGui.Separator();
                ImGui.BeginDisabled(_selectArdFilesState.InputArd == null || !_selectArdFilesState.OutputArds.Any());
                if (ImGui.Button("Proceed"))
                {
                    LoadMapArd(
                        new MapArdsAfter(
                            _before.MapName,
                            _before.MapFile,
                            _selectArdFilesState.InputArd,
                            _selectArdFilesState.OutputArds
                        )
                    );

                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndDisabled();
                ImGui.EndPopup();
            }
        }

        private void MainWindow()
        {
            if (!IsGameOpen)
            {
                ImGui.Text("Game content not loaded.");
                return;
            }

            ForControl(() =>
            {
                var nextPos = ImGui.GetCursorPos();
                var ret = ImGui.Begin("MapList",
                    ImGuiWindowFlags.NoDecoration |
                    ImGuiWindowFlags.NoCollapse |
                    ImGuiWindowFlags.NoMove);
                ImGui.SetWindowPos(nextPos);
                ImGui.SetWindowSize(new Vector2(64, 0));
                return ret;
            }, () => { }, (Action)(() =>
            {
                foreach (var mapArds in _mapArdsList)
                {
                    if (ImGui.Selectable(mapArds.MapName, MapName == mapArds.MapName))
                    {
                        if (mapArds.ArdFilesRelative.Count() == 1)
                        {
                            LoadMapArd(
                                new MapArdsAfter(
                                    mapArds.MapName,
                                    mapArds.MapFile,
                                    mapArds.ArdFilesRelative.Single(),
                                    mapArds.ArdFilesRelative
                                )
                            );
                        }
                        else
                        {
                            _before = mapArds;

                            _selectArdFilesState.Reset();

                            ImGui.OpenPopup(SelectArdFilesCaption);
                        }
                    }
                }
            }));
            ImGui.SameLine();

            if (!IsMapOpen)
            {
                ImGui.Text("Please select a map to edit.");
                return;
            }

            _mapRenderer.Update(1f / 60);
            _mapRenderer.Draw();
        }

        void MainMenu()
        {
            ForMenuBar(() =>
            {
                ForMenu("File", () =>
                {
                    ForMenuItem("Open extracted game folder...", "CTRL+O", MenuFileOpen);
                    ForMenuItem("Unload current map+ard", "CTRL+Q", MenuFileUnload, IsOpen);
                    ForMenuItem("Import extern MAP file", MenuFileOpenMap, IsGameOpen);
                    ForMenuItem("Import extern ARD file", MenuFileOpenArd, IsGameOpen);
                    ForMenuItem("Save map+ard", "CTRL+S", MenuFileSave, IsOpen);
                    ForMenuItem("Save map as...", MenuFileSaveMapAs, IsOpen);
                    ForMenuItem("Save ard as...", MenuFileSaveArdAs, IsOpen);
                    ImGui.Separator();
                    ForMenu("Export", () =>
                    {
                        ForMenuItem("Map Collision", ExportMapCollision, _mapRenderer.ShowMapCollision.HasValue);
                        ForMenuItem("Camera Collision", ExportCameraCollision, _mapRenderer.ShowCameraCollision.HasValue);
                        ForMenuItem("Light Collision", ExportLightCollision, _mapRenderer.ShowLightCollision.HasValue);
                        ForMenuItem("World Meshes", ExportWorldMeshesNonSliced);
                        ForMenuItem("World Meshes (Sliced Textures)", ExportWorldMeshesSliced);
                        ForMenuItem("Map Collision (Combined)", ExportCombinedMapCollision, _mapRenderer.ShowMapCollision.HasValue);
                    });
                    ImGui.Separator();
                    ForMenu("Preferences", () =>
                    {
                        ForEdit("Movement speed", () => EditorSettings.MoveSpeed, x => EditorSettings.MoveSpeed = x);
                        ForEdit("Movement speed (shift)", () => EditorSettings.MoveSpeedShift, x => EditorSettings.MoveSpeedShift = x);
                    });
                    ImGui.Separator();
                    ForMenuItem("Exit", MenuFileExit);
                });
                ForMenu("View", () =>
                {
                    ForMenuCheck("Camera", () => EditorSettings.ViewCamera, x => EditorSettings.ViewCamera = x);
                    ForMenuCheck("Layer control", () => EditorSettings.ViewLayerControl, x => EditorSettings.ViewLayerControl = x);
                    ForMenuCheck("Spawn points", () => EditorSettings.ViewSpawnPoint, x => EditorSettings.ViewSpawnPoint = x);
                    ForMenuCheck("BOB descriptors", () => EditorSettings.ViewBobDescriptor, x => EditorSettings.ViewBobDescriptor = x);
                    ForMenuCheck("Mesh group", () => EditorSettings.ViewMeshGroup, x => EditorSettings.ViewMeshGroup = x);
                    ForMenuCheck("Spawn script MAP", () => EditorSettings.ViewSpawnScriptMap, x => EditorSettings.ViewSpawnScriptMap = x);
                    ForMenuCheck("Spawn script BTL", () => EditorSettings.ViewSpawnScriptBattle, x => EditorSettings.ViewSpawnScriptBattle = x);
                    ForMenuCheck("Spawn script EVT", () => EditorSettings.ViewSpawnScriptEvent, x => EditorSettings.ViewSpawnScriptEvent = x);
                });
                ForMenu("Help", () =>
                {
                    ForMenuItem("About", ShowAboutDialog);
                });
            });
        }

        private void MenuFileOpen() => FileDialog.OnFolder(OpenFolder);
        private void MenuFileUnload() => _mapRenderer.Close();
        private void MenuFileOpenMap() => FileDialog.OnOpen(_mapRenderer.OpenMap, MapFilter);
        private void MenuFileOpenArd() => FileDialog.OnOpen(_mapRenderer.OpenArd, ArdFilter);

        private void MenuFileSave()
        {
            _mapRenderer.SaveMap(_after.MapFile);

            foreach (var ard in _after.ArdFilesRelativeOutput)
            {
                _mapRenderer.SaveArd(Path.Combine(_ardPath, ard));
            }
        }

        private void MenuFileSaveMapAs()
        {
            var defaultName = MapName + ".map";
            FileDialog.OnSave(_mapRenderer.SaveMap, MapFilter, defaultName);
        }

        private void MenuFileSaveArdAs()
        {
            var defaultName = MapName + ".ard";
            FileDialog.OnSave(_mapRenderer.SaveArd, ArdFilter, defaultName);
        }

        private void ExportMapCollision() => FileDialog.OnSave(fileName =>
        {
            ExportScene(fileName, _mapRenderer.MapCollision.Scene);
        }, ModelFilter, $"{MapName}_map-collision.dae");

        private void ExportCameraCollision() => FileDialog.OnSave(fileName =>
        {
            ExportScene(fileName, _mapRenderer.CameraCollision.Scene);
        }, ModelFilter, $"{MapName}_camera-collision.dae");

        private void ExportLightCollision() => FileDialog.OnSave(fileName =>
        {
            ExportScene(fileName, _mapRenderer.LightCollision.Scene);
        }, ModelFilter, $"{MapName}_light-collision.dae");

        private void ExportWorldMeshes(bool sliceTextures) => FileDialog.OnSave(fileName =>
        {
            static TextureWrapMode KHToAssimpWrapMode(ModelTexture.TextureWrapMode wrapMode)
            {
                switch (wrapMode)
                {
                    case ModelTexture.TextureWrapMode.Repeat:
                    case ModelTexture.TextureWrapMode.RegionRepeat:
                        return TextureWrapMode.Wrap;
                    case ModelTexture.TextureWrapMode.Clamp:
                    case ModelTexture.TextureWrapMode.RegionClamp:
                        return TextureWrapMode.Clamp;
                }
                throw new Exception($"Unknown texture wrap mode: {wrapMode}");
            }
            static bool KHWrapModeIsWholeTexture(ModelTexture.TextureWrapMode wrapMode)
            {
                switch (wrapMode)
                {
                    case ModelTexture.TextureWrapMode.Repeat:
                    case ModelTexture.TextureWrapMode.Clamp:
                        return true;
                    case ModelTexture.TextureWrapMode.RegionRepeat:
                    case ModelTexture.TextureWrapMode.RegionClamp:
                        return false;
                }
                throw new Exception($"Unknown texture wrap mode: {wrapMode}");
            }
            // open text file
            var textureWrapInfoFileName = Path.Combine(Path.GetDirectoryName(fileName) ?? throw new InvalidOperationException(), sliceTextures ? $"{MapName}-preSliced-texture-info.txt" : $"{MapName}-texture-info.txt");
            StreamWriter textureInfoFile = new StreamWriter(textureWrapInfoFileName);
            // create new assimp scene
            var scene = new Assimp.Scene();
            // iter over all textures
            // stores all Texture2Ds associated with this mesh
            var textures = new List<Texture2D>();
            // used if sliceTextures is true
            var textureRegionInfo = new List<Vector4>();
            scene.RootNode = new Assimp.Node("root");

            // Gets the raw data from a texture, applying slicing
            // returns the width and height of the texture and the data
            // regions assumed to be pre-multiplied by texture size
            (int, int, byte[]) GetSlicedTextureData(Texture2D texture, Vector2 regionU, Vector2 regionV)
            {
                // regionU and regionV are minimum and maximum pixel index in the width and height of the texture
                var rawData = new byte[texture.Width * texture.Height * 4];
                texture.GetData(rawData);
                // apply slicing
                var width = (int)(regionU.Y - regionU.X);
                var height = (int)(regionV.Y - regionV.X);
                var slicedData = new byte[width * height * 4];
                for (int y = 0; y < height; y++)
                {
                    Array.Copy(rawData, (int)regionU.X * 4 + (int)(regionV.X + y) * texture.Width * 4, slicedData, y * width * 4, width * 4);
                }

                return (width, height, slicedData);
            }

            // regions assumed to be pre-multiplied by texture size
            Texture2D SliceTexture(Texture2D texture, Vector2 regionU, Vector2 regionV)
            {
                var (width, height, data) = GetSlicedTextureData(texture, regionU, regionV);
                var slicedTexture = new Texture2D(_bootstrap.GraphicsDevice, width, height);
                slicedTexture.SetData(data);
                return slicedTexture;
            }

            int FindTextureIndex(Texture2D texture)
            {
                var textureData = new byte[texture.Width * texture.Height * 4];
                texture.GetData(textureData);
                for (int i = 0; i < textures.Count; i++)
                {
                    if (textures[i] == texture)
                        return i;
                    
                    // optimization: check if texture has same size
                    if (textures[i].Width != texture.Width || textures[i].Height != texture.Height)
                    {
                        continue;
                    }

                    // check if texture is identical
                    var compareData = new byte[textures[i].Width * textures[i].Height * 4];
                    textures[i].GetData(compareData);
                    if (textureData.SequenceEqual(compareData))
                        return i;
                }

                // not found
                return -1;
            }
            void DoMeshGroup(MeshGroup meshGroup, ModelBackground map, Node groupNode, int groupIdx, string prefix) {
                var textureIndexMapping = new List<int>();
                // contains all meshes, each mesh has a unique material defined by meshTextureDefs
                var meshes = new List<Assimp.Mesh>();
                var meshTextureDefs = new List<IKingdomTexture>();
                var meshChunkDefs = new List<ModelBackground.ModelChunk>();
                static bool ModelChunkEquality(ModelBackground.ModelChunk a, ModelBackground.ModelChunk b)
                {
                    return a.IsAlpha == b.IsAlpha && a.IsAlphaAdd == b.IsAlphaAdd && a.IsAlphaSubtract == b.IsAlphaSubtract;
                } 
                int FindMeshIndex(IKingdomTexture textureDef, ModelBackground.ModelChunk chunkDef)
                {
                    for (var i = 0; i < meshes.Count; i++)
                    {
                        if (meshTextureDefs[i] == textureDef && (chunkDef == null || ModelChunkEquality(meshChunkDefs[i], chunkDef)))
                        {
                            return i;
                        }
                    }
                    return -1;
                }

                // add each texture directly, skipping duplicates
                foreach (var kingdomTexture in meshGroup.Textures) {
                    Texture2D texture;
                    if (sliceTextures)
                    {
                        var addrU = kingdomTexture.AddressU;
                        var addrV = kingdomTexture.AddressV;
                        var regU = kingdomTexture.RegionU;
                        var regV = kingdomTexture.RegionV;
                        regU.X *= kingdomTexture.Texture2D.Width;
                        regU.Y = regU.Y * kingdomTexture.Texture2D.Width;
                        regV.X *= kingdomTexture.Texture2D.Height;
                        regV.Y = regV.Y * kingdomTexture.Texture2D.Height;
                        if (KHWrapModeIsWholeTexture(addrU))
                            regU = new Vector2(0, kingdomTexture.Texture2D.Width);
                        if (KHWrapModeIsWholeTexture(addrV))
                            regV = new Vector2(0, kingdomTexture.Texture2D.Height);
                        texture = SliceTexture(
                            kingdomTexture.Texture2D,
                            regU,
                            regV
                        );
                    }
                    else
                    {
                        texture = kingdomTexture.Texture2D;
                    }
                    var idx = FindTextureIndex(texture);
                    if (idx == -1)
                    {
                        // add
                        textures.Add(texture);
                        idx = textures.Count - 1;
                    }

                    textureIndexMapping.Add(idx);
                }

                for (var meshIdx = 0; meshIdx < meshGroup.MeshDescriptors.Count; meshIdx++)
                {
                    var mesh = meshGroup.MeshDescriptors[meshIdx];
                    var rawTextureIndex = textureIndexMapping[mesh.TextureIndex & 0xFFFF];
                    var texture = meshGroup.Textures[mesh.TextureIndex & 0xFFFF];
                    var chunk = map?.Chunks[meshIdx];

                    // find identical texture
                    var textureDefIndex = FindMeshIndex(texture, chunk);
                    Assimp.Mesh assimpMesh;
                    if (textureDefIndex == -1)
                    {
                        int groupChildIdx = groupNode.Children.Count;
                        // add new mesh with its own material and texture def
                        assimpMesh = new Assimp.Mesh($"{prefix} Mesh {groupChildIdx}", Assimp.PrimitiveType.Triangle);
                        assimpMesh.MaterialIndex = scene.Materials.Count;
                        var assimpMaterial = new Assimp.Material();
                        assimpMaterial.AddMaterialTexture(new Assimp.TextureSlot($"{MapName}-texture{rawTextureIndex}.png", TextureType.Diffuse, 0, TextureMapping.FromUV, 0, 0, TextureOperation.Add, KHToAssimpWrapMode(texture.AddressU), KHToAssimpWrapMode(texture.AddressV), 0));
                        scene.Materials.Add(assimpMaterial);
                        // add to meshes and texture defs
                        meshes.Add(assimpMesh);
                        meshTextureDefs.Add(texture);
                        if (chunk != null)
                        {
                            meshChunkDefs.Add(chunk);
                        }

                        // add mesh to scene
                        scene.Meshes.Add(assimpMesh);
                        var node = new Assimp.Node($"{prefix} Mesh {groupChildIdx}");
                        node.MeshIndices.Add(scene.Meshes.Count - 1);
                        groupNode.Children.Add(node);
                        // add to texture info
                        var alphaInt = mesh.IsOpaque ? 1 : 0;
                        if (chunk != null)
                        {
                            alphaInt |= chunk.IsAlpha ? 2 : 0;
                            alphaInt |= chunk.IsAlphaAdd ? 4 : 0;
                            alphaInt |= chunk.IsAlphaSubtract ? 8 : 0;
                        }
                        else
                        {
                            alphaInt |= mesh.IsOpaque ? 0 : 2;
                        }
                        var priority = chunk?.Priority ?? -1;
                        var drawPriority = chunk?.DrawPriority ?? 0;
                        var regionU = texture.RegionU * texture.Texture2D.Width;
                        var regionV = texture.RegionV * texture.Texture2D.Height;
                        textureInfoFile.WriteLine(sliceTextures
                            // pre-sliced
                            ? $"{groupIdx},{groupChildIdx}:{MapName}-texture{rawTextureIndex}:{alphaInt}:{priority}:{drawPriority}:{KHToAssimpWrapMode(texture.AddressU)},{KHToAssimpWrapMode(texture.AddressV)}"
                            // not pre-sliced
                            : $"{groupIdx},{groupChildIdx}:{MapName}-texture{rawTextureIndex}:{alphaInt}:{priority}:{drawPriority}:{regionU.X},{regionU.Y}:{regionV.X},{regionV.Y}:{texture.AddressU},{texture.AddressV}"
                        );
                    }
                    else
                    {
                        assimpMesh = meshes[textureDefIndex];
                    }

                    var vertOfs = assimpMesh.Vertices.Count;

                    static float ApplyUVOffsets(float coord, ModelTexture.TextureWrapMode wm, Vector2 region)
                    {
                        if (!KHWrapModeIsWholeTexture(wm))
                        {
                            return (coord - region.X) / (region.Y - region.X);
                        }

                        return coord;
                    }

                    for (var i = 0; i < mesh.Vertices.Length; i++)
                    {
                        assimpMesh.Vertices.Add(new Vector3D(mesh.Vertices[i].X, mesh.Vertices[i].Y, mesh.Vertices[i].Z));
                        assimpMesh.VertexColorChannels[0].Add(new Color4D(mesh.Vertices[i].R, mesh.Vertices[i].G, mesh.Vertices[i].B, mesh.Vertices[i].A));
                        assimpMesh.TextureCoordinateChannels[0].Add(new Vector3D(
                            ApplyUVOffsets(mesh.Vertices[i].Tu, texture.AddressU, texture.RegionU), 
                            1 - ApplyUVOffsets(mesh.Vertices[i].Tv, texture.AddressV, texture.RegionV),
                            mesh.Vertices[i].A
                            ));
                        assimpMesh.TextureCoordinateChannels[1].Add(new Vector3D(mesh.Vertices[i].A, 0, 0));
                    }
                    for (var i = 0; i < mesh.Indices.Length; i += 3)
                    {
                        assimpMesh.Faces.Add(new Face(new int[] { mesh.Indices[i] + vertOfs, mesh.Indices[i + 1] + vertOfs, mesh.Indices[i + 2] + vertOfs }));
                    }
                }
                /*
                for (int meshIdx = 0; meshIdx < meshGroup.MeshDescriptors.Count; meshIdx++)
                {
                    var mesh = meshGroup.MeshDescriptors[meshIdx];
                    int rawTextureIndex = textureIndexMapping[mesh.TextureIndex];
                    var texture = meshGroup.Textures[mesh.TextureIndex];

                    // create new mesh
                    var assimpMesh = new Assimp.Mesh($"Group {groupIdx} Mesh {meshIdx}", Assimp.PrimitiveType.Triangle);
                    assimpMesh.MaterialIndex = meshIdx;
                    var assimpMaterial = new Assimp.Material();
                    assimpMaterial.AddMaterialTexture(new Assimp.TextureSlot($"{MapName}-texture{rawTextureIndex}.png", TextureType.Diffuse, 0, TextureMapping.FromUV, 0, 0, TextureOperation.Add, KHToAssimpWrapMode(texture.AddressU), KHToAssimpWrapMode(texture.AddressV), 0));
                    scene.Materials.Add(assimpMaterial);
                    // get length of vertex data
                    assimpMesh.Vertices.Capacity = mesh.Vertices.Length;
                    assimpMesh.VertexColorChannels[0].Capacity = mesh.Vertices.Length;
                    assimpMesh.TextureCoordinateChannels[0].Capacity = mesh.Vertices.Length;
                    assimpMesh.Vertices.Clear();
                    assimpMesh.VertexColorChannels[0].Clear();
                    assimpMesh.TextureCoordinateChannels[0].Clear();
                    // add vertices
                    for (int i = 0; i < mesh.Vertices.Length; i++)
                    {
                        assimpMesh.Vertices.Add(new Vector3D(mesh.Vertices[i].X, mesh.Vertices[i].Y, mesh.Vertices[i].Z));
                        assimpMesh.VertexColorChannels[0].Add(new Color4D(mesh.Vertices[i].R, mesh.Vertices[i].G, mesh.Vertices[i].B, mesh.Vertices[i].A));
                        assimpMesh.TextureCoordinateChannels[0].Add(new Vector3D(mesh.Vertices[i].Tu, mesh.Vertices[i].Tv, 0));
                    }
                    // add indices
                    assimpMesh.Faces.Capacity = mesh.Indices.Length / 3;
                    for (int i = 0; i < mesh.Indices.Length; i += 3)
                    {
                        assimpMesh.Faces.Add(new Face(new int[] { mesh.Indices[i], mesh.Indices[i + 1], mesh.Indices[i + 2] }));
                    }
                    // add mesh to scene
                    scene.Meshes.Add(assimpMesh);
                    // add mesh to node
                    groupNode.MeshIndices.Add(scene.Meshes.Count - 1);
                    // debug log
                    var isOpaqueInt = mesh.IsOpaque ? 1 : 0;
                    textureInfoFile.WriteLine($"{groupIdx},{meshIdx}:{MapName}-texture{rawTextureIndex}:{isOpaqueInt}:{texture.RegionU.X},{texture.RegionU.Y}:{texture.RegionV.X},{texture.RegionV.Y}:{texture.AddressU},{texture.AddressV}");
                }
                */
            }

            for (var groupIdx = 0; groupIdx < _mapRenderer.MapMeshGroups.Count; groupIdx++)
            {
                System.Diagnostics.Debug.WriteLine($"Mesh Group {groupIdx}");
                // create new node
                var groupNode = new Assimp.Node($"{groupIdx} Mesh Group {groupIdx}");
                // add node to scene
                scene.RootNode.Children.Add(groupNode);
                // get mesh group
                var meshGroup = _mapRenderer.MapMeshGroups[groupIdx].MeshGroup;
                var map = _mapRenderer.MapMeshGroups[groupIdx].Map;
                if (map.Chunks.Count != meshGroup.MeshDescriptors.Count)
                {
                    throw new Exception("map.Chunks.Count != meshGroup.MeshDescriptors.Count");
                }
                DoMeshGroup(meshGroup, map, groupNode, groupIdx, $"Group {groupIdx}");
            }


            for (var bobIdx = 0; bobIdx < _mapRenderer.BobDescriptors.Count; bobIdx++)
            {
                var bob = _mapRenderer.BobDescriptors[bobIdx];
                var mesh = _mapRenderer.BobMeshGroups[bob.BobIndex];
                var bobNodeIdx = _mapRenderer.MapMeshGroups.Count + bobIdx;
                var bobNode = new Assimp.Node($"{bobNodeIdx} BOB {bobIdx}");
                scene.RootNode.Children.Add(bobNode);
                bobNode.Transform = Matrix4x4.FromRotationX(bob.RotationX) *
                                    Matrix4x4.FromRotationY(bob.RotationY) *
                                    Matrix4x4.FromRotationZ(bob.RotationZ) *
                                    Matrix4x4.FromScaling(new Vector3D(bob.ScalingX, bob.ScalingY, bob.ScalingZ)) *
                                    Matrix4x4.FromTranslation(new Vector3D(bob.PositionX, -bob.PositionY, -bob.PositionZ));
                DoMeshGroup(mesh.MeshGroup, null, bobNode, bobNodeIdx, $"BOB {bobIdx}");
            }

            textureInfoFile.Close();
            // save scene
            ExportScene(fileName, scene);
            // get directory
            var dir = Path.GetDirectoryName(fileName);
            // save textures
            for (int i = 0; i < textures.Count; i++)
            {
                var texture = textures[i];
                var path = Path.Combine(dir, $"{MapName}-texture{i}.png");
                // open file stream
                using var fs = File.OpenWrite(path);
                // save texture
                texture.SaveAsPng(fs, texture.Width, texture.Height);
            }
        }, ModelFilter, $"{MapName}-world.dae");

        private void ExportWorldMeshesNonSliced() => ExportWorldMeshes(false);
        private void ExportWorldMeshesSliced() => ExportWorldMeshes(true);

        private void ExportCombinedMapCollision() => FileDialog.OnSave(fileName =>
        {
            var coct = _mapRenderer.MapCollision.Coct;
            var scene = new Assimp.Scene();
            scene.RootNode = new Assimp.Node("root");
            var faceIndex = 0;
            var nodeMesh = new Assimp.Mesh($"collision", Assimp.PrimitiveType.Triangle);

            for (int i = 0; i < coct.Nodes.Count; i++)
            {
                var meshGroup = coct.Nodes[i];
                foreach (var mesh in meshGroup.Meshes)
                {

                    foreach (var item in mesh.Collisions)
                    {
                        var v1 = coct.VertexList[item.Vertex1];
                        var v2 = coct.VertexList[item.Vertex2];
                        var v3 = coct.VertexList[item.Vertex3];

                        if (item.Vertex4 >= 0)
                        {
                            var v4 = coct.VertexList[item.Vertex4];
                            nodeMesh.Vertices.Add(new Vector3D(v1.X, v1.Y, v1.Z));
                            nodeMesh.Vertices.Add(new Vector3D(v2.X, v2.Y, v2.Z));
                            nodeMesh.Vertices.Add(new Vector3D(v3.X, v3.Y, v3.Z));
                            nodeMesh.Vertices.Add(new Vector3D(v1.X, v1.Y, v1.Z));
                            nodeMesh.Vertices.Add(new Vector3D(v3.X, v3.Y, v3.Z));
                            nodeMesh.Vertices.Add(new Vector3D(v4.X, v4.Y, v4.Z));
                            nodeMesh.Faces.Add(new Assimp.Face(new int[]
                            {
                                faceIndex++, faceIndex++, faceIndex++,
                                faceIndex++, faceIndex++, faceIndex++
                            }));
                        }
                        else
                        {
                            nodeMesh.Vertices.Add(new Vector3D(v1.X, v1.Y, v1.Z));
                            nodeMesh.Vertices.Add(new Vector3D(v2.X, v2.Y, v2.Z));
                            nodeMesh.Vertices.Add(new Vector3D(v3.X, v3.Y, v3.Z));
                            nodeMesh.Faces.Add(new Assimp.Face(new int[]
                            {
                                faceIndex++, faceIndex++, faceIndex++
                            }));
                        }
                        
                    }
                }
            }
            scene.Meshes.Add(nodeMesh);
            scene.RootNode.MeshIndices.Add(scene.Meshes.Count - 1);
            // save scene
            ExportScene(fileName, scene);
        }, ModelFilter, $"{MapName}_map-collision.dae");

        private void MenuFileExit() => _exitFlag = true;

        public void OpenFolder(string gamePath)
        {
            try
            {
                if (!Directory.Exists(_ardPath = Path.Combine(gamePath, "ard")) ||
                    !Directory.Exists(_mapPath = Path.Combine(gamePath, "map")) ||
                    !Directory.Exists(_objPath = Path.Combine(gamePath, "obj")))
                    throw new DirectoryNotFoundException(
                        "The specified directory must contain the full extracted copy of the game.");

                GamePath = gamePath;
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        }

        private void UpdateTitle()
        {
            _bootstrap.Title = Title;
        }

        private void EnumerateMapList()
        {
            var mapFiles = Array.Empty<string>();
            foreach (var region in Constants.Regions)
            {
                var testPath = Path.Combine(_mapPath, region);
                if (Directory.Exists(testPath))
                {
                    mapFiles = Directory.GetFiles(testPath, "*.map");
                    if (mapFiles.Length != 0)
                    {
                        _mapPath = testPath;
                        _region = region;
                        break;
                    }
                }
            }

            _mapArdsList.Clear();

            foreach (var mapFile in mapFiles)
            {
                var mapName = Path.GetFileNameWithoutExtension(mapFile);

                var ardFiles = Constants.Regions
                    .Select(region => Path.Combine(region, $"{mapName}.ard"))
                    .Where(it => File.Exists(Path.Combine(_ardPath, it)))
                    .ToArray();

                _mapArdsList.Add(new MapArdsBefore(mapName, mapFile, ardFiles));
            }
        }

        private void AddKeyMapping(Keys key, Action action)
        {
            _keyMapping[key] = action;
        }

        private void ProcessKeyMapping()
        {
            var k = Keyboard.GetState();
            if (k.IsKeyDown(Keys.LeftControl))
            {
                var keys = k.GetPressedKeys();
                foreach (var key in keys)
                {
                    if (_keyMapping.TryGetValue(key, out var action))
                        action();
                }
            }
        }

        private void ProcessKeyboardInput(KeyboardState keyboard, float deltaTime)
        {
            var speed = (float)(deltaTime * EditorSettings.MoveSpeed);
            var moveSpeed = speed;
            if (keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift))
                moveSpeed = (float)(deltaTime * EditorSettings.MoveSpeedShift);

            var camera = _mapRenderer.Camera;
            if (keyboard.IsKeyDown(Keys.W))
                camera.CameraPosition += Vector3.Multiply(camera.CameraLookAtX, moveSpeed * 5);
            if (keyboard.IsKeyDown(Keys.S))
                camera.CameraPosition -= Vector3.Multiply(camera.CameraLookAtX, moveSpeed * 5);
            if (keyboard.IsKeyDown(Keys.D))
                camera.CameraPosition -= Vector3.Multiply(camera.CameraLookAtY, moveSpeed * 5);
            if (keyboard.IsKeyDown(Keys.A))
                camera.CameraPosition += Vector3.Multiply(camera.CameraLookAtY, moveSpeed * 5);
            if (keyboard.IsKeyDown(Keys.Q))
                camera.CameraPosition += Vector3.Multiply(camera.CameraLookAtZ, moveSpeed * 5);
            if (keyboard.IsKeyDown(Keys.E))
                camera.CameraPosition -= Vector3.Multiply(camera.CameraLookAtZ, moveSpeed * 5);

            if (keyboard.IsKeyDown(Keys.Up))
                camera.CameraRotationYawPitchRoll += new Vector3(0, 0, 1 * speed);
            if (keyboard.IsKeyDown(Keys.Down))
                camera.CameraRotationYawPitchRoll -= new Vector3(0, 0, 1 * speed);
            if (keyboard.IsKeyDown(Keys.Left))
                camera.CameraRotationYawPitchRoll += new Vector3(1 * speed, 0, 0);
            if (keyboard.IsKeyDown(Keys.Right))
                camera.CameraRotationYawPitchRoll -= new Vector3(1 * speed, 0, 0);
        }

        private void ProcessMouseInput(MouseState mouse)
        {
            const float Speed = 0.25f;
            if (mouse.LeftButton == ButtonState.Pressed)
            {
                var camera = _mapRenderer.Camera;
                var xSpeed = (_previousMousePosition.X - mouse.Position.X) * Speed;
                var ySpeed = (_previousMousePosition.Y - mouse.Position.Y) * Speed;
                camera.CameraRotationYawPitchRoll += new Vector3(1 * -xSpeed, 0, 0);
                camera.CameraRotationYawPitchRoll += new Vector3(0, 0, 1 * ySpeed);
            }

            _previousMousePosition = mouse.Position;
        }

        private static void ExportScene(string fileName, Scene scene)
        {
            using var ctx = new AssimpContext();
            var extension = Path.GetExtension(fileName).ToLower();
            var exportFormat = ctx.GetSupportedExportFormats();
            foreach (var format in exportFormat)
            {
                if ($".{format.FileExtension}" == extension)
                {
                    var material = new Material();
                    material.Clear();

                    scene.Materials.Add(material);
                    ctx.ExportFile(scene, fileName, format.FormatId);
                    return;
                }
            }

            ShowError($"Unable to export with '{extension}' extension.");
        }

        public static void ShowError(string message, string title = "Error") =>
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

        private void ShowAboutDialog() =>
            MessageBox.Show("OpenKH is amazing.");
    }
}
