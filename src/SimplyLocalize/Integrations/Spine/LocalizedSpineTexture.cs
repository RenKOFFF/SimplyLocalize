using System;
using System.Collections.Generic;
using global::Spine.Unity;
using SimplyLocalize.Components;
using UnityEngine;

namespace SimplyLocalize.SpineIntegration
{
    /// <summary>
    /// Replaces one complete Spine atlas page with a localized Texture2D.
    /// Add one component per localized page when the Spine atlas uses multiple pages.
    /// The page is selected automatically by texture dimensions and, when needed,
    /// by matching the source texture name against the localized texture name.
    /// </summary>
    [AddComponentMenu("SimplyLocalize/Spine/Localized Spine Texture")]
    public sealed class LocalizedSpineTexture : LocalizedAsset<Texture2D>
    {
        [SerializeField]
        [Tooltip("Material of the Spine atlas page to replace. Select it explicitly for " +
                 "multi-page atlases. If empty, the page is resolved from texture size and name.")]
        private Material _atlasPage;

        private SkeletonRenderer _skeletonRenderer;
        private SkeletonGraphic _skeletonGraphic;
        private Material _sourceMaterial;
        private Texture _sourceGraphicTexture;
        private Material _localizedMaterial;

        public Material AtlasPage => _atlasPage;

        public SkeletonDataAsset SkeletonDataAsset
        {
            get
            {
                ResolveTarget();
                return GetSkeletonDataAsset();
            }
        }

        protected override Texture2D ReadCurrentAsset()
        {
            // Missing localization intentionally keeps the original atlas page.
            return null;
        }

