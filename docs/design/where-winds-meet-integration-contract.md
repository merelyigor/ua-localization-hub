# Where Winds Meet / Winds4UA — Stage 9 integration contract

Status: **STAGE 9 — REVIEWED / ACCEPTED**
Evidence checked: **2026-10-08**
Scope: read-only analysis; no game or production runtime changes.

Owner decisions recorded: stable game ID `where-winds-meet`; initial implementation scope is Steam-first; restore means return to the exact pre-Hub state, not guaranteed stock bytes. Stage 10 remains unauthorized.

## Evidence boundary

- **Steam — LOCALLY VERIFIED:** Steam AppID `3564740`, manifest association, installed root markers, executable, and Steam locale directory were inspected read-only. The local Steam manifest reports build ID `25281753`.
- **Official launcher — DOCUMENTED / NOT LOCALLY VERIFIED / DEFERRED:** Winds4UA documents its target directory, but this machine's installation does not establish that launcher layout or its detection markers. It is outside the initial Steam-first scope and needs separate Owner authorization/verification.
- Public facts below were checked against the [Winds4UA download instructions](https://winds4ua.com.ua/download), the [current public API documentation](https://winds4ua.com.ua/docs/public-api/releases), and read-only requests to the production API. The API host is `winds4ua.com.ua`.
- The current package was downloaded to isolated temporary storage, hash-checked and listed; entries were compared read-only with the local Steam locale directory. No archive content was copied to the game.

## Steam detection and root validation

Steam's library metadata associates AppID `3564740` with `installdir` `Where Winds Meet`; the installed root is the corresponding `steamapps/common/Where Winds Meet` directory. The manifest had `StateFlags=4` and `buildid=25281753` (also `TargetBuildID=25281753`).

Approved initial detection scope is Steam AppID `3564740`, stable game ID `where-winds-meet`, with root `<SteamLibrary>\\steamapps\\common\\Where Winds Meet`. Detection should use:

| Marker | Requirement | Meaning |
|---|---|---|
| Steam `libraryfolders.vdf` entry plus `appmanifest_3564740.acf` with matching AppID/install directory | Required for automatic Steam discovery | Associates the root with the expected Steam product; do not trust a path alone. |
| `Engine/Binaries/Win64r/wwm.exe` | Required root validation marker | Expected main executable exists under the manifest-derived root. |
| `Package/HD/oversea/locale/` | Required localization-target marker | Expected Steam locale target exists. |

The conjunction of Steam association and both product-specific paths is a bounded false-positive check; it is not proof of a particular game build or unmodified files. Manual selection must apply the same root checks. Do not infer the Steam library from a hard-coded drive.

The executable exists (60,421,624 bytes), but Windows version-resource fields inspected—FileVersion, ProductVersion, ProductName, FileDescription, CompanyName and OriginalFilename—are empty/zero. Other observed local version-like values are not yet authoritative compatibility inputs: `Engine/Binaries/Win64r/Built.version` reports `2026-09-02/r62419`; root `version.txt` reports the older `Beta v2.1.0`; locale `patch_version.json` contains `6408660` with undocumented semantics. Steam build ID is reliable as a Steam build identifier only; no API mapping to it was observed.

## Install targets and public instructions

Winds4UA's public manual instructions say to unpack the selected archive into the game directory and replace files, then launch the game and set its language to English. The Steam target is:

`Package/HD/oversea/locale/`

The public instructions document this Official-launcher target:

`LocalData/Patch/HD/oversea/locale/`

The page also instructs users to check that `translate_words_map_en` was replaced (for example by its size). It mentions `install_windows.bat` as installing the main and diff files using the public API and names LBK Launcher integration. The script was not run or used to mutate the game.

The current local Steam target contains 49 files. The two current package files are present, but their provenance is unknown:

| Relative path | Local size | Local SHA-256 | Evidence |
|---|---:|---|---|
| `Package/HD/oversea/locale/translate_words_map_en` | 51,720,778 | `573000ace8fad4a1cd5a2d1f81015ce14fee10b0f30aae0c94f34eb39bcd9a4c` | differs from the v2.6.2 package entry |
| `Package/HD/oversea/locale/translate_words_map_en_diff` | 11,838,700 | `f37ffe0fb7eba0fe2f3b77aecd7161529ec9d5ac8e801e3fca32e60d87ee27e2` | differs from the v2.6.2 package entry |

**ORIGINAL STOCK BASELINE NOT PROVEN.** These files could be game originals, prior localization output, or another build/mode. Owner has approved restore semantics as **pre-Hub state**, not guaranteed Steam stock bytes. Before the first Hub mutation of each target, record whether it existed, its exact previous bytes, size and SHA-256. Rollback/restore returns those exact bytes for pre-existing files and deletes files that did not exist before Hub management. User-facing UI must not say “Відновити оригінал” unless stock state can be proven; prefer **“Відновити попередній стан”**. This contract does not require proving current local bytes are stock.

## Current production API and mode contract

Canonical latest endpoint: `GET https://winds4ua.com.ua/api/public/v1/releases/latest`. Public documentation: [Winds4UA releases API](https://winds4ua.com.ua/docs/public-api/releases). The aggregate `GET https://winds4ua.com.ua/api/public/v1/releases` was also checked read-only and agrees with `/latest` for the current release and default package.

On 2026-10-08 both endpoints returned HTTP 200 JSON with `Cache-Control: public, max-age=300` and weak ETags. Aggregate ETag was `W/"376569417a65fbd65233ea1b4f8ccd26549a22c4192996670b325b9f3a1b42d2"`; latest ETag was `W/"5bf0d4458ba7cb8ea2e6ed9389c32ee5c5657d1b3cdd16bc4de2173b2960364f"`. A latest GET with the returned weak ETag in `If-None-Match` returned HTTP 200 with the same ETag; a valid 200 remains usable fresh metadata and this behavior is not a Hub blocker. `Last-Modified` was not observed.

Current release observed: version **`v2.6.2`** (string), published `2026-10-08T04:26:53+03:00`, `status=current`, `game_version=null`, `game_tested=true`, `translated_rows=179444`. The response contains `success`, `generated_at`, `data.current`, `data.history`, and `data.modes`. `data.history` had five entries: v2.6.2, v2.6.1, v2.6, v2.5, v2.4. History is informational; it is not an install source.

`data.modes[]` is dynamic presentation metadata; installability must be taken from `available` and the selected mode's current package metadata. Match a current file to its mode using `slug` plus `variant`; do not infer package identity from display label or top-level current URL. Unknown future modes and fields must not break parsing. `available=false` is not installable and currently carries empty URL plus null size/hash. Do not infer that unavailable modes are equivalent to another mode.

| Stable mode key / variant | Available | Current package metadata |
|---|---:|---|
| `ukrainian` / `default` | true | `https://winds4ua.com.ua/releases/v2.6.2/where_winds_meet-v2.6.2.zip`; 63,507,986 bytes; SHA-256 `9e808faaae64ca00b68903fd5969a2153208f13c1d6833057d5f7f7f0af11881` |
| `english-items` / `english_items` | false | URL empty; size/hash null; no current installable file |
| `english-terms` / `english_terms` | false | URL empty; size/hash null; no current installable file |

The download page now presents v2.6.2 consistently with the API. It documents the Steam locale path and an Official-launcher path, but only the Steam installation was locally verified. The page's general extraction wording should not be implemented as blind extraction into the locale folder: the package paths are game-root-relative under `Package/`.

The site helper `install_windows.bat` appears to reflect an older flow that can handle `_diff` as a separate archive, whereas the current v2.6.2 ZIP already contains both target files. This is recorded as external helper/documentation drift only. Hub must use the current API/package contract and must not execute or depend on the helper.

Historical note: a 2026-10-07 API snapshot observed v2.9 and three available mode artifacts, including identical extracted file bytes for the two English modes. The current authoritative API instead marks both English modes unavailable and supplies no current package for them. The old observation is not evidence of current installability, does not imply interchangeability, and is **not a Stage 10 blocker**.

## v2.6.2 archive manifest and comparison

The Ukrainian ZIP download matched the API-advertised byte count and SHA-256: actual size `63,507,986`; SHA-256 `9e808faaae64ca00b68903fd5969a2153208f13c1d6833057d5f7f7f0af11881`. It contains exactly two regular entries, no wrapper release directory, absolute paths, `..` traversal or symlink-like entries. The `Package/` root is directly relative to the Steam game root:

| Archive path | Size | Entry SHA-256 |
|---|---:|---|
| `Package/HD/oversea/locale/translate_words_map_en` | 51,708,288 | `89c0551cf4e55333cd23d524de81ca6bf8509e6af7f34adc29e750298c17ba77` |
| `Package/HD/oversea/locale/translate_words_map_en_diff` | 11,838,723 | `f6924b1983ba93e2c17e199dd202b97c92c192b7e76920a4bbdde8bab5a3f69a` |

Both target paths exist in the observed local Steam installation, and both differ from the v2.6.2 package hashes. The package would replace both files on this installation. The local bytes' stock/original provenance remains unproven; see the approved pre-Hub restore contract above. On other supported builds a target may be absent, so installation must support both replace and create semantics.

## Backup, rollback and restore-state requirements

The safe backup unit is a **manifest of individual target files plus their prior existence state**, not a blind directory copy. Before mutation record for each target: relative path, existed/missing, size, locally computed SHA-256, and recoverable bytes when it existed. A transaction journal must support partial application.

- Rollback after a failed install restores bytes of every pre-existing target and deletes every target that was absent before the transaction.
- Restore points must preserve the same existence metadata so mode changes and updates can be reversed safely.
- Installation state is committed only after all files are extracted, verified and applied; partial state is corruption/failure, not success.
- The API supplies no observed original files/source hash. Therefore restore must mean returning to exact **pre-Hub state**, not game stock. Do not label it “original”. Owner has approved this semantic contract; implementation must preserve per-file existence and bytes before the first Hub mutation.

Existing Hub `BackupStore`, install/restore services, state resolver, file fingerprint/monitor and rollback path are single-file BDO implementations centered on `languagedata_en.loc` and BDO `public_id`/patch semantics. They cannot represent a two-file install with absent-file rollback without a bounded multi-file transaction/state model. Stage 10 is **HIGH-RISK FILE MUTATION / PRE-COMMIT ARCHITECT REVIEW REQUIRED**.

## Installed identity and state recommendation

No BDO-style immutable `public_id` was observed. Do not use display label or version alone. Candidate exact artifact identity should include:

`(Owner-approved game id, mode slug, version string, selected archive SHA-256, selected archive size)`

Persist a verified per-target manifest containing each relative path, expected size and SHA-256, plus the transaction's pre-state existence/backup references. Resolve state as follows:

- exact selected mode + release identity and every target file matches expected hash/size → exact current mode installed;
- same version text but different mode slug or archive hash → not the exact target (mode change/update decision, not “installed”);
- current selected mode artifact hash differs from installed artifact → update candidate, after validating local files;
- missing or modified target file against installed manifest → corrupted/partially modified;
- no trustworthy installation manifest → unknown, never infer installed from file names alone.

This is a recommendation only; no storage schema is changed. Stable game ID `where-winds-meet` is **OWNER APPROVED** for future use; it is not registered in production by this Stage 9 analysis.

## Game version and compatibility limits

The current API reports `game_version=null` and `game_tested=true`. Steam supplies build ID `25281753`, but no semantic mapping or supported-build range connects it to localization release `v2.6.2`. EXE version resources are empty; `Built.version`, `version.txt`, and `patch_version.json` have no documented compatibility mapping. Thus:

1. Hub can identify/display a Steam build ID, but not a validated semantic game version from the inspected evidence.
2. The current API cannot assert compatibility with that local Steam build.
3. A hard compatibility gate cannot be safely implemented from these values.

Stage 10 must not claim compatibility or invent build ranges. Owner-approved interim behavior: the Hub may detect/display the local Steam build, label compatibility as **not confirmed by API**, and require explicit neutral user confirmation before install; per-file pre-Hub backup and rollback remain mandatory. If a future API provides authoritative build compatibility, integrate it only in a later bounded task. No external project was accessed or changed for this analysis.

## Existing Hub architecture fit

| Concern | Existing reusable component | BDO-specific assumption | WWM requirement | Stage 10 action |
|---|---|---|---|---|
| Game catalog | `GameCatalog`, `GameDefinition` pattern | production catalog explicitly creates BDO only | Steam-first WWM descriptor, Owner-approved ID `where-winds-meet` | register only under separately authorized Stage 10 |
| Detection | `GameDetector`, Steam library/appmanifest traversal | AppID 582660, BDO registry and `ads/languagedata_en.loc` | AppID 3564740 plus WWM EXE/locale markers | add WWM-specific markers; never reuse BDO paths |
| Session lifecycle | `SelectedGameSessionHost` lifecycle | concrete `BdoGameSession` composition | WWM-specific API/install/persistence composition | extend minimally after contracts, test A→B→A isolation |
| Persistence paths | `GamePersistencePaths` | current BDO data schemas | isolated path under confirmed stable game ID | reuse path scoping; separate incompatible state schema as needed |
| API/feed | `BdoUaApiClient` / BDO DTOs | numeric version, `public_id`, patch compatibility | string version, `files[]`, modes, no public ID | dedicated small WWM DTO/client; keep aggregate feed dynamic |
| Feed cache | `ReleaseFeedCache` | BDO current/public ID schema v1 | WWM feed identity/mode metadata | do not deserialize WWM into BDO schema; add scoped validated cache only if needed |
| Mode model/cards | `LocalizationMode`, mode card/policy | BDO `current` and `public_id` semantics | slug + variant + availability + archive metadata | map only proven fields; unknown modes remain dynamic |
| Release identity | BDO install metadata/state | public ID and numeric version | mode + string version + archive hash/size | persist immutable artifact identity and per-file installed manifest |
| Compatibility | BDO `compatible_with_official_patch` | numeric official patch comparison | no supported-build mapping | explicit unknown/unverified policy; no fabricated comparison |
| Download verification | `LocalizationInstaller` HTTPS/size/SHA | one direct file | ZIP archive plus extracted files | verify outer ZIP, safe entries, per-file manifest before mutation |
| Archive handling | existing update ZIP code is app self-update, not game installer | not applicable | path-safe locale-target extraction only | implement dedicated bounded WWM archive validation; never execute archive content |
| Install transaction | `LocalizationInstallService` | one `languagedata_en.loc` target | two-file coordinated transaction | high-risk journaled multi-file transaction with rollback |
| Backup | `BackupStore` | one file/original snapshot | file bytes + prior existence per target | extend/introduce WWM-specific manifest backup after schema review |
| Restore state | `RestoreOriginalService` | BDO official source and patch-matched snapshot | exact per-file pre-Hub state; not guaranteed stock | implement truthful “restore previous state” semantics; no “original” claim |
| Installed state | `LocalizationStateService` | one-file fingerprint + public ID | per-file manifest/hash and exact archive identity | add WWM resolver; unknown/corrupt states fail closed |
| File monitor | BDO fingerprint/monitor | one target file | monitor two target files and partial drift | scope monitor to WWM manifest and detect partial mutation |
| UI cards | existing target/mode cards | BDO mode policy | server mode labels, availability, test status | reuse presentation pattern, avoid claiming compatibility |
| Notifications | existing session-scoped notifications | BDO release identity | WWM version/mode/artifact identity | scope dedup keys by stable game ID + mode + archive identity |

## Stage 9 result and Stage 10 gate

Stage 9 — **REVIEWED / ACCEPTED** by external Architect: BLOCKER `0`, IMPORTANT `0`, OPTIONAL `0`. The accepted evidence covers Steam discovery, current production API and mode availability, the v2.6.2 archive, two-file mapping, pre-Hub restore, and compatibility limits. No runtime code changed.

Stage 10 remains **NOT STARTED** and requires a separate bounded implementation task. The old identical-payload v2.9 issue is not a blocker: the current API marks both English modes unavailable and supplies no current artifacts for them. The Owner-approved stable ID remains `where-winds-meet`, scope is Steam-first, and Official launcher is deferred. The current API's `game_tested=true` does not assert compatibility with a particular Steam build; compatibility remains unconfirmed and requires neutral explicit user confirmation before mutation.

Any Stage 10 multi-file installation is **HIGH-RISK FILE MUTATION / PRE-COMMIT ARCHITECT REVIEW REQUIRED**. Before the first Hub mutation, preserve each managed target's relative path, prior existence, bytes when present, size and SHA-256. Restore means exact pre-Hub state: restore existing files and remove Hub-created files that were absent before. Use **“Відновити попередній стан”**, not “Відновити оригінал”, unless an authoritative stock source contract is added later. Production GameCatalog remains BDO-only until Stage 10 is separately authorized and implemented.
