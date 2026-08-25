# Shared Scene Action Profile Implementation Plan

## 1. Goal

Allow multiple CharaStudio scene cards that represent the same Timeline scene with different characters to use one shared OSR2/SR6 action set.

The implementation must provide these behaviors:

- Editing and saving a shared action set from one bound scene updates what every other bound scene loads next time.
- A scene can switch to or fork a different action set when its animation or character motion does not match the shared version.
- Replacing character cards without changing the logical female/male slots does not require rebuilding every scene-part binding.
- Existing scene-local `.sr6script`, `.sr6cfg`, and raw `.txt` files keep working without migration.
- A scene with a valid shared profile can play even when that scene has no raw sampled `.txt` file.
- The built-in CharaStudio scene browser shows whether a scene has a usable shared or legacy action set.
- Broken or incomplete references never silently send invalid motion to hardware.

The first implementation targets the WPF desktop application. The Qt application keeps its current legacy behavior and file compatibility, but does not need shared-profile UI support in this phase.

## 2. Current Constraints

The current implementation derives every action file from the raw scene `.txt` path:

- Plugin scene path mapping and TCP send: `plugin/Osr2_sr6_link.cs:316-342`, `plugin/Osr2_sr6_link.cs:599-640`
- WPF per-scene path helpers: `wpf/KKOsr2Sr6Link.Wpf/Engine/Axis.cs:39-47`
- WPF scene load and automatic legacy creation: `wpf/KKOsr2Sr6Link.Wpf/MainWindow.xaml.cs:511-617`
- WPF save always writes back to the current scene stem: `wpf/KKOsr2Sr6Link.Wpf/MainWindow.xaml.cs:619-625`
- Existing action formats: `wpf/KKOsr2Sr6Link.Wpf/Engine/SceneFiles.cs:25-110`

The current character binding compares the full display string exactly:

```csharp
_lovemakingDatas.FirstOrDefault(x => x.CharasName == scenePart.Charas)
```

The plugin writes values such as:

```text
Girl Name(chaF_001)-Boy Name(chaM_001)
```

Changing the character display name therefore breaks matching even when `chaF_001` and `chaM_001` still represent the same logical slots.

The plugin currently sends scene messages only when the raw `.txt` exists. That gate must change because a valid shared profile is sufficient for playback.

## 3. Chosen Design

Use a central action-profile library. A scene card stores only an immutable profile key, not a copy of the full action data.

Do not embed the six action streams in every scene card. Full embedding creates independent copies, so updating one action would not update other cards.

### 3.1 Profile Library

Store shared profiles under the existing data root:

```text
UserData/KK_osr_sr6_link/_profiles/
```

Each profile reuses the existing Qt-compatible files:

```text
<profileKey>.sr6script
<profileKey>.surge.sr6script
<profileKey>.sway.sr6script
<profileKey>.twist.sr6script
<profileKey>.roll.sr6script
<profileKey>.pitch.sr6script
<profileKey>.sr6cfg
```

No new bundled action format is required. Reuse `SceneFiles.LoadSr6Script`, `SaveSr6Script`, `LoadSr6Cfg`, and `SaveSr6Cfg`.

`profileKey` rules:

- It is an immutable filename stem selected when the profile is created.
- It is stored without an absolute path.
- It must pass normal Windows filename validation.
- It must not contain directory separators, `|`, or `:`, and it must not equal `.` or `..`.
- Duplicate keys are rejected instead of overwriting another profile accidentally.
- Profile renaming is outside the first implementation. A new profile can be created instead.

### 3.2 Scene Reference Sidecar

Add one scene-local reference file:

```text
<scene-stem>.sr6ref
```

The file contains one UTF-8 line with the `profileKey`.

Purposes:

- Fast WPF lookup before loading action files.
- Fast scene-browser badge lookup without parsing arbitrary scene PNG data.
- Local cache when the scene card already contains the same ExtendedSave profile key.

The `.sr6ref` is not the action source. It only points to the central profile.

### 3.3 Scene Card Metadata

Use ExtendedSave scene data with a stable plugin data ID. Save only:

```text
version = 1
profileKey = <key>
```

The scene card metadata allows the binding to survive Studio Save As, scene renaming, and scene copying when the corresponding profile library is still installed.

Use `KKAPI.Studio.SaveLoad.SceneCustomFunctionController` for scene lifecycle handling:

- `Load`: replace the current in-memory profile key with the loaded card value.
- `Clear`: clear the current profile key.
- `Import`: keep the current scene profile key. Imported scene metadata must not replace the current scene binding.
- `Save`: write the current profile key, or remove this plugin's data when no profile is bound.

Subscribe to `ExtensibleSaveFormat.ExtendedSave.SceneBeingSaved(string path)` only for the actual save path. Use it to create or clear the `.sr6ref` beside the newly saved scene path, including Studio Save As.

