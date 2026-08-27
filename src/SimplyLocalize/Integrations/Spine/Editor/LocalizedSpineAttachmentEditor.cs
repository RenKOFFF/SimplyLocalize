using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using global::Spine;
using global::Spine.Unity;
using SimplyLocalize.Editor.AssetPreviews;
using SimplyLocalize.Editor.Data;
using SimplyLocalize.Editor.Inspectors;
using SimplyLocalize.Editor.Utilities;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SimplyLocalize.SpineIntegration.Editor
{
    [CustomEditor(typeof(LocalizedSpineAttachment))]
    public sealed class LocalizedSpineAttachmentEditor : UnityEditor.Editor
    {
        private SerializedProperty _keyProperty;
        private SerializedProperty _targetProperty;
        private SerializedProperty _regionPathProperty;
        private SerializedProperty _atlasPageProperty;
        private SerializedProperty _monitorSkinChangesProperty;

        private readonly List<RegionDescriptor> _regions = new();
        private string[] _regionLabels = Array.Empty<string>();
        private Object _cachedSkeletonDataAsset;
        private string _regionLoadError;

        private int _languageIndex;
        private bool _assignExtractedTexture = true;
        private bool _showAdvanced;
        private readonly HashSet<string> _expandedLanguages = new();
        private string _newKeyInput = string.Empty;

        private void OnEnable()
        {
            BindProperties();
            RebuildRegionCache(force: true);
        }

        private void BindProperties()
        {
            _keyProperty = serializedObject.FindProperty("_key");
            _targetProperty = serializedObject.FindProperty("_target");
            _regionPathProperty = serializedObject.FindProperty("_regionPath");
            _atlasPageProperty = serializedObject.FindProperty("_atlasPage");
            _monitorSkinChangesProperty = serializedObject.FindProperty("_monitorSkinChanges");
        }

        public override void OnInspectorGUI()
        {
            if (_keyProperty == null || _targetProperty == null ||
                _regionPathProperty == null || _atlasPageProperty == null ||
                _monitorSkinChangesProperty == null)
            {
                BindProperties();
                RebuildRegionCache(force: true);
            }

            serializedObject.Update();

            DrawKeySelector();
            if (!string.IsNullOrWhiteSpace(_keyProperty.stringValue))
                DrawAssetPreview();

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Spine Attachment", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            DrawRegionSelector();

            _showAdvanced = EditorGUILayout.Foldout(_showAdvanced, "Advanced", true);
            if (_showAdvanced)
            {
                EditorGUI.indentLevel++;
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(_targetProperty, new GUIContent("Spine Target"));
                if (EditorGUI.EndChangeCheck())
                {
                    serializedObject.ApplyModifiedProperties();
                    RebuildRegionCache(force: true);
                    serializedObject.Update();
                }
                EditorGUILayout.PropertyField(_monitorSkinChangesProperty);
                EditorGUI.indentLevel--;
            }

            serializedObject.ApplyModifiedProperties();
            RebuildRegionCache(force: false);

            EditorGUILayout.Space(8);
            DrawValidationAndPreview();
            EditorGUILayout.Space(8);
            DrawExtractionTool();
        }

        private void DrawKeySelector()
        {
            var keys = GetTextureAssetKeys();
            var currentKey = _keyProperty.stringValue;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Key (Texture2D)");
            var display = string.IsNullOrEmpty(currentKey)
                ? "<None>"
                : keys.Contains(currentKey)
                    ? FormatKey(currentKey)
                    : $"<Missing> ({currentKey})";

            var previousColor = GUI.color;
            if (!string.IsNullOrEmpty(currentKey) && !keys.Contains(currentKey))
                GUI.color = new Color(1f, 0.35f, 0.35f);

            if (GUILayout.Button(display, EditorStyles.popup))
            {
                var searchWindow = ScriptableObject.CreateInstance<KeySearchWindow>();
                searchWindow.Init(keys, selectedKey =>
                {
                    SetKey(selectedKey ?? string.Empty);
                    EditorDataCache.Invalidate();
                });
                var mousePosition = GUIUtility.GUIToScreenPoint(UnityEngine.Event.current.mousePosition);
                SearchWindow.Open(new SearchWindowContext(mousePosition), searchWindow);
            }
            GUI.color = previousColor;

            if (!string.IsNullOrEmpty(currentKey) && !keys.Contains(currentKey) &&
                GUILayout.Button("+", GUILayout.Width(22)))
            {
                AddKeyToAssetTables(currentKey);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Add new key");
            var duplicate = !string.IsNullOrWhiteSpace(_newKeyInput) && keys.Contains(_newKeyInput);
            previousColor = GUI.color;
            if (duplicate)
                GUI.color = new Color(1f, 0.35f, 0.35f);
            _newKeyInput = EditorGUILayout.TextField(_newKeyInput);
            GUI.color = previousColor;

            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_newKeyInput) || duplicate))
            {
                if (GUILayout.Button("Add", GUILayout.Width(40)))
                {
                    AddKeyToAssetTables(_newKeyInput);
                    SetKey(_newKeyInput);
                    _newKeyInput = string.Empty;
                }
            }
            EditorGUILayout.EndHorizontal();

            if (duplicate)
                EditorGUILayout.HelpBox("This key already exists.", MessageType.Error);
        }

        private void SetKey(string key)
        {
            serializedObject.Update();
            _keyProperty.stringValue = key;
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawAssetPreview()
        {
            var config = EditorDataCache.Config;
            if (config?.languages == null)
                return;

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Translations", EditorStyles.boldLabel);

            var anyExpanded = _expandedLanguages.Count > 0;
            if (GUILayout.Button(anyExpanded ? "Collapse all" : "Expand all",
                    EditorStyles.miniButton, GUILayout.Width(90)))
            {
                if (anyExpanded)
                {
                    _expandedLanguages.Clear();
                }
                else
                {
                    foreach (var profile in config.languages)
                        if (profile != null)
                            _expandedLanguages.Add(profile.Code);
                }
            }
            EditorGUILayout.EndHorizontal();

            foreach (var profile in config.languages)
            {
                if (profile == null)
                    continue;

                var table = FindAssetTable(profile.Code);
                Object asset = table?.Get(_keyProperty.stringValue);
                string fallbackLanguage = null;

                if (asset == null)
                {
                    var fallback = FallbackResolver.ResolveAsset(
                        config,
                        _keyProperty.stringValue,
                        profile.Code,
                        code => FindAssetTable(code));
                    asset = fallback.Asset;
                    fallbackLanguage = fallback.FromLanguage;
                }

                DrawLanguageRow(profile, asset, fallbackLanguage);
            }
        }

        private void DrawLanguageRow(LanguageProfile profile, Object asset, string fallbackLanguage)
        {
            var expanded = _expandedLanguages.Contains(profile.Code);
            var isFallback = asset != null && !string.IsNullOrEmpty(fallbackLanguage);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"{profile.displayName} ({profile.Code})",
                GUILayout.Width(EditorGUIUtility.labelWidth));

            if (asset != null)
            {
                var previousColor = GUI.color;
                if (isFallback)
                    GUI.color = new Color(1f, 1f, 1f, 0.55f);

                var thumbnail = AssetPreview.GetMiniThumbnail(asset);
                var rowRect = EditorGUILayout.GetControlRect(GUILayout.Height(20));
                if (thumbnail != null)
                    GUI.DrawTexture(new Rect(rowRect.x, rowRect.y, 20, 20), thumbnail, ScaleMode.ScaleToFit);

                var nameRect = new Rect(rowRect.x + 24, rowRect.y, rowRect.width - 24, rowRect.height);
                EditorGUI.LabelField(
                    nameRect,
                    isFallback ? $"{asset.name}  — from {fallbackLanguage}" : asset.name,
                    EditorStyles.miniLabel);

                EditorGUIUtility.AddCursorRect(rowRect, MouseCursor.Link);
                if (UnityEngine.Event.current.type == EventType.MouseDown &&
                    UnityEngine.Event.current.button == 0 &&
                    rowRect.Contains(UnityEngine.Event.current.mousePosition))
                {
                    Selection.activeObject = asset;
                    EditorGUIUtility.PingObject(asset);
                    UnityEngine.Event.current.Use();
                }
                GUI.color = previousColor;

                if (GUILayout.Button(expanded ? "▼" : "▶", EditorStyles.miniButton,
                        GUILayout.Width(22), GUILayout.Height(18)))
                {
                    if (expanded)
                        _expandedLanguages.Remove(profile.Code);
                    else
                        _expandedLanguages.Add(profile.Code);
                }
            }
            else
            {
                var previousColor = GUI.color;
                GUI.color = new Color(1f, 0.5f, 0.5f);
                EditorGUILayout.LabelField("(not assigned)", EditorStyles.miniLabel);
                GUI.color = previousColor;
            }
            EditorGUILayout.EndHorizontal();

            if (!expanded || asset == null)
                return;

            var previewRect = GUILayoutUtility.GetRect(200f, 140f, GUILayout.ExpandWidth(false));
            previewRect.x += EditorGUIUtility.labelWidth;

            var previewColor = GUI.color;
            if (isFallback)
                GUI.color = new Color(1f, 1f, 1f, 0.55f);
            AssetPreviewRegistry.GetRendererFor(asset).DrawPreview(previewRect, asset);
            GUI.color = previewColor;
        }

        private void DrawRegionSelector()
        {
            if (!string.IsNullOrEmpty(_regionLoadError))
            {
                EditorGUILayout.PropertyField(_regionPathProperty, new GUIContent("Region Path"));
                EditorGUILayout.HelpBox(_regionLoadError, MessageType.Error);
                return;
            }

            if (_regions.Count == 0)
            {
                EditorGUILayout.PropertyField(_regionPathProperty, new GUIContent("Region Path"));
                EditorGUILayout.HelpBox(
                    "No textured RegionAttachment or MeshAttachment was found in the selected SkeletonDataAsset.",
                    MessageType.Error);
                return;
            }

            var currentIndex = _regions.FindIndex(region =>
                string.Equals(region.Path, _regionPathProperty.stringValue, StringComparison.Ordinal) &&
                ReferenceEquals(region.AtlasPage, _atlasPageProperty.objectReferenceValue));

            // Existing components created before Atlas Page was serialized only have a path.
            // Preserve that path and bind it to its first matching page instead of resetting
            // the selection to the first region in the skeleton.
            var pathOnlyIndex = _regions.FindIndex(region =>
                string.Equals(region.Path, _regionPathProperty.stringValue, StringComparison.Ordinal));
            var displayedIndex = currentIndex >= 0
                ? currentIndex
                : Mathf.Max(0, pathOnlyIndex);
            EditorGUI.BeginChangeCheck();
            var selectedIndex = EditorGUILayout.Popup(
                new GUIContent("Atlas Region"),
                displayedIndex,
                _regionLabels);

            if (EditorGUI.EndChangeCheck() || currentIndex < 0)
            {
                var selectedRegion = _regions[selectedIndex];
                _regionPathProperty.stringValue = selectedRegion.Path;
                _atlasPageProperty.objectReferenceValue = selectedRegion.AtlasPage;
                serializedObject.ApplyModifiedProperties();
            }
        }

        private void DrawValidationAndPreview()
        {
            var descriptor = GetSelectedRegion();
            if (descriptor == null)
            {
                DrawStatus("Select a valid Spine atlas region.", MessageType.Error);
                return;
            }

            EditorGUILayout.LabelField("Attachment Validation", EditorStyles.boldLabel);
            DrawStatus(
                $"{descriptor.Path} [{descriptor.PageTextureName}]: " +
                $"{descriptor.OriginalWidth}×{descriptor.OriginalHeight}, " +
                $"{descriptor.AttachmentNames.Count} attachment(s), {descriptor.SlotNames.Count} slot(s).",
                MessageType.Info);

            if (descriptor.HasUnsupportedAttachment)
            {
                DrawStatus(
                    "The region is also used by an unsupported attachment type. " +
                    "Only RegionAttachment and MeshAttachment are replaced.",
                    MessageType.Error);
            }

            if (descriptor.HasSequence)
            {
                DrawStatus(
                    "At least one attachment uses a Spine sequence. Only its base region is localized.",
                    MessageType.Warning);
            }

            if (descriptor.HasDifferentLogicalSizes)
            {
                DrawStatus(
                    "Attachments with this path report different logical region sizes.",
                    MessageType.Error);
            }

            if (descriptor.IsRotated || descriptor.IsTrimmed)
            {
                var details = string.Join(", ", new[]
                {
                    descriptor.IsRotated ? "rotated" : null,
                    descriptor.IsTrimmed ? "trimmed" : null
                }.Where(value => value != null));

                DrawStatus(
                    $"The atlas region is {details}. Extraction restores its original orientation and canvas.",
                    MessageType.Warning);
            }

            DrawLocalizedTextureValidation(descriptor);
            DrawSourcePreview(descriptor);
        }

        private void DrawLocalizedTextureValidation(RegionDescriptor descriptor)
        {
            var key = _keyProperty.stringValue;
            if (string.IsNullOrWhiteSpace(key))
            {
                DrawStatus("Localization key is empty.", MessageType.Error);
                return;
            }

            var languageAssets = GetDirectLanguageAssets(key);
            if (languageAssets.Count == 0)
            {
                DrawStatus(
                    "No localized Texture2D is assigned yet. Missing language assets intentionally use the original Spine attachment.",
                    MessageType.Warning);
                return;
            }

            foreach (var languageAsset in languageAssets)
            {
                if (languageAsset.Asset == null)
                    continue;

                if (languageAsset.Asset is not Texture2D texture)
                {
                    DrawStatus(
                        $"{languageAsset.LanguageCode}: asset '{languageAsset.Asset.name}' is not a Texture2D.",
                        MessageType.Error);
                    continue;
                }

                var width = texture.width;
                var height = texture.height;
                var exactSize = width == descriptor.OriginalWidth && height == descriptor.OriginalHeight;

                DrawStatus(
                    exactSize
                        ? $"{languageAsset.LanguageCode}: {texture.name}, {width}×{height}."
                        : $"{languageAsset.LanguageCode}: {texture.name} is {width}×{height}; " +
                          $"required size is {descriptor.OriginalWidth}×{descriptor.OriginalHeight}.",
                    exactSize ? MessageType.Info : MessageType.Error);

                DrawImporterWarnings(languageAsset.LanguageCode, texture);
            }
        }

        private static void DrawImporterWarnings(string languageCode, Texture2D texture)
        {
            var assetPath = AssetDatabase.GetAssetPath(texture);
            if (AssetImporter.GetAtPath(assetPath) is not TextureImporter importer)
                return;

            var warnings = new List<string>();
            if (importer.wrapMode != TextureWrapMode.Clamp)
                warnings.Add("Wrap Mode is not Clamp");
            if (importer.mipmapEnabled)
                warnings.Add("Mip Maps are enabled");

            if (warnings.Count > 0)
            {
                DrawStatus(
                    $"{languageCode}: {string.Join("; ", warnings)}.",
                    MessageType.Warning);
            }
        }

        private static void DrawSourcePreview(RegionDescriptor descriptor)
        {
            if (descriptor.SourceTexture == null || descriptor.Region == null)
                return;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Source region preview", EditorStyles.boldLabel);

            const float maxWidth = 320f;
            const float maxHeight = 220f;
            var aspect = descriptor.OriginalHeight > 0
                ? (float)descriptor.OriginalWidth / descriptor.OriginalHeight
                : 1f;
            var width = Mathf.Min(maxWidth, maxHeight * aspect);
            var height = width / Mathf.Max(0.001f, aspect);
            var rect = GUILayoutUtility.GetRect(width, height, GUILayout.ExpandWidth(false));

            var sourceRect = SpineRegionExtraction.GetUnityAtlasRect(
                descriptor.Region,
                descriptor.SourceTexture.height);
            var uv = new Rect(
                sourceRect.x / descriptor.SourceTexture.width,
                sourceRect.y / descriptor.SourceTexture.height,
                sourceRect.width / descriptor.SourceTexture.width,
                sourceRect.height / descriptor.SourceTexture.height);

            EditorGUI.DrawPreviewTexture(rect, Texture2D.grayTexture);
            GUI.DrawTextureWithTexCoords(rect, descriptor.SourceTexture, uv, true);
        }

        private void DrawExtractionTool()
        {
            EditorGUILayout.LabelField("Localization Template", EditorStyles.boldLabel);

            var config = EditorDataCache.Config;
            var languages = config?.languages?
                .Where(profile => profile != null)
                .ToArray() ?? Array.Empty<LanguageProfile>();

            if (languages.Length == 0)
            {
                DrawStatus("Simply Localize has no configured languages.", MessageType.Error);
                return;
            }

            _languageIndex = Mathf.Clamp(_languageIndex, 0, languages.Length - 1);
            _languageIndex = EditorGUILayout.Popup(
                "Target Language",
                _languageIndex,
                languages.Select(profile => $"{profile.displayName} ({profile.Code})").ToArray());
            _assignExtractedTexture = EditorGUILayout.ToggleLeft(
                "Assign extracted texture to this key in the language AssetTable",
                _assignExtractedTexture);

            var descriptor = GetSelectedRegion();
            var canExtract = descriptor != null &&
                             descriptor.SourceTexture != null &&
                             !string.IsNullOrWhiteSpace(_keyProperty.stringValue);

            using (new EditorGUI.DisabledScope(!canExtract))
            {
                if (GUILayout.Button("Extract and Import Localization Template", GUILayout.Height(28)))
                {
                    ExtractTemplate(descriptor, languages[_languageIndex].Code);
                }
            }

            if (!canExtract)
            {
                EditorGUILayout.HelpBox(
                    "Select a valid region and enter a localization key to enable extraction.",
                    MessageType.Info);
            }
        }

        private void ExtractTemplate(RegionDescriptor descriptor, string languageCode)
        {
            var outputPath = SpineRegionExtraction.BuildOutputPath(
                descriptor.SourceTexture,
                descriptor.Path,
                languageCode);

            if (File.Exists(ToAbsolutePath(outputPath)) &&
                !EditorUtility.DisplayDialog(
                    "Overwrite localization template?",
                    $"The file already exists:\n{outputPath}",
                    "Overwrite",
                    "Cancel"))
            {
                return;
            }

            try
            {
                var texture = SpineRegionExtraction.ExtractAndImport(descriptor.Region, outputPath);
                if (texture == null)
                    throw new InvalidOperationException("Unity did not import the extracted PNG as a Texture2D.");

                if (_assignExtractedTexture)
                {
                    var table = FindAssetTable(languageCode);
                    if (table == null)
                    {
                        throw new InvalidOperationException(
                            $"Could not find a Simply Localize AssetTable for language '{languageCode}'.");
                    }

                    Undo.RecordObject(table, "Assign localized Spine attachment");
                    table.Set(_keyProperty.stringValue, texture);
                    table.Sort();
                    EditorUtility.SetDirty(table);
                    AssetDatabase.SaveAssets();
                    EditorDataCache.Invalidate();
                }

                Selection.activeObject = texture;
                EditorGUIUtility.PingObject(texture);
                Repaint();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "Spine localization extraction failed",
                    exception.Message,
                    "OK");
            }
        }

        private void RebuildRegionCache(bool force)
        {
            var skeletonDataAsset = GetSkeletonDataAsset(_targetProperty?.objectReferenceValue as Component);
            if (!force && ReferenceEquals(_cachedSkeletonDataAsset, skeletonDataAsset))
                return;

            _cachedSkeletonDataAsset = skeletonDataAsset;
            _regions.Clear();
            _regionLabels = Array.Empty<string>();
            _regionLoadError = null;

            if (skeletonDataAsset == null)
            {
                _regionLoadError = "Select a SkeletonRenderer/SkeletonAnimation or SkeletonGraphic with a SkeletonDataAsset.";
                return;
            }

            SkeletonData skeletonData;
            try
            {
                skeletonData = skeletonDataAsset.GetSkeletonData(true);
            }
            catch (Exception exception)
            {
                _regionLoadError = $"Could not load SkeletonData: {exception.Message}";
                return;
            }

            if (skeletonData == null)
            {
                _regionLoadError = "SkeletonDataAsset returned no SkeletonData.";
                return;
            }

            var descriptors = new Dictionary<RegionKey, RegionDescriptor>();
            var visitedSkins = new HashSet<Skin>();

            AddSkinRegions(skeletonData.DefaultSkin, skeletonData, descriptors, visitedSkins);
            foreach (var skin in skeletonData.Skins)
                AddSkinRegions(skin, skeletonData, descriptors, visitedSkins);

            _regions.AddRange(descriptors.Values
                .OrderBy(region => region.Path, StringComparer.Ordinal)
                .ThenBy(region => region.PageTextureName, StringComparer.Ordinal));
            _regionLabels = _regions.Select(region =>
                $"{region.Path}    [{region.PageTextureName}]    " +
                $"{region.OriginalWidth}×{region.OriginalHeight}    " +
                $"({region.AttachmentNames.Count})").ToArray();
        }

        private static void AddSkinRegions(
            Skin skin,
            SkeletonData skeletonData,
            IDictionary<RegionKey, RegionDescriptor> descriptors,
            ISet<Skin> visitedSkins)
        {
            if (skin == null || !visitedSkins.Add(skin))
                return;

            foreach (var entry in skin.Attachments)
            {
                if (entry.Attachment is not IHasTextureRegion textured ||
                    string.IsNullOrWhiteSpace(textured.Path))
                {
                    continue;
                }

                var atlasPage = (textured.Region as AtlasRegion)?.page?.rendererObject as Material;
                var key = new RegionKey(textured.Path, atlasPage);
                if (!descriptors.TryGetValue(key, out var descriptor))
                {
                    descriptor = new RegionDescriptor(textured.Path, atlasPage);
                    descriptors.Add(key, descriptor);
                }

                descriptor.Add(entry, textured, skeletonData);
            }
        }

        private RegionDescriptor GetSelectedRegion()
        {
            return _regions.FirstOrDefault(region =>
                string.Equals(region.Path, _regionPathProperty.stringValue, StringComparison.Ordinal) &&
                ReferenceEquals(region.AtlasPage, _atlasPageProperty.objectReferenceValue));
        }

        private static SkeletonDataAsset GetSkeletonDataAsset(Component component)
        {
            return component switch
            {
                SkeletonRenderer renderer => renderer.SkeletonDataAsset,
                SkeletonGraphic graphic => graphic.SkeletonDataAsset,
                _ => null
            };
        }

        private static List<string> GetTextureAssetKeys()
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var config = EditorDataCache.Config;
            if (config?.languages == null)
                return new List<string>();

            foreach (var profile in config.languages)
            {
                if (profile == null)
                    continue;

                var table = FindAssetTable(profile.Code);
                if (table == null)
                    continue;

                foreach (var entry in table.Entries)
                {
                    if (!string.IsNullOrWhiteSpace(entry.key) &&
                        (entry.asset == null || entry.asset is Texture2D))
                    {
                        keys.Add(entry.key);
                    }
                }
            }

            return keys.OrderBy(key => key, StringComparer.Ordinal).ToList();
        }

        private static List<LanguageAsset> GetDirectLanguageAssets(string key)
        {
            var result = new List<LanguageAsset>();
            var config = EditorDataCache.Config;
            if (config?.languages == null)
                return result;

            foreach (var profile in config.languages)
            {
                if (profile == null)
                    continue;

                var table = FindAssetTable(profile.Code);
                if (table != null && table.HasKey(key))
                    result.Add(new LanguageAsset(profile.Code, table.Get(key)));
            }

            return result;
        }

        private static LocalizationAssetTable FindAssetTable(string languageCode)
        {
            var basePath = EditorDataCache.Data?.BasePath;
            if (string.IsNullOrWhiteSpace(basePath))
                return null;

            var languageDirectory = Path.Combine(basePath, languageCode);
            if (!Directory.Exists(languageDirectory))
                return null;

            foreach (var file in Directory.GetFiles(languageDirectory, "*.asset", SearchOption.TopDirectoryOnly))
            {
                var assetPath = ToAssetPath(file);
                var table = AssetDatabase.LoadAssetAtPath<LocalizationAssetTable>(assetPath);
                if (table != null)
                    return table;
            }

            return null;
        }

        private static void AddKeyToAssetTables(string key)
        {
            var config = EditorDataCache.Config;
            if (config?.languages == null || string.IsNullOrWhiteSpace(key))
                return;

            foreach (var profile in config.languages)
            {
                if (profile == null)
                    continue;

                var table = FindAssetTable(profile.Code);
                if (table == null || table.HasKey(key))
                    continue;

                Undo.RecordObject(table, "Add localization asset key");
                table.Set(key, null);
                table.Sort();
                EditorUtility.SetDirty(table);
            }

            AssetDatabase.SaveAssets();
            EditorDataCache.Invalidate();
        }

        private static string FormatKey(string key)
        {
            return key.Contains('/') ? $"{key.Split('/').Last()}    ({key})" : key;
        }

        private static string ToAssetPath(string absolutePath)
        {
            var normalized = absolutePath.Replace('\\', '/');
            var dataPath = Application.dataPath.Replace('\\', '/');
            return normalized.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase)
                ? "Assets" + normalized.Substring(dataPath.Length)
                : normalized;
        }

        private static string ToAbsolutePath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
        }

        private static void DrawStatus(string message, MessageType type)
        {
            var previousColor = GUI.color;
            GUI.color = type switch
            {
                MessageType.Error => new Color(1f, 0.72f, 0.72f),
                MessageType.Warning => new Color(1f, 0.9f, 0.55f),
                _ => new Color(0.78f, 1f, 0.78f)
            };
            EditorGUILayout.HelpBox(message, type);
            GUI.color = previousColor;
        }

        private sealed class RegionDescriptor
        {
            public readonly string Path;
            public readonly Material AtlasPage;
            public readonly HashSet<string> AttachmentNames = new(StringComparer.Ordinal);
            public readonly HashSet<string> SlotNames = new(StringComparer.Ordinal);

            public AtlasRegion Region;
            public Texture2D SourceTexture;
            public int OriginalWidth;
            public int OriginalHeight;
            public bool HasUnsupportedAttachment;
            public bool HasSequence;
            public bool HasDifferentLogicalSizes;

            public string PageTextureName => AtlasPage?.mainTexture != null
                ? AtlasPage.mainTexture.name
                : "Unknown page";

            public bool IsRotated => Region != null && Region.degrees != 0;
            public bool IsTrimmed => Region != null &&
                                     (Region.originalWidth != Region.packedWidth ||
                                      Region.originalHeight != Region.packedHeight ||
                                      !Mathf.Approximately(Region.offsetX, 0f) ||
                                      !Mathf.Approximately(Region.offsetY, 0f));

            public RegionDescriptor(string path, Material atlasPage)
            {
                Path = path;
                AtlasPage = atlasPage;
            }

            public void Add(Skin.SkinEntry entry, IHasTextureRegion textured, SkeletonData skeletonData)
            {
                AttachmentNames.Add(entry.Name);
                var slots = skeletonData.Slots;
                if (entry.SlotIndex >= 0 && entry.SlotIndex < slots.Count)
                    SlotNames.Add(slots.Items[entry.SlotIndex].Name);

                if (entry.Attachment is not RegionAttachment && entry.Attachment is not MeshAttachment)
                    HasUnsupportedAttachment = true;
                if (textured.Sequence != null)
                    HasSequence = true;

                if (textured.Region is not AtlasRegion atlasRegion)
                    return;

                var width = atlasRegion.originalWidth > 0
                    ? atlasRegion.originalWidth
                    : atlasRegion.packedWidth;
                var height = atlasRegion.originalHeight > 0
                    ? atlasRegion.originalHeight
                    : atlasRegion.packedHeight;

                if (Region != null && (OriginalWidth != width || OriginalHeight != height))
                    HasDifferentLogicalSizes = true;

                Region ??= atlasRegion;
                OriginalWidth = OriginalWidth == 0 ? width : OriginalWidth;
                OriginalHeight = OriginalHeight == 0 ? height : OriginalHeight;

                if (SourceTexture == null && atlasRegion.page?.rendererObject is Material material)
                    SourceTexture = material.mainTexture as Texture2D;
            }
        }

        private readonly struct RegionKey : IEquatable<RegionKey>
        {
            private readonly string _path;
            private readonly Material _atlasPage;

            public RegionKey(string path, Material atlasPage)
            {
                _path = path;
                _atlasPage = atlasPage;
            }

            public bool Equals(RegionKey other)
            {
                return string.Equals(_path, other._path, StringComparison.Ordinal) &&
                       ReferenceEquals(_atlasPage, other._atlasPage);
            }

            public override bool Equals(object obj)
            {
                return obj is RegionKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((_path != null ? StringComparer.Ordinal.GetHashCode(_path) : 0) * 397) ^
                           (_atlasPage != null ? _atlasPage.GetInstanceID() : 0);
                }
            }
        }

        private readonly struct LanguageAsset
        {
            public readonly string LanguageCode;
            public readonly Object Asset;

            public LanguageAsset(string languageCode, Object asset)
            {
                LanguageCode = languageCode;
                Asset = asset;
            }
        }
    }

    internal static class SpineRegionExtraction
    {
        public static string BuildOutputPath(Texture2D sourceTexture, string regionPath, string languageCode)
        {
            var sourcePath = AssetDatabase.GetAssetPath(sourceTexture).Replace('\\', '/');
            var sourceDirectory = Path.GetDirectoryName(sourcePath)?.Replace('\\', '/') ?? "Assets";
            var safeRegionPath = Sanitize(regionPath.Replace('/', '_'));
            var leafName = Sanitize(regionPath.Split('/').Last());
            var fileName = $"{leafName}_{Sanitize(languageCode.ToLowerInvariant())}.png";
            return $"{sourceDirectory}/Localized/{safeRegionPath}/{fileName}";
        }

        public static Texture2D ExtractAndImport(AtlasRegion region, string outputAssetPath)
        {
            if (region?.page?.rendererObject is not Material material ||
                material.mainTexture is not Texture2D sourceTexture)
            {
                throw new InvalidOperationException("The Spine atlas region has no readable Texture2D page.");
            }

            var sourcePath = AssetDatabase.GetAssetPath(sourceTexture);
            if (AssetImporter.GetAtPath(sourcePath) is not TextureImporter sourceImporter)
                throw new InvalidOperationException("The source Spine atlas has no TextureImporter.");

            var wasReadable = sourceImporter.isReadable;
            Color32[] sourcePixels;
            int sourceWidth;
            int sourceHeight;

            try
            {
                if (!wasReadable)
                {
                    sourceImporter.isReadable = true;
                    sourceImporter.SaveAndReimport();
                }

                var readableTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
                sourcePixels = readableTexture.GetPixels32();
                sourceWidth = readableTexture.width;
                sourceHeight = readableTexture.height;
            }
            finally
            {
                if (!wasReadable)
                {
                    sourceImporter = AssetImporter.GetAtPath(sourcePath) as TextureImporter;
                    if (sourceImporter != null)
                    {
                        sourceImporter.isReadable = false;
                        sourceImporter.SaveAndReimport();
                    }
                }
            }

            var packedPixels = ExtractPackedPixels(region, sourcePixels, sourceWidth, sourceHeight);
            var logicalPixels = RestoreOriginalCanvas(region, packedPixels);

            if (region.page.pma)
                UnpremultiplyAlpha(logicalPixels);

            var outputWidth = GetOriginalWidth(region);
            var outputHeight = GetOriginalHeight(region);
            var outputTexture = new Texture2D(outputWidth, outputHeight, TextureFormat.RGBA32, false, false);
            outputTexture.SetPixels32(logicalPixels);
            outputTexture.Apply(false, false);

            var absolutePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", outputAssetPath));
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath) ?? throw new InvalidOperationException());
            File.WriteAllBytes(absolutePath, outputTexture.EncodeToPNG());
            Object.DestroyImmediate(outputTexture);

            AssetDatabase.ImportAsset(outputAssetPath, ImportAssetOptions.ForceSynchronousImport);
            ConfigureLocalizedTextureImporter(outputAssetPath, sourceImporter);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(outputAssetPath);
        }

        public static Rect GetUnityAtlasRect(AtlasRegion region, int textureHeight)
        {
            var width = region.degrees == 270 ? region.packedHeight : region.packedWidth;
            var height = region.degrees == 270 ? region.packedWidth : region.packedHeight;
            return new Rect(region.x, textureHeight - region.y - height, width, height);
        }

        private static PackedPixels ExtractPackedPixels(
            AtlasRegion region,
            IReadOnlyList<Color32> sourcePixels,
            int sourceWidth,
            int sourceHeight)
        {
            var rect = GetUnityAtlasRect(region, sourceHeight);
            var width = Mathf.RoundToInt(rect.width);
            var height = Mathf.RoundToInt(rect.height);
            var startX = Mathf.RoundToInt(rect.x);
            var startY = Mathf.RoundToInt(rect.y);

            if (startX < 0 || startY < 0 || startX + width > sourceWidth || startY + height > sourceHeight)
                throw new InvalidOperationException("The Spine atlas region lies outside its source texture.");

            var pixels = new Color32[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                    pixels[y * width + x] = sourcePixels[(startY + y) * sourceWidth + startX + x];
            }

            if (region.degrees == 270)
                return RotateCounterClockwise(pixels, width, height);

            if (region.degrees != 0)
            {
                throw new NotSupportedException(
                    $"Atlas rotation of {region.degrees} degrees is not supported by the extractor.");
            }

            return new PackedPixels(pixels, width, height);
        }

        private static PackedPixels RotateCounterClockwise(Color32[] source, int width, int height)
        {
            var outputWidth = height;
            var outputHeight = width;
            var output = new Color32[outputWidth * outputHeight];

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var destinationX = y;
                    var destinationY = width - 1 - x;
                    output[destinationY * outputWidth + destinationX] = source[y * width + x];
                }
            }

            return new PackedPixels(output, outputWidth, outputHeight);
        }

        private static Color32[] RestoreOriginalCanvas(AtlasRegion region, PackedPixels packed)
        {
            var outputWidth = GetOriginalWidth(region);
            var outputHeight = GetOriginalHeight(region);
            var output = new Color32[outputWidth * outputHeight];
            var offsetX = Mathf.RoundToInt(region.offsetX);
            var offsetY = Mathf.RoundToInt(region.offsetY);

            if (offsetX < 0 || offsetY < 0 ||
                offsetX + packed.Width > outputWidth ||
                offsetY + packed.Height > outputHeight)
            {
                throw new InvalidOperationException(
                    "The packed region and its trim offsets do not fit the original canvas.");
            }

            for (var y = 0; y < packed.Height; y++)
            {
                Array.Copy(
                    packed.Pixels,
                    y * packed.Width,
                    output,
                    (offsetY + y) * outputWidth + offsetX,
                    packed.Width);
            }

            return output;
        }

        private static void UnpremultiplyAlpha(Color32[] pixels)
        {
            for (var i = 0; i < pixels.Length; i++)
            {
                var color = pixels[i];
                if (color.a == 0 || color.a == 255)
                    continue;

                color.r = (byte)Mathf.Min(255, Mathf.RoundToInt(color.r * 255f / color.a));
                color.g = (byte)Mathf.Min(255, Mathf.RoundToInt(color.g * 255f / color.a));
                color.b = (byte)Mathf.Min(255, Mathf.RoundToInt(color.b * 255f / color.a));
                pixels[i] = color;
            }
        }

        private static void ConfigureLocalizedTextureImporter(string assetPath, TextureImporter sourceImporter)
        {
            if (AssetImporter.GetAtPath(assetPath) is not TextureImporter importer)
                throw new InvalidOperationException("Unity did not create a TextureImporter for the extracted PNG.");

            importer.textureType = TextureImporterType.Default;
            importer.isReadable = false;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = sourceImporter != null ? sourceImporter.filterMode : FilterMode.Bilinear;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        private static int GetOriginalWidth(AtlasRegion region)
        {
            return region.originalWidth > 0 ? region.originalWidth : region.packedWidth;
        }

        private static int GetOriginalHeight(AtlasRegion region)
        {
            return region.originalHeight > 0 ? region.originalHeight : region.packedHeight;
        }

        private static string Sanitize(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(value.Select(character =>
                invalid.Contains(character) || character == ' ' ? '_' : character).ToArray());
        }

        private readonly struct PackedPixels
        {
            public readonly Color32[] Pixels;
            public readonly int Width;
            public readonly int Height;

            public PackedPixels(Color32[] pixels, int width, int height)
            {
                Pixels = pixels;
                Width = width;
                Height = height;
            }
        }
    }
}
