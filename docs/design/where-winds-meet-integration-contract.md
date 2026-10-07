# Where Winds Meet / Winds4UA — Stage 9 integration contract

Status: **STAGE 9 — ANALYZED / VALIDATED / PENDING EXTERNAL ARCHITECT REVIEW**
Evidence checked: **2026-10-07**
Scope: read-only analysis; no game or production runtime changes.

Owner decisions recorded: stable game ID `where-winds-meet`; initial implementation scope is Steam-first; restore means return to the exact pre-Hub state, not guaranteed stock bytes. Stage 10 remains unauthorized.

## Evidence boundary

- **Steam — LOCALLY VERIFIED:** Steam AppID `3564740`, manifest association, installed root markers, executable, and Steam locale directory were inspected read-only. The local Steam manifest reports build ID `25281753`.
- **Official launcher — DOCUMENTED / NOT LOCALLY VERIFIED / DEFERRED:** Winds4UA documents its target directory, but this machine's installation does not establish that launcher layout or its detection markers. It is outside the initial Steam-first scope and needs separate Owner authorization/verification.
- Public facts below were checked against the [Winds4UA download instructions](https://winds4ua.com.ua/download), its [public releases API documentation](https://winds4ua.com.ua/docs/public-api/releases), and read-only requests to the documented API.
- Archive contents were downloaded to an isolated temporary analysis directory, hash-checked, listed/extracted there, and compared read-only with the local Steam locale directory. No archive content was copied to the game.

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

The current local Steam target contains 49 files. The two archive-overlapping files are present, but their provenance is unknown:

| Relative path | Local size | Local SHA-256 | Evidence |
|---|---:|---|---|
| `Package/HD/oversea/locale/translate_words_map_en` | 51,720,778 | `573000ace8fad4a1cd5a2d1f81015ce14fee10b0f30aae0c94f34eb39bcd9a4c` | differs from all three v2.9 archive variants |
| `Package/HD/oversea/locale/translate_words_map_en_diff` | 11,838,700 | `f37ffe0fb7eba0fe2f3b77aecd7161529ec9d5ac8e801e3fca32e60d87ee27e2` | differs from all three v2.9 archive variants |

**ORIGINAL STOCK BASELINE NOT PROVEN.** These files could be game originals, prior localization output, or another build/mode. Owner has approved restore semantics as **pre-Hub state**, not guaranteed Steam stock bytes. Before the first Hub mutation of each target, record whether it existed, its exact previous bytes, size and SHA-256. Rollback/restore returns those exact bytes for pre-existing files and deletes files that did not exist before Hub management. User-facing UI must not say “Відновити оригінал” unless stock state can be proven; prefer **“Відновити попередній стан”**. This contract does not require proving current local bytes are stock.

## Public API observations

The documented aggregate endpoint is:

`GET https://wwm-translate-ua.eremenko.dev/api/public/v1/releases`

On 2026-10-07 it returned HTTP 200, JSON (`application/json`), `Cache-Control: public, max-age=300`, weak ETag, `X-Robots-Tag: noindex`, and rate-limit headers (60 requests/minute). No `Last-Modified` was observed. The response had `success`, `generated_at`, and `data.current`, `data.history`, `data.modes`.

The public docs list aggregate `/releases`, short `/releases/latest`, `/download/latest`, and `/download/latest/{mode}`. No per-mode latest metadata endpoint was documented or observed. The mode-specific latest download route is a redirect, not a metadata contract. No dedicated public health/version endpoint was found among documented linked resources; `/status` is a translation statistics page.

Observed current release:

- version is a **string** (`v2.9`), published `2026-10-04T06:21:25+03:00`, status `current`;
- `game_version` is null and `game_tested` is false; `translated_rows=178793`;
- `data.history` contained v2.9, v2.8, v2.6, v2.5 and v2.4; older historical records observed had status `archived` and may have fewer modes. Public documentation says history is informational and not installable;
- `data.current.files[]` describes release files; `data.modes[]` describes selectable mode metadata. Current records align by `slug` and `variant`; mode records include label, description, audience, availability and file metadata. Top-level current download/size/hash match the default `ukrainian` file. Use the matching current file entry for a selected mode, not a display label or history entry.
- `available=false` means the mode has no downloadable file. Documentation states `current=null` is a valid state and modes may remain listed but unavailable. A current release can expose fewer files than modes; unknown future modes/fields must be tolerated dynamically.

Current observed mode/file mapping:

| Stable mode key / variant | Label | Description | Audience | Available | Download URL | Size / SHA-256 |
|---|---|---|---|---:|---|---|
| `ukrainian` / `default` | Українська | Усе українською: інтерфейс, діалоги, назви предметів, навичок і місць. | Для більшості гравців. | true | `https://wwm-translate-ua.eremenko.dev/releases/v2.9/where_winds_meet-v2.9.zip` | 63,762,357 / `bcc8bb3c912165ccb7b80d14257f1c2e56d194fb7c7c7f05c8f9fc30b19b6783` |
| `english-items` / `english_items` | Українська (Предмети англійською) | Українські тексти, але назви предметів лишаються англійськими, як у грі без перекладу. | Якщо шукаєте предмети в англомовних гайдах чи в розмові з іноземними гравцями. | true | `https://wwm-translate-ua.eremenko.dev/releases/v2.9/where_winds_meet-v2.9_english-items.zip` | 63,762,297 / `468a2753f24aa8dc8bf16125ec6bb1e0dc781b4ff14264765c867871711b7b71` |
| `english-terms` / `english_terms` | Українська (Всі терміни англійською) | Українські тексти, але назви предметів, навичок, місць, титулів і товарів крамниці лишаються англійськими. | Якщо граєте за англомовними гайдами й вікі або звикли до англійських термінів. | true | `https://wwm-translate-ua.eremenko.dev/releases/v2.9/where_winds_meet-v2.9_english-terms.zip` | 63,762,297 / `27d3cc5d49fab54316546afe611e5c89ee3dfbeb248a9ea021556f36eed36209` |

The English-items and English-terms v2.9 archives had identical bytes for both contained files despite distinct mode identity/labels. Preserve the server's mode identity; do not infer that the modes are interchangeable.

The public download page displayed Beta v2.6.2 while the API returned v2.9 during this observation. This is a stale public presentation, but it does not block technical Hub integration. Exact weak-ETag revalidation returned HTTP 200 instead of the documented 304; this is also a non-blocking external observation because valid HTTP 200 metadata remains usable. Neither issue is to be fixed in the Hub repository.

**External mode contract decision required before Stage 10:** the `english-items` and `english-terms` artifacts have different outer URLs/SHA-256 but identical extracted file bytes. Do not assume this is intentional or a generator defect. Before Stage 10, Owner/Winds4UA must confirm one of: (1) equivalence is intentional for this release; (2) one or both artifacts are incorrect and will be corrected; or (3) Hub should temporarily expose only one mode. Both modes remain documented; do not remove either by assumption.

## v2.9 archive manifest and comparison

All three archives matched the API-advertised byte count and SHA-256. Each contained exactly two regular files, with the same safe relative path set; paths had no absolute prefix, traversal segment, unexpected root, or symlink-like entry:

1. `Package/HD/oversea/locale/translate_words_map_en`
2. `Package/HD/oversea/locale/translate_words_map_en_diff`

| Mode | Entry | Size | Entry SHA-256 |
|---|---|---:|---|
| `ukrainian` | `translate_words_map_en` | 51,921,915 | `373bc94ce777a3341495381611a3c4dd09ec9f3299a4770d8e463c19899cf2ca` |
| `ukrainian` | `translate_words_map_en_diff` | 11,886,369 | `0bd6ae6f5f187c80547347a9abb0e84f7c1ab09fad062afd7d925e78f689d827` |
| `english-items` | `translate_words_map_en` | 51,921,918 | `fa2f907ddb23d7d126b5a9a3df543f2a93da06d7c47b5c7da8d5ff35ec9887fc` |
| `english-items` | `translate_words_map_en_diff` | 11,886,369 | `0bd6ae6f5f187c80547347a9abb0e84f7c1ab09fad062afd7d925e78f689d827` |
| `english-terms` | `translate_words_map_en` | 51,921,918 | `fa2f907ddb23d7d126b5a9a3df543f2a93da06d7c47b5c7da8d5ff35ec9887fc` |
| `english-terms` | `translate_words_map_en_diff` | 11,886,369 | `0bd6ae6f5f187c80547347a9abb0e84f7c1ab09fad062afd7d925e78f689d827` |

Both target paths currently exist locally, but neither local size/hash matches any v2.9 entry. Relative to this observed installation, the release would overwrite both files. On a different supported build where either path is absent, it would add that file. Therefore the operation is not safely modeled as overwrite-only.

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

The API currently reports `game_version=null` and `game_tested=false`. Steam supplies build ID `25281753`, but no semantic mapping or supported-build range connects it to a localization release. EXE version resources are empty; `Built.version`, `version.txt`, and `patch_version.json` have no documented compatibility mapping. Thus:

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

Stage 9 established the evidence-based Steam, feed, mode, archive, mutation and restore contract and is **ANALYZED / VALIDATED / PENDING EXTERNAL ARCHITECT REVIEW**. Owner decisions settle the stable ID, Steam-first scope, pre-Hub restore semantics and neutral confirmation for unconfirmed compatibility. No runtime code changed.

Stage 10 remains **NOT STARTED / NOT AUTHORIZED / BLOCKED ON EXTERNAL MODE CONTRACT DECISION**. Before it can start, Owner/Winds4UA must explicitly resolve the identical extracted payloads for `english-items` and `english-terms` by confirming equivalence, correcting the artifacts, or authorizing Hub to expose one mode temporarily. Do not guess or remove either mode before that decision. The stale download-page version and weak-ETag 200 behavior are recorded non-blocking external observations; neither is a Hub blocker. Official launcher remains deferred pending separate Owner authorization/verification.

Any Stage 10 multi-file installation must preserve pre-Hub existence/bytes/size/hash per target and rollback partial writes, and requires **HIGH-RISK FILE MUTATION / PRE-COMMIT ARCHITECT REVIEW REQUIRED**. Production GameCatalog remains BDO-only until Stage 10 is separately authorized and implemented.