        protected override void ApplyAsset(Texture2D texture)
        {
            RestoreOriginalTexture();

            if (texture == null)
                return;

            ResolveTarget();
            var skeletonDataAsset = GetSkeletonDataAsset();
            if (skeletonDataAsset == null)
            {
                Debug.LogError(
                    "[SimplyLocalize] LocalizedSpineTexture requires a SkeletonRenderer " +
                    "or SkeletonGraphic with a SkeletonDataAsset on the same GameObject.",
                    this);
                return;
            }

            var sourceMaterial = ResolveSourceMaterial(skeletonDataAsset, texture);
            if (sourceMaterial == null)
                return;

            _localizedMaterial = new Material(sourceMaterial)
            {
                name = $"{sourceMaterial.name} ({texture.name}, localized)",
                mainTexture = texture
            };
            _sourceMaterial = sourceMaterial;

            if (_skeletonRenderer != null)
            {
                _skeletonRenderer.CustomMaterialOverride[sourceMaterial] = _localizedMaterial;
            }
            else if (_skeletonGraphic != null)
            {
                _sourceGraphicTexture = sourceMaterial.mainTexture;
                _skeletonGraphic.CustomMaterialOverride[_sourceGraphicTexture] = _localizedMaterial;
                _skeletonGraphic.SetMaterialDirty();
                _skeletonGraphic.SetVerticesDirty();
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            RestoreOriginalTexture();
        }

        private void OnDestroy()
        {
            RestoreOriginalTexture();
        }

        private void ResolveTarget()
        {
            if (_skeletonRenderer == null)
                _skeletonRenderer = GetComponent<SkeletonRenderer>();
            if (_skeletonRenderer == null && _skeletonGraphic == null)
                _skeletonGraphic = GetComponent<SkeletonGraphic>();
        }

        private SkeletonDataAsset GetSkeletonDataAsset()
        {
            if (_skeletonRenderer != null)
                return _skeletonRenderer.SkeletonDataAsset;
            return _skeletonGraphic != null ? _skeletonGraphic.SkeletonDataAsset : null;
        }

        private Material ResolveSourceMaterial(
            SkeletonDataAsset skeletonDataAsset,
            Texture2D localizedTexture)
        {
            if (_atlasPage != null)
                return ValidateExplicitAtlasPage(skeletonDataAsset, localizedTexture);

            return FindSourceMaterialAutomatically(skeletonDataAsset, localizedTexture);
        }

        private Material ValidateExplicitAtlasPage(
            SkeletonDataAsset skeletonDataAsset,
            Texture2D localizedTexture)
        {
            var belongsToSkeleton = false;

            foreach (var atlasAsset in skeletonDataAsset.atlasAssets)
            {
                if (atlasAsset == null)
                    continue;

                foreach (var material in atlasAsset.Materials)
                {
                    if (ReferenceEquals(material, _atlasPage))
                    {
                        belongsToSkeleton = true;
                        break;
                    }
                }

                if (belongsToSkeleton)
                    break;
            }

            if (!belongsToSkeleton)
            {
                Debug.LogError(
                    $"[SimplyLocalize] Atlas page material '{_atlasPage.name}' does not belong " +
                    $"to SkeletonDataAsset '{skeletonDataAsset.name}'.",
                    this);
                return null;
            }

            var sourceTexture = _atlasPage.mainTexture;
            if (sourceTexture == null)
            {
                Debug.LogError(
                    $"[SimplyLocalize] Atlas page material '{_atlasPage.name}' has no texture.",
                    this);
                return null;
            }

            if (sourceTexture.width != localizedTexture.width ||
                sourceTexture.height != localizedTexture.height)
            {
                Debug.LogError(
                    $"[SimplyLocalize] Localized texture '{localizedTexture.name}' has size " +
                    $"{localizedTexture.width}x{localizedTexture.height}, but atlas page " +
                    $"'{sourceTexture.name}' requires {sourceTexture.width}x{sourceTexture.height}.",
                    this);
                return null;
            }

            return _atlasPage;
        }

        private Material FindSourceMaterialAutomatically(
            SkeletonDataAsset skeletonDataAsset,
            Texture2D localizedTexture)
        {
            var sizeMatches = new List<Material>();

            foreach (var atlasAsset in skeletonDataAsset.atlasAssets)
            {
                if (atlasAsset == null)
                    continue;

                foreach (var material in atlasAsset.Materials)
                {
                    if (material == null || material.mainTexture == null)
                        continue;

                    if (material.mainTexture.width == localizedTexture.width &&
                        material.mainTexture.height == localizedTexture.height)
                    {
                        sizeMatches.Add(material);
                    }
                }
            }

            if (sizeMatches.Count == 1)
                return sizeMatches[0];

            if (sizeMatches.Count > 1)
            {
                Material nameMatch = null;
                var longestMatchLength = -1;
                var longestMatchIsAmbiguous = false;

                foreach (var material in sizeMatches)
                {
                    var sourceName = material.mainTexture.name;
                    if (localizedTexture.name.IndexOf(sourceName, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    if (sourceName.Length > longestMatchLength)
                    {
                        nameMatch = material;
                        longestMatchLength = sourceName.Length;
                        longestMatchIsAmbiguous = false;
                    }
                    else if (sourceName.Length == longestMatchLength)
                    {
                        longestMatchIsAmbiguous = true;
                    }
                }

                // Prefer the most specific page name. For example,
                // MagazineCovers_2_ru matches both MagazineCovers and
                // MagazineCovers_2, but the latter is the intended page.
                if (nameMatch != null && !longestMatchIsAmbiguous)
                    return nameMatch;
            }

            var reason = sizeMatches.Count == 0
                ? "No atlas page has the same dimensions"
                : "More than one atlas page has the same dimensions and its name is ambiguous";
            Debug.LogError(
                $"[SimplyLocalize] {reason} for localized texture '{localizedTexture.name}' " +
                $"({localizedTexture.width}x{localizedTexture.height}). Keep the localized atlas " +
                "page dimensions unchanged and include the original page name in its filename.",
                this);
            return null;
        }

        private void RestoreOriginalTexture()
        {
            if (_skeletonRenderer != null && _sourceMaterial != null &&
                _skeletonRenderer.CustomMaterialOverride.TryGetValue(_sourceMaterial, out var rendererOverride) &&
                ReferenceEquals(rendererOverride, _localizedMaterial))
            {
                _skeletonRenderer.CustomMaterialOverride.Remove(_sourceMaterial);
            }

            if (_skeletonGraphic != null && _sourceGraphicTexture != null &&
                _skeletonGraphic.CustomMaterialOverride.TryGetValue(_sourceGraphicTexture, out var graphicOverride) &&
                ReferenceEquals(graphicOverride, _localizedMaterial))
            {
                _skeletonGraphic.CustomMaterialOverride.Remove(_sourceGraphicTexture);
                _skeletonGraphic.SetMaterialDirty();
                _skeletonGraphic.SetVerticesDirty();
            }

            if (_localizedMaterial != null)
                Destroy(_localizedMaterial);

            _sourceMaterial = null;
            _sourceGraphicTexture = null;
            _localizedMaterial = null;
        }
    }
}
