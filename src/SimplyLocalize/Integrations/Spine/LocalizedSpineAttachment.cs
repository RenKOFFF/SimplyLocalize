using System;
using System.Collections.Generic;
using global::Spine;
using global::Spine.Unity;
using global::Spine.Unity.AttachmentTools;
using SimplyLocalize.Components;
using UnityEngine;

namespace SimplyLocalize.SpineIntegration
{
    /// <summary>
    /// Replaces every Spine RegionAttachment or MeshAttachment that uses a specified
    /// atlas region path with a localized Texture2D from a Simply Localize asset table.
    /// The replacement is applied through a runtime overlay skin, so attachment
    /// timelines continue to work and the original SkeletonData remains untouched.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("SimplyLocalize/Spine/Localized Spine Attachment")]
    public sealed class LocalizedSpineAttachment : LocalizedAsset<Texture2D>
    {
        [SerializeField]
        [Tooltip("SkeletonAnimation, SkeletonRenderer, or SkeletonGraphic to localize. " +
                 "If empty, a supported component is searched on this GameObject.")]
        private Component _target;

        [SerializeField]
        [Tooltip("Spine atlas region path, for example props/poster_scotch. " +
                 "All attachment names that use this path are localized.")]
        private string _regionPath;

        [SerializeField]
        [HideInInspector]
        [Tooltip("Material of the Spine atlas page containing the selected region.")]
        private Material _atlasPage;

        [SerializeField]
        [Tooltip("Reapply the localization if another system assigns a different Spine skin at runtime.")]
        private bool _monitorSkinChanges = true;

        private SkeletonRenderer _skeletonRenderer;
        private SkeletonGraphic _skeletonGraphic;
        private ISkeletonComponent _skeletonComponent;
        private IAnimationStateComponent _animationStateComponent;

        private readonly Dictionary<RegionResourceKey, RuntimeRegionResource> _regionResources = new();

        private Texture2D _localizedTexture;
        private Skin _baseSkin;
        private Skin _appliedCombinedSkin;
        private bool _isApplying;
        private bool _reportedMissingAttachments;

        public Component Target => _target;
        public string RegionPath => _regionPath;
        public Material AtlasPage => _atlasPage;
        public SkeletonDataAsset SkeletonDataAsset
        {
            get
            {
                ResolveTarget();
                return _skeletonComponent?.SkeletonDataAsset;
            }
        }

        protected override void OnEnable()
        {
            ResolveTarget();
            SubscribeToRebuild();
            base.OnEnable();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            UnsubscribeFromRebuild();
            RestoreBaseSkin();
        }

        private void OnDestroy()
        {
            ReleaseRuntimeResources();
        }

        private void LateUpdate()
        {
            if (!_monitorSkinChanges || _localizedTexture == null || _isApplying)
                return;

            var skeleton = GetSkeleton();
            if (skeleton == null || _appliedCombinedSkin == null)
                return;

            if (!ReferenceEquals(skeleton.Skin, _appliedCombinedSkin))
            {
                _baseSkin = skeleton.Skin;
                ApplyLocalizedTexture(_localizedTexture);
            }
        }

        protected override Texture2D ReadCurrentAsset()
        {
            // A missing localized asset deliberately means "use the original Spine attachment".
            return null;
        }

        protected override void ApplyAsset(Texture2D asset)
        {
            _localizedTexture = asset;

            if (asset == null)
            {
                RestoreBaseSkin();
                return;
            }

            ApplyLocalizedTexture(asset);
        }