Add a direct project reference to `KKS_ExtensibleSaveFormat.dll`. Declare runtime dependencies on KKSAPI and ExtendedSave using their installed plugin IDs or constants.

### 3.4 Reference Precedence

When both local and card references are available:

1. A valid local `.sr6ref` wins because it may represent a binding changed in WPF but not yet saved back into the Studio scene card.
2. If no local reference exists, use the card `profileKey` received from the plugin and recreate `.sr6ref`.
3. If the two values differ, load the local value and show that the scene card must be saved once to persist the new binding.

Changing or forking a profile updates `.sr6ref` immediately and sends the new key to the plugin. The scene card is updated the next time the user saves in Studio.

## 4. Action Source Resolution

Change WPF scene loading to resolve action sources in this order:

1. Valid shared profile selected by `.sr6ref` or scene-card metadata.
2. Complete legacy scene-local six-axis `.sr6script` set and `.sr6cfg`.
3. Raw sampled `.txt`, using the current generation behavior.

Rules:

- A shared profile is complete only when all six axis files and `.sr6cfg` exist and parse successfully.
- All six axis action arrays must have the same non-zero length.
- A legacy action set uses the same completeness rule. Do not keep the current L0-only existence check.
- If a referenced profile is missing, malformed, incomplete, or has inconsistent axis lengths, report the broken reference and try the legacy or raw fallback without deleting or overwriting the reference.
- If no safe source exists, load no actions and send no hardware output.
- Raw `.txt` remains scene-specific. It is optional for shared-profile playback but still required for regenerating actions from scene geometry.
- When a valid profile loads without raw data, manual curve playback and editing remain available. Controls that require `_lovemakingDatas` must be disabled with a clear status message.
- Funscript export continues to use the currently loaded in-memory axes and the current scene export path.

Add small aggregate helpers around the existing per-file methods rather than introducing a new storage abstraction:

- Resolve profile stem from game root and `profileKey`.
- Resolve `.sr6ref` path from a scene `.txt` path.
- Check whether all seven action files form a complete set.
- Load and save all six axes plus scene parts for a target stem.

## 5. Character-Pair Matching

Add one parser that extracts the stable slot key from both full display labels and root-only values:

```text
Girl Name(chaF_001)-Boy Name(chaM_001)
chaF_001-chaM_001
```

Both normalize to:

```text
chaF_001-chaM_001
```

Use this parser everywhere touched by shared-profile loading instead of `string.Split('-')` or full-string equality.

Loading a shared `.sr6cfg` into a scene with raw data must:

1. Extract the saved female and male root slots.
2. Find the current scene's `LovemakingData.CharasName` with the same normalized slot key.
3. Replace `ScenePart.Charas` in memory with the current scene's full display label.
4. Keep the existing `.sr6cfg` JSON shape when saving so Qt can still parse it.

If a slot cannot be resolved:

- Keep the saved part and curve data available for playback.
- Mark that part as unresolved for raw regeneration.
- Require manual selection or a forked profile instead of silently selecting the first pair.

This parser also removes the current dependency on splitting display names at every `-`, which is unsafe when a character name itself contains a hyphen.

## 6. TCP Contract

Extend plugin-to-desktop scene messages with one optional fourth field:

```text
path|index|interval|profileKey
```

Compatibility requirements:

- WPF accepts both three-field and four-field messages.
- An empty fourth field means no card profile binding.
- Qt continues reading fields 0 through 2 and ignores the extra field.
- `profileKey` filename validation prevents `|` and `:` from entering the protocol.

Add one desktop-to-plugin command:

```text
5:<profileKey>
```

`5:` clears the current binding.

The binding command is infrequent state persistence, not timeline control. Do not subject it to the existing 200 ms play/seek throttle. Return success or failure to the WPF handler so a disconnected plugin cannot look like a saved binding.

The plugin receive loop runs on a background thread while scene save callbacks run on Unity's main thread. Protect the current profile key with the smallest appropriate synchronization or atomic publication.

### 6.1 Sending Without Raw TXT

Replace the plugin's current send gate:

```text
raw .txt exists
```

with:

```text
raw .txt exists OR current scene has a profileKey
```

The plugin still sends the expected raw `.txt` path even when that file does not exist. WPF uses the path to resolve the scene PNG, `.sr6ref`, legacy fallback, and profile root.

After a successful scene load, send one scene message even when Timeline is paused. WPF needs the scene path and profile key before playback begins.

## 7. WPF User Interface

Add the shared-profile controls near the current Save button in `MainWindow.xaml:243-260`.

Required UI state:

