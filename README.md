# Runtime Translator

Authors: Gesvd & Codex.

Runtime Translator is a RimWorld internal mod prototype that translates loaded mods from an in-game settings panel.

It is intentionally idle at startup: it loads settings and cache metadata only. Network connections and text scanning start after the player presses **Start translation**.

## Current Scope

- In-game mod settings window.
- Select loaded mods for translation.
- Choose source and target language.
- Choose provider:
  - OpenAI-compatible chat/completions API.
  - LibreTranslate-compatible HTTP API.
  - Lara Translate SDK bridge.
- Start, pause, resume, and cancel background translation.
- Cache translated entries under RimWorld's config folder.
- Apply common `Def.label` and `Def.description` translations at runtime.
- Export translated entries as standard RimWorld `Languages/<Target>/DefInjected/...` XML files.

## Use In Game

1. Build the DLL into `Assemblies/RuntimeTranslator.dll`.
2. Place this folder under RimWorld's `Mods` directory.
3. Enable **Runtime Translator** in RimWorld's mod list.
4. Start RimWorld normally. The mod stays idle during startup.
5. Open **Options > Mod Settings > Runtime Translator**.
6. Choose provider settings, target language, and the loaded mods to translate.
7. Press **Start**. Use **Pause**, **Resume**, or **Cancel** as needed.

Each task keeps the language, provider, glossary, skip-completed choice, and apply/export settings it had when **Start** was pressed. Changes to those settings affect the next task. **Batch size** is live: the next batch uses the current value, including after resuming a paused task; an already-sent request keeps its original size. Manual preview edits use the current apply setting.

**Skip completed translations** is enabled by default. It excludes valid completed entries from API requests and from the pending task total, while applying cached results to the game. The status shows the skipped count separately. Valid cached results can be reused across providers; the selected provider is preferred, otherwise the latest available record is used. Cleared preferred records, changed source text, and changed applicable glossary rules still need translation. Turning this option off retranslates all selected entries and can overwrite earlier automatic or manual translations.

Runtime translations continue to apply on the main thread after the settings window is closed, up to 20 entries per frame. Original field text is retained for this game session, so restarting a task does not translate an earlier translation. Clearing a preview translation restores the original field text and allows that entry to be translated again.

Cache saves first write a temporary file, then replace `cache.xml` while retaining the previous version as `cache.xml.bak`. A damaged or missing primary cache can load the backup. Save failures appear in the status/log and preserve the previous cache file.

Exports include only the chosen target language. For each Mod/Def/field, the most recently updated cached version wins across providers and source versions; a cleared latest version suppresses older translations. Export overwrites its own `RuntimeTranslator.xml` files and removes stale files of that name under the selected language's generated `DefInjected` folders. Put hand-maintained translations in separate files or folders.

Provider responses with missing, repeated, or unknown protected placeholders are rejected rather than cached. Previously saved translations are not automatically repaired.

Cancelled tasks finish with `Cancelled`, including cancellation before a request starts or while paused. Unexpected task errors finish with `Failed` and retain the error message. Results already completed before cancellation remain in the cache.

Preview's **Replace next 50** processes each matching record once per operation, even when the replacement contains the search text. The remaining count excludes processed records. Changing the search/replace fields or starting a translation task begins a new replacement operation; manually editing a record allows that record to be replaced again.

## Translating This Mod

Runtime Translator now appears in its own **Mods** list. Select it and start a translation task to translate the settings interface. The English UI table is in `Languages/English/Keyed/RuntimeTranslator.xml`; its 88 entries support runtime editing/caching and standard `Keyed` export. Its source table remains English even after the interface is translated. Game/provider IDs and diagnostic log messages are not all localized.

## Interrupting Work On Exit

The mod hooks Unity's early quit request, application quit, and updater destruction. A shared cancellation signal stops main translation requests, glossary translation, glossary file scanning, and automatic export. Queued UI actions and runtime writes are discarded once shutdown starts. Shutdown never joins worker tasks or waits for the cache lock; auto-export uses a cache snapshot rather than holding that lock during file I/O.

Previously saved batches remain on disk, protected by temporary-file replacement and backup. A batch that has not yet been saved at the moment of exit may need to be translated again. This mechanism has regression coverage with active requests and a busy cache lock; actual RimWorld/OS shutdown still needs an in-game check.

## Glossary