        private void ApplyLocalizedTexture(Texture2D texture)
        {
            if (_isApplying || texture == null || string.IsNullOrWhiteSpace(_regionPath))
                return;

            ResolveTarget();
            var skeleton = GetSkeleton();
            if (skeleton == null)
                return;

            _isApplying = true;
            try
            {
                if (!ReferenceEquals(skeleton.Skin, _appliedCombinedSkin))
                    _baseSkin = skeleton.Skin;

                var localizedSkin = BuildLocalizedSkin(skeleton.Data, _baseSkin, texture);
                if (localizedSkin == null)
                {
                    if (!_reportedMissingAttachments)
                    {
                        Debug.LogError(
                            $"[SimplyLocalize] No supported Spine attachments use region path '{_regionPath}'.",
                            this);
                        _reportedMissingAttachments = true;
                    }

                    return;
                }

                _reportedMissingAttachments = false;

                var combinedSkin = new Skin($"SimplyLocalize/{_regionPath}/{texture.name}");
                if (_baseSkin != null)
                    combinedSkin.AddSkin(_baseSkin);
                combinedSkin.AddSkin(localizedSkin);

                _appliedCombinedSkin = combinedSkin;
                skeleton.SetSkin(combinedSkin);
                ReapplySetupPose(skeleton);
            }
            finally
            {
                _isApplying = false;
            }
        }

        private Skin BuildLocalizedSkin(SkeletonData skeletonData, Skin activeBaseSkin, Texture2D texture)
        {
            if (skeletonData == null)
                return null;

            var sources = CollectSourceAttachments(skeletonData, activeBaseSkin);
            if (sources.Count == 0)
                return null;

            var localizedSkin = new Skin($"SimplyLocalize/{_regionPath}");
            var replacementCount = 0;

            foreach (var source in sources.Values)
            {
                if (source.Attachment is not IHasTextureRegion texturedAttachment ||
                    texturedAttachment.Region is not AtlasRegion sourceRegion ||
                    sourceRegion.page?.rendererObject is not Material sourceMaterial)
                {
                    continue;
                }

                var localizedRegion = GetOrCreateLocalizedRegion(
                    texture,
                    sourceMaterial,
                    sourceRegion.page.pma);
                if (localizedRegion == null)
                    continue;

                var replacement = source.Attachment.GetRemappedClone(
                    localizedRegion,
                    cloneMeshAsLinked: true,
                    useOriginalRegionSize: true,
                    scale: 1f);

                if (replacement == null)
                    continue;

                localizedSkin.SetAttachment(source.SlotIndex, source.Name, replacement);
                replacementCount++;
            }

            return replacementCount > 0 ? localizedSkin : null;
        }

        private Dictionary<AttachmentKey, SourceAttachment> CollectSourceAttachments(
            SkeletonData skeletonData,
            Skin activeBaseSkin)
        {
            var result = new Dictionary<AttachmentKey, SourceAttachment>();

            AddMatchingAttachments(skeletonData.DefaultSkin, result);

            foreach (var skin in skeletonData.Skins)
                AddMatchingAttachments(skin, result);

            // A runtime-combined character skin may contain more specific versions.
            // Add it last so its attachment wins for the same slot/name pair.
            AddMatchingAttachments(activeBaseSkin, result);

            return result;
        }

        private void AddMatchingAttachments(
            Skin skin,
            IDictionary<AttachmentKey, SourceAttachment> result)
        {
            if (skin == null)
                return;

            foreach (var entry in skin.Attachments)
            {
                if (entry.Attachment is not IHasTextureRegion textured ||
                    !string.Equals(textured.Path, _regionPath, StringComparison.Ordinal))
                {
                    continue;
                }

                if (_atlasPage != null &&
                    (textured.Region is not AtlasRegion atlasRegion ||
                     !ReferenceEquals(atlasRegion.page?.rendererObject, _atlasPage)))
                {
                    continue;
                }

                if (entry.Attachment is not RegionAttachment && entry.Attachment is not MeshAttachment)
                    continue;

                var key = new AttachmentKey(entry.SlotIndex, entry.Name);
                result[key] = new SourceAttachment(entry.SlotIndex, entry.Name, entry.Attachment);
            }
        }

