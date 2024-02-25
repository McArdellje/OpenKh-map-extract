#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using UnityEngine.Rendering;
using UnityEngine.UI;
using VRC.Udon.Wrapper.Modules;

public class OpenKhImporter : EditorWindow
{
    [MenuItem("MC/OpenKH Import")]
    public static void ShowWindow() {
        GetWindow<OpenKhImporter>("OpenKH Import");
    }

    public GameObject model;
    public GameObject inSceneModel;
    public string materialFolderPath = "";
    public bool showTextureDropdown;
    public Vector2 textureDropdownScrollPos;
    public bool enableMipMaps = false;
    public bool flipTransparentRenderOrder = true;
    
    private static readonly int SrcBlend = Shader.PropertyToID("_SrcBlend");
    private static readonly int DstBlend = Shader.PropertyToID("_DstBlend");
    private static readonly int ZWrite = Shader.PropertyToID("_ZWrite");
    private static readonly int MainTex = Shader.PropertyToID("_MainTex");
    private static readonly int TextureRegion = Shader.PropertyToID("_TextureRegion");
    private static readonly int TextureWrapModeU = Shader.PropertyToID("_TextureWrapModeU");
    private static readonly int TextureWrapModeV = Shader.PropertyToID("_TextureWrapModeV");

    private enum WrapMode {
        Wrap = 0,
        Repeat = 0,
        Clamp = 1,
        RegionClamp = 2,
        RegionRepeat = 3,
    }

    private enum AlphaFlags {
        IsOpaque = 1,
        IsAlpha = 2,
        IsAlphaAdd = 4,
        IsAlphaSubtract = 8,
    }

    private struct TextureInfo {
        public int GroupIndex;
        public int MeshIndex;
        public string TextureName;
        public int AlphaFlagsInt;
        public int Priority;
        public int DrawPriority;
        public Vector2 RegionU;
        public Vector2 RegionV;
        public WrapMode WrapU;
        public WrapMode WrapV;
    }

    private static TextureWrapMode ToUnityWrap(WrapMode wm) {
        switch (wm) {
            case WrapMode.Repeat:
            case WrapMode.RegionRepeat:
                return TextureWrapMode.Repeat;
            case WrapMode.Clamp:
            case WrapMode.RegionClamp:
                return TextureWrapMode.Clamp;
            default:
                throw new System.Exception($"Unknown wrap mode {wm}");
        }
    }

    private static TextureInfo ParseTextureInfoSingleLine(string line) {
        var info = new TextureInfo();
        var colonSplit = line.Split(':');

        var meshSplit = colonSplit[0].Split(',');
        info.GroupIndex = int.Parse(meshSplit[0]);
        info.MeshIndex = int.Parse(meshSplit[1]);

        info.TextureName = colonSplit[1];

        info.AlphaFlagsInt = int.Parse(colonSplit[2]);

        info.Priority = int.Parse(colonSplit[3]);

        info.DrawPriority = int.Parse(colonSplit[4]);

        var regionUSplit = colonSplit[5].Split(',');
        info.RegionU = new Vector2(float.Parse(regionUSplit[0]), float.Parse(regionUSplit[1]));

        var regionVSplit = colonSplit[6].Split(',');
        info.RegionV = new Vector2(float.Parse(regionVSplit[0]), float.Parse(regionVSplit[1]));

        var wrapModeSplit = colonSplit[7].Split(',');
        // wrap modes are stored as strings of enum names
        info.WrapU = (WrapMode)System.Enum.Parse(typeof(WrapMode), wrapModeSplit[0]);
        info.WrapV = (WrapMode)System.Enum.Parse(typeof(WrapMode), wrapModeSplit[1]);

        return info;
    }

    private static IEnumerable<TextureInfo> ParseTextureInfo(IEnumerable<string> lines) {
        return lines.Select(ParseTextureInfoSingleLine);
    }

    private static IEnumerable<TextureInfo> ParseTextureInfo(string lines) {
        return ParseTextureInfo(lines.Split('\n').Where(x => x != ""));
    }

