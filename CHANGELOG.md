# Changelog

## [2.0.0] — 2026-08-27

### Изменения
- Reworked editor and inspector of localization components.
- Added an optional Spine integration with localized attachment replacement, complete atlas-page replacement, multi-page targeting, previews, validation, and region extraction tools.

## [2.0.0-alpha.1] — 2026

A complete rewrite of the package. Not backward compatible with the 1.x line — see [Migration](README.md#migration-from-v1) for the upgrade path.

### What's new

- **Per-language folder structure** — replaces the single `localization.json`. Each language has its own folder with multiple JSON files that get merged at runtime.
- **`LanguageProfile` ScriptableObject** — single source of truth for a language: identity, font (TMP + legacy), font fallback, typography, spacing, layout/direction, per-language fallback chain.
- **Generic asset API** — `Localization.GetAsset<T>(key)` works for any `UnityEngine.Object` type. No more separate `GetSprite`/`GetAudio` methods.
- **`LocalizationAssetTable` ScriptableObject** — one asset table per language, drag-and-drop, supports any asset type per entry.
- **Per-language fallback chains** — `uk → ru → en (global)`, with cycle protection.
- **Pluralization** — built-in rules for Germanic, Slavic, Romance, East Asian and Arabic plural forms via `{N|form1|form2|...}` syntax.
- **Indexed and named parameters** — `{0}`, `{playerName}`, mixed.
- **DI-friendly** — `LocalizationManager` is fully public; can be instantiated and injected via VContainer / Zenject without using the static `Localization` facade.
- **Rewritten editor window** with 8 tabs:
  - **Translations** — virtualized tree, search, multi-select, inline editing, undo/redo, key rename with reference updates in scenes/prefabs.
  - **Assets** — virtualized tree, dynamic type filters, inline previews per language, search, undo/redo, rename.
  - **Languages** — language list with content type badges and fallback chain display, create / add existing / remove.
  - **Profiles** — embedded inspector for editing language profiles in place.
  - **Coverage** — per-language coverage bars and warnings (missing translations, parameter mismatches).
  - **Auto Localize** — bulk-add `LocalizedText` to all text components in a scene.
  - **Tools** — CSV import/export, sort, find unused keys.
  - **Settings** — key conversion mode, logging.
- **Extensibility via interfaces and TypeCache discovery:**
  - `IAssetPreviewRenderer` — custom previews for any asset type
  - `IAssetTypeFilter` — custom filters in the Assets tab
  - `ILocalizationDataProvider` — custom data sources (Addressables, remote, etc.)
  - `[LocalizationEditorTab]` attribute — add custom tabs to the editor window

### Components

- `LocalizedText` — TextMeshPro and legacy `Text`
- `FormattableLocalizedText` — runtime parameters via `SetArgs` / `SetArg` / `SetParam` / `SetParams`
- `LocalizedSprite` — `Image` and `SpriteRenderer`
- `LocalizedAudioClip` — `AudioSource`
- `LocalizedEvent` — invoke language-specific `UnityEvent`s
- `LocalizedProfileOverride` — per-component override of profile sections
- `LocalizedAsset<T>` — generic base for custom localized asset components

### Removed

- Old `LocalizationText`, `FormattableLocalizationText`, `LocalizationImage` components (renamed to `Localized*`)
- `Localization.SetLocalization()` (renamed to `SetLanguage`)
- `Localization.GetSprite()` / `GetAudio()` (replaced by generic `GetAsset<T>()`)
- `text.TranslateByKey()` / `text.SetValue()` runtime methods (replaced by Inspector key + `Key` property + parameter methods)
- Per-language `hasText` / `hasSprites` / `hasAudio` flags (now scanned automatically from real table contents)

---

## Legacy 1.x history

The 1.x series of Simply Localize was a different architecture (single JSON file, enum-based keys, separate sprite/audio APIs). The history is kept here for users migrating from older versions.

## [1.6.5] - 04.10.2025
- Optimized the `SetLanguage(string)` method
- Fixed a bug with `FormattalbeLocalizationText`

## [1.6.4] - 25.08.2025
### Added
- Added the ability to change the key from the code.

## [1.6.3] - 17.08.2025
### Added
- Automatic sorting, grouping of keys and translations in the editor.
- Added functionality for creating your own localization components.
- Added a new localization component for `Sprite Renderer`.
- New function buttons: `Set translation` and `Replace to *component*` for text components.
- Example of using an asset.
- Ability to change the default language via the language selection drop-down list + the ability to disable this functionality.
- Automatic creation of a new language on first launch.
- Ability to disable font replacement when changing the language.

### Fixed
- Reworked logic for sprite localization.

### Changed
- Reworked logic for searching for a key in the localization component.

## [1.6.2] - 11.07.2025
### Fixed
- Fixed a bug with Layout. After exiting maximize mode for any window, the layout was reset to default results.

## [1.6.1] - 28.05.2025
### Changed
- Resource saving logic has been reworked.
- Overall editor performance has been improved.
- All auxiliary buttons have been removed: 'Load from JSON', 'Save to JSON' and 'Generate Keys'.

### Fixed
- Sprite localization has been improved: a bug has been fixed that caused keys to disappear in some languages

## [1.6.0] - 22.05.2025
### Added
- Automatic keys loading when clicking "Load From JSON" button
- Full support for multiline translations
- New language switch handling that removes focus from translation fields

### Updated
- Completely redesigned editor window UI
- Improved text rendering for better readability
- Optimized performance when working with large localization sets

### Fixed
- Minor layout issues in dark/light themes
- Rare case of duplicate key generation

## [1.5.5] - 2025-02-10
### Added
- Added popup window for installing "Unity Localized App Title" package.

## [1.5.4] - 2025-01-26
### Updated
- Updated Logging: changed args to string and added log types.

## [1.5.3] - 2025-01-20
### Updated and changed
- Added real-time update for game window dropdown.

## [1.5.2] - 2025-01-20
### Updated and changed
- Fixed bug with transition between text localization types (Formattable to Base and back).
- Added localization update in editor + setting for this.
- Fixed Logger.
- Remove reinitialization of languages.

## [1.5.1] - 2025-01-20
### Updated and changed
- Updated the language change window in the game view.

## [1.5.0] - 2025-01-19
### Updated and changed
- Removed enum keys. Now they are stored as strings.
- Updated the way of adding keys. Now it is possible to add keys from the input field.
- Added the ability to change the current language at runtime from the inspector.

## [1.4.1] - 2024-12-19
### Changed
- Updated the namespaces for the asset.
- Updated the installation instructions.
- Added image localization component.
- Added logging settings.
- Added localization menu options.

## [1.4.0] - 2024-12-19
### Updated and changed
- Got rid of duplicates for TMP and Legacy components.
- Updated search in the list of keys.
- Updated the validator of the input text.
- Added `WindowEditor` for editing keys.

## [1.3.4] - 2024-10-12
### Updated and changed
* Updated KeyGenerator. Now it also deletes .meta files. 
* Added error handler for situations when there is a conflict of LocalizationKey.cs files.
* Added asset update after adding a key via the LocalizationText component.
* Updated documentation.

## [1.3.3] - 2024-10-09
### Added
* Added the ability to change default values for Formattable components from the inspector.

## [1.3.2] - 2024-09-27
### Fixed
* Replaced the body of the SetValue method for FormattableLocalization components. Now it can accept multiple values.

## [1.3.1] - 2024-08-28
### Fixed
* Fixed a bug where Formattable components were not automatically updated when changing the language.

## [1.3.0] - 2024-08-20
### Changed
* The logic of constructing localization keys has been changed. Now the keys will be stored in the Assets folder, and not in Packages as before. This will help avoid errors and constant recompilations.

## [1.2.5] - 2024-08-19
### Added
* Added the ability to add multiple keys at once.

## [1.2.4] - 2024-07-27
### Added
* Added the ability to change the language at runtime.
* Added documentation in README.MD file.

### Changed
* A small restructuring of methods was made: TryGetKey() and TryGetTranslatedText() were moved from the LocalizationText components to the Localizaton class.

### Bug fixes
* Fixed a bug where when switching fonts it was impossible to return to the original font version.

## [1.2.3] - 2024-07-27 
### Added
* Added the ability to add a new key directly through the input field.

## [1.2.2] - 2024-07-27 
### Added
* Added search field to search for keys. Thanks to https://github.com/roboryantron for providing the search field asset - ***UnityEditorJunkie***.
* Added comments for generated keys.
* Added additional check for created keys. Now when creating a new key and when duplicating keys, the last key will receive the number 2 at the end.

## [1.2.1] - 2024-07-25 
### Changed
* Added the ability to set a default language in the LocalizationKeysData asset. 
* The LocalizationKeysData asset has been moved to the Resources folder for easy access.

## [1.2.0] - 2024-07-23 
### Changed
* Added global localization template generation. Now there is only one .json file with translations and it is generated mainly from code. To work, just add the translations themselves to the created file.
### Removed
* Removed check for "Sample" key. Now this key is not required to be stored and can be easily replaced with any other or deleted.

## [1.1.0] - 2024-07-22 
### Changed
* Updated the logic for creating LocalizationKeysData asset.
* The way enum is serialized has been changed. Now enum values are implemented as strings. (Thanks for asset https://github.com/cathei).

### Added 
* Added the ability to quickly mark formatted keys with #F.
* Added new assembly definition for generated keys.