        private AtlasRegion GetOrCreateLocalizedRegion(
            Texture2D texture,
            Material sourceMaterial,
            bool premultiplyAlpha)
        {
            var key = new RegionResourceKey(texture, sourceMaterial, premultiplyAlpha);
            if (_regionResources.TryGetValue(key, out var cached))
                return cached.Region;

            AtlasRegion region;
            if (premultiplyAlpha)
            {
                Texture2D readableCopy = null;
                try
                {
                    var pmaSource = texture;
                    if (!texture.isReadable)
                    {
                        readableCopy = CreateReadableCopy(texture);
                        pmaSource = readableCopy;
                    }

                    region = pmaSource.ToAtlasRegionPMAClone(
                        sourceMaterial,
                        TextureFormat.RGBA32,
                        mipmaps: false);
                }
                finally
                {
                    if (readableCopy != null)
                        Destroy(readableCopy);
                }
            }
            else
            {
                var material = new Material(sourceMaterial)
                {
                    name = $"{sourceMaterial.name} ({texture.name}, localized)",
                    mainTexture = texture
                };
                region = texture.ToAtlasRegion(material);
            }

            if (region == null)
                return null;

            // Texture2D.ToAtlasRegion treats the texture origin like a bottom-left pivot
            // and assigns half-size trim offsets. That is unsuitable for a standalone
            // full-canvas localization texture: MeshAttachment.UpdateRegion then produces
            // UVs around -0.5..0.5, which tile when the generated PMA texture uses Repeat.
            // The extractor restores the complete original canvas, so this region is
            // deliberately untrimmed and centered.
            region.offsetX = 0f;
            region.offsetY = 0f;
            region.originalWidth = texture.width;
            region.originalHeight = texture.height;
            region.packedWidth = texture.width;
            region.packedHeight = texture.height;
            region.degrees = 0;
            region.rotate = false;

            if (region.page != null)
            {
                region.page.uWrap = TextureWrap.ClampToEdge;
                region.page.vWrap = TextureWrap.ClampToEdge;
            }

            var ownedMaterial = region.page?.rendererObject as Material;
            var regionTexture = ownedMaterial != null ? ownedMaterial.mainTexture as Texture2D : null;
            if (regionTexture != null)
                regionTexture.wrapMode = TextureWrapMode.Clamp;
            var ownedTexture = regionTexture != null && regionTexture != texture
                ? regionTexture
                : null;

            _regionResources[key] = new RuntimeRegionResource(region, ownedMaterial, ownedTexture);
            return region;
        }

        private static Texture2D CreateReadableCopy(Texture2D source)
        {
            var temporary = RenderTexture.GetTemporary(
                source.width,
                source.height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Default);
            var previous = RenderTexture.active;

            try
            {
                Graphics.Blit(source, temporary);
                RenderTexture.active = temporary;

                var copy = new Texture2D(
                    source.width,
                    source.height,
                    TextureFormat.RGBA32,
                    mipChain: false,
                    linear: false)
                {
                    name = $"{source.name} (readable temporary)"
                };
                copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
                copy.Apply(false, false);
                return copy;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
            }
        }

        private void RestoreBaseSkin()
        {
            var skeleton = GetSkeleton();
            if (skeleton == null || _appliedCombinedSkin == null)
                return;

            if (ReferenceEquals(skeleton.Skin, _appliedCombinedSkin))
            {
                skeleton.SetSkin(_baseSkin);
                ReapplySetupPose(skeleton);
            }

            _appliedCombinedSkin = null;
        }

        private void ReapplySetupPose(Skeleton skeleton)
        {
            skeleton.SetSlotsToSetupPose();
            _animationStateComponent?.AnimationState?.Apply(skeleton);
        }

        private Skeleton GetSkeleton()
        {
            ResolveTarget();
            return _skeletonComponent?.Skeleton;
        }

        private void ResolveTarget()
        {
            var resolved = _target;
            if (resolved == null)
            {
                resolved = GetComponent<SkeletonRenderer>();
                if (resolved == null)
                    resolved = GetComponent<SkeletonGraphic>();
            }

            if (ReferenceEquals(resolved, _skeletonRenderer) ||
                ReferenceEquals(resolved, _skeletonGraphic))
            {
                return;
            }

            UnsubscribeFromRebuild();

            _skeletonRenderer = resolved as SkeletonRenderer;
            _skeletonGraphic = resolved as SkeletonGraphic;
            _skeletonComponent = resolved as ISkeletonComponent;
            _animationStateComponent = resolved as IAnimationStateComponent;

            if (_target == null && resolved != null)
                _target = resolved;

            if (isActiveAndEnabled)
                SubscribeToRebuild();
        }