    private static TextureInfo ParseTextureInfoPreSlicedSingleLine(string line) {
        var info = new TextureInfo();
        var colonSplit = line.Split(':');

        var meshSplit = colonSplit[0].Split(',');
        info.GroupIndex = int.Parse(meshSplit[0]);
        info.MeshIndex = int.Parse(meshSplit[1]);

        info.TextureName = colonSplit[1];

        info.AlphaFlagsInt = int.Parse(colonSplit[2]);

        info.Priority = int.Parse(colonSplit[3]);

        info.DrawPriority = int.Parse(colonSplit[4]);

        var wrapModeSplit = colonSplit[5].Split(',');
        // wrap modes are stored as strings of enum names
        info.WrapU = (WrapMode)System.Enum.Parse(typeof(WrapMode), wrapModeSplit[0]);
        info.WrapV = (WrapMode)System.Enum.Parse(typeof(WrapMode), wrapModeSplit[1]);

        return info;
    }

    private static IEnumerable<TextureInfo> ParseTextureInfoPreSliced(IEnumerable<string> lines) {
        return lines.Select(ParseTextureInfoPreSlicedSingleLine);
    }

    private static IEnumerable<TextureInfo> ParseTextureInfoPreSliced(string lines) {
        return ParseTextureInfoPreSliced(lines.Split('\n').Where(x => x != ""));
    }