- Current source: `Scene local` or `Shared: <profileKey>`.
- Profile selector listing complete profiles under `_profiles`.
- `Create shared profile`: save the current in-memory axes and parts under a new validated key, bind the current scene, update `.sr6ref`, and notify the plugin.
- `Fork profile`: copy the current in-memory axes and parts to a new key and bind only the current scene to it.
- `Clear shared binding`: clear the reference and return to a safe scene-local source only when one exists or after explicitly saving the current data locally.
- Existing Save button writes to the active source.
- Shared Save button text must clearly identify global impact, for example `Save shared profile: <profileKey>`.
- Show `Save the Studio scene once to store this binding in the card` after a binding change.

Do not add profile rename, profile deletion, profile usage counting, live file watching, or batch card rewriting in this phase.

Add all user-visible strings to both localization dictionaries:

- `wpf/KKOsr2Sr6Link.Wpf/Localization/Strings.en.xaml`
- `wpf/KKOsr2Sr6Link.Wpf/Localization/Strings.zh-Hant.xaml`

## 8. Built-In Scene Browser Badge

Target the built-in `Studio.SceneLoadScene` UI confirmed by the user.

Use Harmony postfixes after scene list/page creation:

- `SceneLoadScene.InitInfo()`
- `SceneLoadScene.SetPage(int)`

Use the existing private fields after verifying their runtime index relationship:

- `listPath`
- `thumbnailNum`
- `page`
- `buttonThumbnail`
- `dicPage`

For each visible scene path, map the PNG path to the existing action-data stem and inspect only filesystem references.

Badge states:

- Green `SR6`: `.sr6ref` points to a complete shared profile.
- Gray `SR6`: no shared reference, but a complete legacy scene-local action set exists.
- Red `SR6!`: `.sr6ref` exists but the referenced profile is missing or incomplete.
- No badge: no usable final action set.

Create or reuse one child badge object per thumbnail. Refreshing pages must not add duplicate badge objects. Use a small Unity text/background overlay; do not modify the scene PNG texture.

Start with direct `File.Exists` and small-file validation. Add caching only if runtime measurement shows the built-in browser becomes slow.

Known limitation:

- A scene card copied from another machine with embedded profile metadata but no local `.sr6ref` cannot show a pre-load badge through the public ExtendedSave API. Its reference is recreated after the first load. Arbitrary PNG ExtendedSave parsing is outside this phase.

## 9. Legacy Migration

Migration is explicit, never automatic.

For an existing scene with local `.sr6script/.sr6cfg`:

1. Load it exactly as today.
2. User selects `Create shared profile`.
3. WPF saves the current in-memory action set under `_profiles/<profileKey>`.
4. WPF writes `.sr6ref` and sends command `5:<profileKey>`.
5. User saves the Studio scene once so ExtendedSave stores the same key in the card.

Do not move, delete, or rewrite existing legacy files during promotion. They remain a fallback until the user removes them manually.

When a new character variant is made by loading a bound scene and using Studio Save As, the in-memory profile key is written into the new scene card and the save-path event creates the new `.sr6ref`. No repeated action binding is required.

## 10. Error Handling

Required failures must be visible and safe:

- Invalid profile key: reject before file or TCP use.
- Duplicate profile key: reject without overwrite.
- Missing one or more profile files: mark broken and fall back.
- Malformed JSON: mark broken and fall back.
- Unequal or empty axis lengths: mark broken and fall back.
- Profile selected while plugin is disconnected: keep WPF state explicit as not stored in the scene card.
- Save failure: keep the previous active source and report the failed path.
- Missing raw `.txt`: allow profile playback but disable regeneration features.
- Playback index outside loaded action bounds: send no hardware value for that frame and report the incompatibility once, not continuously.

Do not attempt to detect semantic motion differences between two characters automatically. Equal length does not prove that body motion is identical. The supported answer is an explicit forked profile.

## 11. Expected Files to Change

Plugin:

- `plugin/kk_osr2_sr6_link.csproj`
- `plugin/Osr2_sr6_link.cs`
- One small scene-profile controller source file only if keeping the controller in the existing large plugin file would make the lifecycle logic unclear.

WPF application:

- `wpf/KKOsr2Sr6Link.Wpf/Engine/Axis.cs`
- `wpf/KKOsr2Sr6Link.Wpf/Engine/SceneFiles.cs`
- `wpf/KKOsr2Sr6Link.Wpf/Engine/Models.cs`
- `wpf/KKOsr2Sr6Link.Wpf/Engine/LinkServer.cs`
- `wpf/KKOsr2Sr6Link.Wpf/MainWindow.xaml`
- `wpf/KKOsr2Sr6Link.Wpf/MainWindow.xaml.cs`
- `wpf/KKOsr2Sr6Link.Wpf/Localization/Strings.en.xaml`
- `wpf/KKOsr2Sr6Link.Wpf/Localization/Strings.zh-Hant.xaml`