        private void SubscribeToRebuild()
        {
            if (_skeletonRenderer != null)
            {
                _skeletonRenderer.OnRebuild -= HandleRendererRebuild;
                _skeletonRenderer.OnRebuild += HandleRendererRebuild;
            }

            if (_skeletonGraphic != null)
            {
                _skeletonGraphic.OnRebuild -= HandleGraphicRebuild;
                _skeletonGraphic.OnRebuild += HandleGraphicRebuild;
            }
        }

        private void UnsubscribeFromRebuild()
        {
            if (_skeletonRenderer != null)
                _skeletonRenderer.OnRebuild -= HandleRendererRebuild;

            if (_skeletonGraphic != null)
                _skeletonGraphic.OnRebuild -= HandleGraphicRebuild;
        }

        private void HandleRendererRebuild(SkeletonRenderer _)
        {
            HandleSkeletonRebuild();
        }

        private void HandleGraphicRebuild(SkeletonGraphic _)
        {
            HandleSkeletonRebuild();
        }

        private void HandleSkeletonRebuild()
        {
            _appliedCombinedSkin = null;
            _baseSkin = GetSkeleton()?.Skin;

            if (_localizedTexture != null)
                ApplyLocalizedTexture(_localizedTexture);
        }

        private void ReleaseRuntimeResources()
        {
            foreach (var resource in _regionResources.Values)
            {
                if (resource.Material != null)
                    Destroy(resource.Material);
                if (resource.Texture != null)
                    Destroy(resource.Texture);
            }

            _regionResources.Clear();
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();

            if (_target == null)
            {
                _target = GetComponent<SkeletonRenderer>();
                if (_target == null)
                    _target = GetComponent<SkeletonGraphic>();
            }
        }
#endif

        private readonly struct AttachmentKey : IEquatable<AttachmentKey>
        {
            private readonly int _slotIndex;
            private readonly string _name;

            public AttachmentKey(int slotIndex, string name)
            {
                _slotIndex = slotIndex;
                _name = name;
            }

            public bool Equals(AttachmentKey other)
            {
                return _slotIndex == other._slotIndex &&
                       string.Equals(_name, other._name, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is AttachmentKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (_slotIndex * 397) ^ (_name != null ? _name.GetHashCode() : 0);
                }
            }
        }

        private readonly struct SourceAttachment
        {
            public readonly int SlotIndex;
            public readonly string Name;
            public readonly Attachment Attachment;

            public SourceAttachment(int slotIndex, string name, Attachment attachment)
            {
                SlotIndex = slotIndex;
                Name = name;
                Attachment = attachment;
            }
        }

        private readonly struct RegionResourceKey : IEquatable<RegionResourceKey>
        {
            private readonly Texture2D _texture;
            private readonly Material _material;
            private readonly bool _pma;

            public RegionResourceKey(Texture2D texture, Material material, bool pma)
            {
                _texture = texture;
                _material = material;
                _pma = pma;
            }

            public bool Equals(RegionResourceKey other)
            {
                return ReferenceEquals(_texture, other._texture) &&
                       ReferenceEquals(_material, other._material) &&
                       _pma == other._pma;
            }

            public override bool Equals(object obj)
            {
                return obj is RegionResourceKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = _texture != null ? _texture.GetHashCode() : 0;
                    hash = (hash * 397) ^ (_material != null ? _material.GetHashCode() : 0);
                    return (hash * 397) ^ _pma.GetHashCode();
                }
            }
        }

        private readonly struct RuntimeRegionResource
        {
            public readonly AtlasRegion Region;
            public readonly Material Material;
            public readonly Texture2D Texture;

            public RuntimeRegionResource(AtlasRegion region, Material material, Texture2D texture)
            {
                Region = region;
                Material = material;
                Texture = texture;
            }
        }
    }
}