    private void OnGUI() {
        var kingdomShader = Shader.Find("KH2/Kingdom Shader");
        var kingdomShaderPreSliced = Shader.Find("KH2/Kingdom Shader (Pre-Sliced)");
        var kingdomShaderPreSlicedCutout = Shader.Find("KH2/Kingdom Shader (Pre-Sliced Cutout)");
        if (kingdomShader == null || kingdomShaderPreSliced == null || kingdomShaderPreSlicedCutout == null) {
            EditorGUILayout.HelpBox("Could not find shaders ('KH2/Kingdom Shader', 'KH2/Kingdom Shader (Pre-Sliced)' and 'KH2/Kingdom Shader (Pre-Sliced Cutout)'", MessageType.Error);
            return;
        }

        model = (GameObject)EditorGUILayout.ObjectField("Model", model, typeof(GameObject), false);
        inSceneModel = (GameObject)EditorGUILayout.ObjectField("In-Scene Model", inSceneModel, typeof(GameObject), true);
        materialFolderPath = EditorGUILayout.TextField("Material Folder Path", materialFolderPath);
        enableMipMaps = EditorGUILayout.Toggle("Enable MipMaps", enableMipMaps);
        flipTransparentRenderOrder = EditorGUILayout.Toggle("Flip Transparent Render Order", flipTransparentRenderOrder);
        if (model == null || inSceneModel == null || materialFolderPath == "") {
            return;
        }
        var areaID = model.name.Split('-')[0];
        EditorGUILayout.LabelField("Area ID", areaID);

        var modelPath = AssetDatabase.GetAssetPath(model);
        if (modelPath == null) {
            EditorGUILayout.HelpBox("Model is not an asset", MessageType.Error);
            return;
        }
        var modelDir = Path.GetDirectoryName(modelPath);
        
        bool isPreSliced = false;

        // find texture-info.txt, next to the model
        var textureWrapInfoPath = modelDir + $"/{areaID}-texture-info.txt";
        // load texture-info.txt (unity asset)
        var textureWrapInfo = AssetDatabase.LoadAssetAtPath<TextAsset>(textureWrapInfoPath);
        if (textureWrapInfo == null) {
            // try loading preSliced-texture-info.txt
            textureWrapInfoPath = modelDir + $"/{areaID}-preSliced-texture-info.txt";
            textureWrapInfo = AssetDatabase.LoadAssetAtPath<TextAsset>(textureWrapInfoPath);
            isPreSliced = true;
            if (textureWrapInfo == null) {
                EditorGUILayout.HelpBox(
                    $"Could not find {areaID}-texture-info.txt or {areaID}-preSliced-texture-info.txt",
                    MessageType.Error);
                return;
            }
        }

        if (isPreSliced) {
            EditorGUILayout.HelpBox("This Map was exported with Pre-Sliced textures, wrapping will be done with texture import settings", MessageType.Info);
        }
        else {
            EditorGUILayout.HelpBox("This Map was exported with Raw textures, wrapping will be done in-shader (this is more expensive than pre-sliced)", MessageType.Info);
        }

        if (GUILayout.Button("Generate Materials")) {
            var usedMaterialNames = new Dictionary<string, int>();
            var textureImporters = new List<TextureImporter>();
            string GetPath(string textureName) {
                if (usedMaterialNames.TryGetValue(textureName, out var count)) {
                    usedMaterialNames[textureName] = count + 1;
                    return materialFolderPath + $"/{textureName}-{count}.mat";
                }
                usedMaterialNames[textureName] = 1;
                return materialFolderPath + $"/{textureName}.mat";
            }

            var infoList = (isPreSliced
                ? ParseTextureInfoPreSliced(textureWrapInfo.text)
                : ParseTextureInfo(textureWrapInfo.text)).ToList();
            AssetDatabase.StartAssetEditing();
            try {
                for (var texInfoIdx = 0; texInfoIdx < infoList.Count; texInfoIdx++) {
                    var textureInfo = infoList[texInfoIdx];
                    EditorUtility.DisplayProgressBar("OpenKH Import",
                        $"Importing Textures {texInfoIdx + 1} / {infoList.Count} '{textureInfo.TextureName}'",
                        (float)texInfoIdx / infoList.Count);
                    var texturePath = modelDir + $"/{textureInfo.TextureName}.png";
                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                    if (texture == null) {
                        throw new System.Exception($"Could not find {textureInfo.TextureName} at {texturePath}");
                    }

                    // get textureImporter
                    var textureImporter = AssetImporter.GetAtPath(texturePath) as TextureImporter;
                    if (textureImporter == null) {
                        throw new System.Exception(
                            $"Could not find texture importer for {textureInfo.TextureName} at {texturePath}");
                    }

                    // set texture importer settings
                    textureImporter.textureCompression = TextureImporterCompression.Uncompressed;
                    if (isPreSliced) {
                        textureImporter.wrapModeU = ToUnityWrap(textureInfo.WrapU);
                        textureImporter.wrapModeV = ToUnityWrap(textureInfo.WrapV);
                    }

                    textureImporter.mipmapEnabled = enableMipMaps;

                    textureImporter.isReadable = true;
                    textureImporter.SaveAndReimport();

                    if (!textureImporters.Contains(textureImporter)) {
                        textureImporters.Add(textureImporter);
                    }
                }
            } finally {
                AssetDatabase.StopAssetEditing();
            }

            for (var texInfoIdx = 0; texInfoIdx < infoList.Count; texInfoIdx++) {
                var textureInfo = infoList[texInfoIdx];
                EditorUtility.DisplayProgressBar("OpenKH Import", $"Generating Materials {texInfoIdx + 1} / {infoList.Count} '{textureInfo.TextureName}'", (float)texInfoIdx / infoList.Count);
                var texturePath = modelDir + $"/{textureInfo.TextureName}.png";
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                if (texture == null) {
                    throw new System.Exception($"Could not find {textureInfo.TextureName} at {texturePath}");
                }

                // get textureImporter
                var textureImporter = AssetImporter.GetAtPath(texturePath) as TextureImporter;
                if (textureImporter == null) {
                    throw new System.Exception(
                        $"Could not find texture importer for {textureInfo.TextureName} at {texturePath}");
                }

                if (!textureImporters.Contains(textureImporter)) {
                    textureImporters.Add(textureImporter);
                }

                var groupTransform = inSceneModel.transform.GetChild(textureInfo.GroupIndex);
                if (groupTransform.childCount == 0) {
                    // there is only 1 group, so we use the root transform
                    groupTransform = inSceneModel.transform;
                }
                var meshRenderer = groupTransform.GetChild(textureInfo.MeshIndex).GetComponent<MeshRenderer>();

                var materialPath = GetPath(textureInfo.TextureName);

                // get material if it exists
                var needToSave = false;
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

                bool HasAlphaWithinUVs() {
                    // get AABB of mesh UVs
                    var mesh = meshRenderer.GetComponent<MeshFilter>().sharedMesh;
                    var uvs = mesh.uv;
                    var min = new Vector2(float.MaxValue, float.MaxValue);
                    var max = new Vector2(float.MinValue, float.MinValue);
                    foreach (var uv in uvs) {
                        min.x = Mathf.Min(min.x, uv.x);
                        min.y = Mathf.Min(min.y, uv.y);
                        max.x = Mathf.Max(max.x, uv.x);
                        max.y = Mathf.Max(max.y, uv.y);
                    }

                    // convert to pixel coordinates
                    min.x *= texture.width;
                    min.y *= texture.height;
                    max.x *= texture.width;
                    max.y *= texture.height;
                    // round down on min, round up on max
                    min.x = Mathf.Floor(min.x);
                    min.y = Mathf.Floor(min.y);
                    max.x = Mathf.Ceil(max.x);
                    max.y = Mathf.Ceil(max.y);
                    // clamp to texture size
                    min.x = Mathf.Clamp(min.x, 0, texture.width);
                    min.y = Mathf.Clamp(min.y, 0, texture.height);
                    max.x = Mathf.Clamp(max.x, 0, texture.width);
                    max.y = Mathf.Clamp(max.y, 0, texture.height);
                    // get pixel data
                    var pixels = texture.GetPixels((int)min.x, (int)min.y, (int)(max.x - min.x), (int)(max.y - min.y));
                    // check if any pixel is not fully opaque
                    return pixels.Any(x => x.a < 1f);
                }

                Shader shader;
                if (isPreSliced) {
                    var shouldBeCutout = textureInfo.AlphaFlagsInt == (int)AlphaFlags.IsOpaque &&
                                         textureImporter.DoesSourceTextureHaveAlpha() &&
                                         HasAlphaWithinUVs();
                    shader = shouldBeCutout
                        ? kingdomShaderPreSlicedCutout
                        : kingdomShaderPreSliced;
                } else {
                    shader = kingdomShader;
                }

                // create material
                if (material == null) {
                    needToSave = true;
                    material = new Material(shader) {
                        name = textureInfo.TextureName
                    };
                } else {
                    material.shader = shader;
                }

                material.SetTexture(MainTex, texture);

                // setup texture region if we aren't pre-sliced
                if (!isPreSliced) {
                    Vector2 FixRegion(Vector2 input, WrapMode wm, int size) {
                        // for raw regions from OpenKH
                        //input.y = (Mathf.Floor(input.y * size) + 1) / size;
                        // we now output pre-fixed integer regions, all we need to do is divide by size
                        if (wm == WrapMode.RegionRepeat) {
                            input += new Vector2(0f, 1f);
                        }

                        input /= size;
                        return input;
                    }

                    Vector2 InvertForUnity(Vector2 input) {
                        // invert the Y axis for Unity, OpenKH uses top down, Unity uses bottom up
                        // we flip x and y because x = min, y = max, but inverting the y axis means we need to flip them
                        return new Vector2(1f, 1f) - new Vector2(input.y, input.x);
                    }

                    var regU = FixRegion(textureInfo.RegionU, textureInfo.WrapU, texture.width);
                    var regV = InvertForUnity(FixRegion(textureInfo.RegionV, textureInfo.WrapV, texture.height));
                    material.SetVector(TextureRegion, new Vector4(regU.x, regU.y, regV.x, regV.y));

                    int WrapModeForRegion(Vector2 reg, WrapMode wrap) {
                        if (!Mathf.Approximately(reg.x, 0f) || !Mathf.Approximately(reg.y, 1f))
                            return (int)wrap;

                        switch (wrap) {
                            case WrapMode.Clamp:
                            case WrapMode.RegionClamp:
                                return (int)WrapMode.Clamp;
                            case WrapMode.Repeat:
                            case WrapMode.RegionRepeat:
                                return (int)WrapMode.Repeat;
                            default:
                                throw new System.Exception($"Unknown wrap mode {wrap}");
                        }
                    }

                    material.SetInt(TextureWrapModeU, WrapModeForRegion(regU, textureInfo.WrapU));
                    material.SetInt(TextureWrapModeV, WrapModeForRegion(regV, textureInfo.WrapV));
                }

                meshRenderer.sharedMaterial = material;

                if (textureInfo.AlphaFlagsInt == (int)AlphaFlags.IsOpaque) {
                    // make queue opaque
                    material.renderQueue = 2000;
                    material.SetInt(SrcBlend, 1);
                    material.SetInt(DstBlend, 0);
                    material.SetInt(ZWrite, 1);
                } else {
                    if ((textureInfo.AlphaFlagsInt & (int)AlphaFlags.IsAlpha) == 0) {
                        throw new System.Exception(
                            $"Texture {textureInfo.TextureName} is not opaque but is not alpha {textureInfo.AlphaFlagsInt}");
                    } else {
                        // make queue transparent
                        if (flipTransparentRenderOrder) {
                            material.renderQueue = 2500 + textureInfo.DrawPriority;
                        } else {
                            material.renderQueue = 3000 - textureInfo.DrawPriority;
                        }
                        var alphaAdd = (textureInfo.AlphaFlagsInt & (int)AlphaFlags.IsAlphaAdd) != 0;
                        var alphaSubtract = (textureInfo.AlphaFlagsInt & (int)AlphaFlags.IsAlphaSubtract) != 0;
                        if (alphaAdd && alphaSubtract) {
                            throw new System.Exception(
                                $"Texture {textureInfo.TextureName} is both alpha add and alpha subtract");
                        }

                        material.SetInt(ZWrite, 0);

                        if (alphaAdd) {
                            // additive (src alpha, one)
                            material.SetInt(SrcBlend, (int)BlendMode.SrcAlpha);
                            material.SetInt(DstBlend, (int)BlendMode.One);
                        } else if (alphaSubtract) {
                            // subtractive (zero, one minus src alpha)
                            material.SetInt(SrcBlend, (int)BlendMode.Zero);
                            material.SetInt(DstBlend, (int)BlendMode.OneMinusSrcAlpha);
                        } else {
                            // alpha blend
                            material.SetInt(SrcBlend, (int)BlendMode.SrcAlpha);
                            material.SetInt(DstBlend, (int)BlendMode.OneMinusSrcAlpha);
                        }
                    }
                }

                if (needToSave) {
                    // save material
                    AssetDatabase.CreateAsset(material, materialPath);
                }
            }
            EditorUtility.DisplayProgressBar("OpenKH Import", "Cleaning up import settings", 1);

            AssetDatabase.StartAssetEditing();
            try {
                // disable readable on all textures for performance
                foreach (var x in textureImporters) {
                    x.isReadable = false;
                    x.SaveAndReimport();
                }
            } finally {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
            }
        }

        // display texture list
        showTextureDropdown = EditorGUILayout.Foldout(showTextureDropdown, "Textures");
        if (showTextureDropdown) {
            textureDropdownScrollPos = EditorGUILayout.BeginScrollView(textureDropdownScrollPos);
            EditorGUI.indentLevel++;
            var infoIter = isPreSliced
                ? ParseTextureInfoPreSliced(textureWrapInfo.text)
                : ParseTextureInfo(textureWrapInfo.text);
            foreach (var textureInfo in infoIter) {
                EditorGUILayout.LabelField($"{textureInfo.GroupIndex},{textureInfo.MeshIndex} {textureInfo.TextureName}");
                // try load texture
                var texturePath = modelDir + $"/{textureInfo.TextureName}.png";
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                if (texture == null) {
                    EditorGUILayout.HelpBox($"Could not find {textureInfo.TextureName} at {texturePath}", MessageType.Error);
                    continue;
                }

                var meshRenderer = inSceneModel.transform.GetChild(textureInfo.GroupIndex).GetChild(textureInfo.MeshIndex).GetComponent<MeshRenderer>();

                if (meshRenderer == null) {
                    EditorGUILayout.HelpBox($"Could not find mesh renderer at {textureInfo.GroupIndex},{textureInfo.MeshIndex}", MessageType.Error);
                    continue;
                }
                EditorGUI.indentLevel++;
                EditorGUILayout.ObjectField(texture, typeof(Texture2D), false);
                EditorGUILayout.ObjectField(meshRenderer, typeof(MeshRenderer), false);
                EditorGUILayout.LabelField($"Alpha Flags: {textureInfo.AlphaFlagsInt:X}");
                EditorGUILayout.LabelField($"Region U: {textureInfo.RegionU}");
                EditorGUILayout.LabelField($"Region V: {textureInfo.RegionV}");
                EditorGUILayout.LabelField($"Wrap U: {textureInfo.WrapU}");
                EditorGUILayout.LabelField($"Wrap V: {textureInfo.WrapV}");
                EditorGUI.indentLevel--;
            }
            EditorGUI.indentLevel--;
            EditorGUILayout.EndScrollView();
        }
    }
}
#endif