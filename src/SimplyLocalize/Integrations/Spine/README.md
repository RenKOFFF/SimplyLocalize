# Simply Localize — Spine integration

Optional Spine support for localizing individual attachments and complete atlas pages.

Runtime and editor code live in separate assemblies. Both assemblies are enabled through a
Unity version define only when `com.esotericsoftware.spine.spine-unity` is installed, so the
future package integration will not make Spine a mandatory Simply Localize dependency.

## Usage

1. Add `Localized Spine Attachment` next to a `SkeletonAnimation`, `SkeletonRenderer`,
   or `SkeletonGraphic` component.
2. Select or create a Simply Localize `Texture2D` asset key.
3. Select the Spine atlas region path to replace, for example `props/poster_scotch`.
4. Expand language rows to compare direct assignments and fallback textures.
5. Under `Localization Template`, select a language and extract the source region.

The generated PNG is imported as a standalone `Texture2D`. Its width and height must
exactly match the original Spine region; a mismatch is shown as an error. Import-setting
issues are shown as warnings. The generated texture can optionally be assigned to the
selected language's `LocalizationAssetTable` immediately.

PMA conversion is selected automatically from the original Spine atlas page. If no
localized texture is available for the active language, the original attachment remains.

An attachment target is stored as `Atlas Page + Region Path`. The page is assigned
automatically by the region picker and is shown in square brackets in the popup. Therefore a
skeleton may use any number of atlas pages while this component replaces only the selected
region on one selected page. Attachments in different skins that use the same region on that
page are localized together; an identically named region on another page is left untouched.

`Advanced/Spine Target` is normally resolved automatically. `Monitor Skin Changes`
reapplies the localized overlay when another runtime system replaces the active Spine skin.

## Full atlas page replacement

Use `Localized Spine Texture` when most or all of an atlas page is localized. The component
uses a Simply Localize `Texture2D` key and finds a `SkeletonRenderer` or `SkeletonGraphic` on
the same GameObject automatically. Select the original material under `Atlas Page`; the
inspector presents it as the page texture name, dimensions, and material name. This explicit
reference is the reliable page identity even when several pages have identical dimensions.

`<Auto: size and filename>` is available as a fallback for simple or previously configured
objects. It first filters pages by exact dimensions and then chooses the longest source page
name contained in the localized filename. For example, `MagazineCovers_2_ru` resolves to
`MagazineCovers_2`, not `MagazineCovers`. If automatic matching is ambiguous, the component
logs an error and leaves the original materials untouched. Explicit `Atlas Page` selection is
recommended for every multi-page skeleton.

One component replaces one atlas page. If a skeleton uses several pages/materials, add one
`Localized Spine Texture` component and one Simply Localize key for every page that contains
localized graphics. Spine may switch atlas pages while an animation changes attachments, so
leaving one of those pages untranslated will make the animation switch back to its original
material. For `MagazineCovers_SkeletonData`, `MagazineCovers_2.png` contains the `stage0`
covers, while `MagazineCovers.png` contains the `stage1` and `stage2` covers.

The localized texture must preserve the source atlas page dimensions, layout, alpha mode
(the MagazineCovers atlas uses PMA), and relevant sampler/import settings. The inspector reports
dimension mismatches as errors, warns when Wrap Mode, Filter Mode, or mip-map settings differ
from the source page, shows its PMA mode, and rejects two components targeting the same page.
PMA pixel data cannot be proven from Unity's import settings, so the source atlas alpha mode is
shown explicitly. The component
clones the source material per skeleton instance and uses Spine's material override dictionaries,
so the shared material asset and other skeleton instances are not modified.