Tests:

- `wpf/KKOsr2Sr6Link.Tests/SceneFilesTests.cs`
- `wpf/KKOsr2Sr6Link.Tests/SceneRoundTripTests.cs`
- `wpf/KKOsr2Sr6Link.Tests/LinkServerTests.cs`
- A focused pair-key parser test file if the behavior does not fit an existing test class cleanly.

Qt source should not require changes in this phase.

## 12. Implementation Order

### Phase 1: File and Pair Primitives

- Add profile-root, profile-stem, and `.sr6ref` path helpers.
- Add complete seven-file action-set load/save validation.
- Add `profileKey` validation.
- Add normalized female/male slot parsing and current-label resolution.
- Add unit tests before connecting UI behavior.

### Phase 2: WPF Source Resolution

- Extend `SceneMessage` and parser for optional `profileKey`.
- Implement shared, legacy, and raw load precedence.
- Permit shared-profile loading without raw `.txt`.
- Route Save to the active action source.
- Add create, select, fork, and clear-binding UI.
- Add localization and status messages.
- Extend round-trip tests for two scenes sharing one profile and one scene forking.

### Phase 3: Plugin Scene Metadata

- Add ExtendedSave compile/runtime dependency.
- Register the scene custom controller early in `Start()`.
- Read and write card `profileKey` with correct Load, Import, Clear, and Save behavior.
- Use the actual save-path event to create `.sr6ref` on Save As.
- Add command `5` handling and thread-safe current-profile state.
- Send optional `profileKey` and allow profile-only scenes to produce timeline messages.

### Phase 4: Scene Browser Badge

- Verify built-in page/index mapping with temporary debug logs.
- Add idempotent thumbnail badges in `InitInfo` and `SetPage` postfixes.
- Validate shared, legacy, broken, page-change, refresh, and Save As cases in CharaStudio.
- Remove temporary debug logging after the mapping is proven.

### Phase 5: Verification

- Run `dotnet test` for the WPF solution.
- Run `dotnet build` for the WPF application.
- Build the plugin against the installed KKS assemblies.
- Test in CharaStudio with WPF connected.
- Confirm Qt still parses old scene-local files and ignores the fourth TCP field.

## 13. Automated Test Cases

Add coverage for:

- Three-field TCP messages remain valid.
- Four-field TCP messages expose `profileKey`.
- Empty fourth field means no profile.
- Invalid profile keys are rejected.
- `.sr6ref` round-trips one key.
- Complete profile requires all six axes and `.sr6cfg`.
- One missing axis rejects the profile.
- Unequal action lengths reject the profile.
- Scene A and Scene B referencing one profile load identical actions.
- Saving shared actions from Scene A changes what Scene B loads.
- Forking Scene C creates an independent profile and does not modify the original.
- Broken shared reference falls back to complete legacy files.
- Broken shared reference with no fallback loads no hardware actions.
- Legacy scene without metadata behaves as before.
- Full labels with different character names but the same `chaF_...` and `chaM_...` roots resolve to the same pair.
- Character names containing hyphens do not break slot parsing.
- Shared profile can load when raw `.txt` is absent.
- Raw-dependent regeneration is unavailable when raw `.txt` is absent.

## 14. Manual Acceptance Criteria

The feature is complete when all of these are demonstrated:

1. Bind Scene A and Scene B to one shared profile.
2. Edit and save the shared curve from Scene A.
3. Load Scene B and observe the same saved curve and scene parts without rebuilding bindings.
4. Replace character cards while retaining `chaF_...` and `chaM_...` slots and confirm parts resolve to the new display names.
5. Create Scene C by Studio Save As and confirm the profile binding and scene-browser badge persist.
6. Fork Scene C to another profile, edit it, and confirm Scene A and Scene B remain unchanged.
7. Load a profile-bound scene with no raw `.txt` and confirm normal playback works while regeneration controls are unavailable.
8. Load a legacy scene with no card metadata or `.sr6ref` and confirm existing behavior is unchanged.
9. Break a profile by removing one axis file and confirm the red badge, visible warning, safe fallback, and no invalid hardware output.
10. Import another scene into the current scene and confirm the current profile binding is not replaced.
11. Change pages and refresh the built-in scene browser and confirm badges remain correct without duplicates.

## 15. Out of Scope

- Embedding full action arrays into every scene card.
- Automatic semantic comparison of character motion.
- Automatic grouping of old scenes by filename, hash, or guessed similarity.
- Profile rename and deletion management.
- Batch rewriting all existing scene cards.
- Parsing arbitrary scene PNG ExtendedSave data only to show pre-load badges.
- Qt shared-profile creation or selection UI.
- Network framing redesign for the existing TCP protocol.
- Live file watching or automatic reload while another process edits a profile.