1. Choose mods on **Mods** and the source/target languages on **Task**.
2. Open **Glossary** and press **Scan selected mods**. Discovery runs in the background without API calls or changes to mod files.
3. Review candidates by occurrence count, search them, and enter preferred translations. Words occurring at least twice are added; blank translations remain inactive. Existing choices survive rescans.
4. Press **Translate missing glossary terms (current provider)** to translate all blank terms for the current language pair, including those outside the current search/page. It uses the configured provider and the live batch size. Already populated terms are preserved, including manual corrections and the built-in pawn defaults. Each successful batch is saved; failed terms remain blank and retryable. A separate cancel button stops this job. Review the generated wording before starting the main translation task.
5. Use **New word / phrase** to add terms not discovered by the scan. Existing rows can be edited or removed. Results are paged at 30 rows to keep the editor responsive.
6. Start a translation task. The glossary is frozen with the task's settings. Edited terms cause matching source texts to be translated again on the next task; unrelated cached translations remain usable.

English-to-Simplified-Chinese starts with `pawn` and `pawns` mapped to `小人`. Both entries can be changed, for example to `角色`. Other language pairs have independent terms. English source aliases and Auto use the English glossary. Automatic discovery currently supports English; other languages can use manually entered terms.

Discovery reads common text fields in the active mod version's `Defs` XML and leaf text in `Languages/English` XML. It excludes English stop words, formatting tokens, IDs, and non-English language files. Malformed or unreadable XML files are skipped with a log warning. Counts are for the selected files, not an exhaustive linguistic analysis; singular/plural forms are separate editable terms.

All three providers protect confirmed terms as placeholders and restore the exact preferred wording. Matching ignores case, uses whole-word boundaries, and prioritizes longer phrases. Format tags and variables remain protected. A provider that damages these placeholders fails the batch rather than storing inconsistent terms. A word with several meanings uses the same preferred wording throughout the language pair; clear or remove a term if that is unsuitable.

Terms are saved in RimWorld mod settings, including on scan completion, adding a term, or each automatic glossary translation batch; inline edits are saved when the settings window closes. The original mod files remain unchanged. Previously generated files are refreshed by exporting again after the new translation task completes.

## Build

Install a .NET SDK that can target .NET Framework 4.7.2, then build with a RimWorld managed assembly path:

```powershell
dotnet build .\RuntimeTranslator.csproj -p:RimWorldManagedDir="C:\Program Files (x86)\Steam\steamapps\common\RimWorld\RimWorldWin64_Data\Managed"
```

The DLL is emitted to `Assemblies/RuntimeTranslator.dll`.

## Regression Checks

Run with PowerShell 7:

```powershell
pwsh -NoProfile -File .\Tests\Run.ps1
```

These checks compile the real translation/cache/provider code with small game API stand-ins. They use temporary files and no network requests. They do not replace an in-game test of Unity's update lifecycle or live provider compatibility.

## Provider Notes

The OpenAI-compatible provider accepts services that expose `/chat/completions`, including many hosted APIs and local tools such as LM Studio.

Its prompt sends a JSON array of `{ "id": "...", "text": "..." }` items and requests the same structure back with translated text. JSON strings preserve paragraph breaks, quotes, and other special characters. Missing, duplicate, unknown, empty, or malformed results fail the batch rather than being partly cached. A surrounding JSON code fence is accepted. Custom rules must preserve this response format; the earlier `ID|||translation` line format is no longer accepted.

The LibreTranslate provider accepts `/translate` endpoints.

The Lara provider follows Lara's official SDK model through a local bridge service. Lara's docs recommend SDK usage and use Access Key ID + Access Key Secret credentials; the low-level REST API is not public by default. RimWorld mods target .NET Framework, while Lara's .NET SDK targets modern .NET, so the mod sends requests to a local bridge URL such as `http://127.0.0.1:8765/translate`.

Expected bridge request:

```json
{
  "text": "text to translate",
  "source": "en",
  "target": "zh-CN",
  "content_type": "text",
  "instructions": "preserve placeholders",
  "access_key_id": "your Lara access key id",
  "access_key_secret": "your Lara access key secret"
}
```

Expected bridge response:

```json
{
  "translatedText": "translated text"
}
```

API keys are stored in RimWorld mod settings. For public releases, prefer a local reverse proxy or a service-specific token with low quota.

## First Prototype Limits

This first version focuses on common loaded `Def` fields such as `label`, `description`, `jobString`, `deathMessage`, `gerund`, and `reportString`.

Apart from its own keyed settings interface, it does not yet patch every other mod's `Keyed` lookup or code-hardcoded UI string. Those need a second pass with targeted Harmony patches after the basic translation queue and cache are proven stable in game.

## Bundled offline translation

Windows x64 users can select LibreTranslate and enable **Use bundled offline engine (Windows, English to Chinese)**. The included engine runs independently on the local machine, uses the existing glossary and requires no API key or internet at runtime. Only English → ChineseSimplified is bundled. See [LocalEngine/README.md](LocalEngine/README.md) for packaging, rebuilding, logs, and [license/source obligations](LocalEngine/THIRD_PARTY_NOTICES.md).
