# Gesvd’s RimLingo: a runtime translator for RimWorld — v1.4.0

![Gesvd’s RimLingo cover](About/Preview.png)

Authors: Gesvd & Codex.

作者：Gesvd & Codex。以下为中文使用说明，后面保留英文说明和接口细节。

## 中文使用说明

当前版本：**v1.4.0**。更新内容见 [CHANGELOG.md](CHANGELOG.md)。

**Gesvd的边缘翻译**（英文名 Gesvd’s RimLingo）是一个在 RimWorld 游戏内运行的模组翻译器。玩家可以选择已加载的模组，通过内置离线引擎或自己配置的翻译 API 补充翻译，并用词汇库统一术语、手动修改结果。它不会在游戏启动时自动联网或开始翻译任务。

模组包 ID 为 `gesvd.rimlingo`。更新后会自动迁移旧版的自身选择记录和自身翻译缓存；API 设置、词汇库及其他模组的翻译缓存继续保留。请在原模组文件夹中更新，以继续使用原设置文件。旧存档或模组列表可能提示包 ID 变化，在模组管理中重新启用本模组即可；依赖本模组的第三方配置也应更新引用。

### 语言扩展包

扩展包位于本项目根目录的 **`LanguagePacks/`**，按 `ChineseSimplified`、`English`、`Japanese`、`Korean`、`Russian`、`German`、`French`、`Spanish`、`Portuguese` 分文件夹。每个模型扩展附本语言的 `README.md`、中文 `README.zh-CN.md`、模型清单和许可。`English` 是中转与基础功能说明，无需独立英语模型。

语言包现在是**独立扩展模组**。玩家在 Steam 创意工坊订阅后，在游戏模组列表同时启用 **RimLingo 基础模组**和所需语言扩展即可。手动安装时，像普通模组一样把 ZIP 解压到游戏的 **`Mods` 目录**，形成 `Mods/RimLingo-Language-Japanese/About/About.xml` 这样的结构，然后在模组列表启用。基础模组排在扩展之前；依赖和顺序已在扩展的 `About.xml` 中声明。无需向基础模组复制模型文件，也无需配置文件路径。语言包需配合本版 v1.4.0 基础模组使用。

语言下拉框在离线模式只显示已启用扩展所提供且有翻译路径的选项。日语→简体中文需要日语包和基础包；中文→日语还需要中文反向包。基础包无需重新下载每一种语言模型，也不会在启动或翻译时自动联网安装。启用或禁用语言扩展后按游戏提示重启。已禁用扩展不会加载。繁体中文和源语言“自动识别”可用于相应在线服务；本批离线包不包含繁体模型，也不包含自动语种识别器。中转翻译会增加处理时间，译法可能不如直接翻译自然；同时使用更多模型也会增加内存占用。

### DeepSeek 与 Claude

在翻译服务下拉菜单选择 **DeepSeek** 或 **Claude**，分别填写自己的 API 密钥。两者的地址、模型和规则独立保存，切换不会覆盖 OpenAI 兼容接口配置。默认地址分别为 `https://api.deepseek.com` 和 `https://api.anthropic.com/v1`；默认模型分别为 `deepseek-flash` 和 `claude-haiku-5-5`，可改成账号实际可用的模型 ID。订阅聊天产品并不等于已有 API 额度。

DeepSeek 复用 OpenAI 兼容的 Chat Completions 协议；Claude 使用原生 Messages API 和 `x-api-key`/`anthropic-version` 认证。两者沿用术语保护和逐条 JSON 校验。Claude 每次请求最多允许输出 8192 tokens，触及输出上限时保留旧译文并报告失败，可降低单次翻译数后重试。服务可用模型及计费以官方为准：[DeepSeek 文档](https://api-docs.deepseek.com/)、[Claude 文档](https://platform.claude.com/docs/en/api/messages/create)。

### 安装与首次使用

1. 下载完整安装包 **`Gesvds-RimLingo-v1.4.0-LibreTranslate-win64.zip`**。GitHub 的 **Code → Download ZIP** 是源码包，不含大型离线运行文件；普通玩家应使用完整安装包，无需自行编译 DLL。
2. 手动安装时，将解压出来的 **`RimLingo` 文件夹**放入 RimWorld 的 `Mods` 目录。进入该文件夹后，应直接看到 `About`、`Assemblies`、`Languages` 和 `LocalEngine`，不要再多套一层目录。
3. 在游戏模组列表中启用 **Gesvd’s RimLingo: a runtime translator for RimWorld**，按游戏提示重新加载。
4. 打开 **选项 → 模组设置 → Gesvd的边缘翻译**。游戏使用简体中文时，会自动显示内置的官方中文界面，无需先启动机器翻译。界面分为任务、模组、翻译服务、译文预览、状态和词汇库。英文游戏界面中仍显示 Gesvd’s RimLingo。
5. 在模组页勾选要处理的模组，在任务页从下拉菜单选择源语言和目标语言，在服务页选择翻译方式，再点击开始。任务可以暂停、继续或取消。

单次最大翻译数支持滑动条和数值输入：滑动条范围为 **1–50**，手动输入可以超过 50（仅限正整数）。修改会从下一次请求起生效，正文与词汇库翻译均适用。过大的数值可能导致游戏卡顿，或超出翻译服务的请求限制；请按实际情况逐步增加。

发布到 Steam 创意工坊时，作者应上传完整包解压后的 `RimLingo` 模组目录，并保留许可及源码资料。正确上传完整目录后，订阅玩家无需再手动解压。

### 使用内置离线翻译

完整包已包含 LibreTranslate、独立 Python 运行时和英中模型。**玩家不需要另装 LibreTranslate、Python 或 Docker，也不需要 API Key，运行时可以断网。**

在 `Provider` 页选择 **LibreTranslate**，勾选 **Use bundled offline engine (Windows, installed language packs)**。源语言选择“英语”，目标语言选择“简体中文”。第一次发起翻译时会在后台加载模型，只监听本机回环地址；退出游戏时会停止引擎进程。

基础包支持 **Windows x64、英文 → 简体中文**；安装对应的可选扩展包后支持中、英、日、韩、俄、德、法、西班牙、葡萄牙语之间的翻译，非英语之间通常经英语中转。其他翻译服务可使用 OpenAI 兼容 API、DeepSeek、Claude、外部 LibreTranslate，或 Lara SDK 本地桥接。详细的离线包说明和日志位置见 [LocalEngine/README.md](LocalEngine/README.md)。

### 自带译文优先、DLC 排除与跳过选项

**已有的有效目标语言译文始终优先，翻译器只补缺失文本。** 对于支持的文本字段，作者自带译文以及游戏已加载的独立汉化模组译文，不会被机器翻译、旧缓存、预览手动替换或生成的导出条目覆盖。

RimWorld 本体和官方 DLC 不会出现在可选模组中，也不会进入翻译或词汇扫描；旧设置中残留的官方内容选择会被清理。

任务页默认开启 **Skip mods with bundled target-language translations (entire mod, even if partial)**，即“跳过自带目标语言译文的整个模组”。识别当前有效版本目录中的标准 Keyed/DefInjected XML；简体、繁体及其他目标语言分别处理。空白不算有效译文，中日韩俄语会检查对应文字。即使只有部分译文也会整模组跳过，需要补缺时请关闭该选项。

如果某个模组只有部分汉化，可以关闭这个选项来补齐缺失条目。**关闭整模组跳过，也不会覆盖已有译文。** 自带译文的模组仍能在模组页勾选，供词汇库导入使用。

另一个默认开启的选项 **Skip completed translations (reuse valid cache)** 用于复用已完成的有效缓存，减少重复请求。关闭后会重新翻译未受自带译文保护的条目，并可能替换已有自动缓存；受保护的人工译文仍会保留。

### 扫描、导入和维护词汇库

词汇库用于统一 `pawn`、武器、建筑等词语的译法。默认已有 `pawn` / `pawns` → “小人”，玩家可以修改。建议先整理词汇库，再开始正文翻译。

- **扫描源语言术语**：选好模组与语言，进入 `Glossary` 页，点击 **Scan selected mods (source-language candidates)**。英语扫描 Def 原文与英语语言文件；其他语言读取相应语言目录。过滤常见功能词与格式变量，把至少出现两次的词加入候选。中日文只提取 2～16 字的完整短片段，不把长句任意切成词；候选需要校对。空白译法不参与替换。
- **导入模组自带术语**：选好源语言与目标语言，点击 **Import glossary from bundled source/target translations**。此功能本地运行，不调用 API；按相同 XML 键配对两种语言，提取短词或短语。英语还能从 Def 原始标签配对；其他源语言需要模组已有对应语言文件。保留已有译法，忽略冲突。
- **翻译缺少译法的词条**：点击 **Translate missing glossary terms (current provider)**，使用当前选择的翻译服务处理空白词条。已有译法会保留。
- **手动维护**：搜索词条、修改译法，或使用 **Add / update term** 添加词和短语。清空译法表示停用该条规则，**Remove** 可以删除词条。

- **应用术语库到已有译文**：按当前语言组合处理已加载模组中涉及已启用术语的已有缓存，不受模组勾选或词汇库搜索分页限制。使用缓存中的原文和当前术语库，通过当前翻译服务重新翻译，可能消耗 API 额度；成功后替换该原文的旧自动缓存版本，受保护的人工译文会跳过；失败或取消时保留尚未成功条目的旧译文。可在任务页暂停、继续或取消。模组自带译文仍优先；已清空的译文、其他语言、不涉及术语的文本，以及原文已改变或模组未加载的缓存不处理。应用到游戏及导出文件遵循任务页对应的开关。手动修正过的匹配缓存也会参与，请先确认术语译法。

扫描会优先统计至少出现两次的连续英文短语（2–6 个有效词），较长短语优先。例如 `parasitic egg` 出现两次，则短语计数为 2，其中的 `parasitic` 和 `egg` 不再各加 2；独立出现的单词另行计数。标点、换行、介词连词及格式占位符会隔断短语。跨文件累计频次，重新扫描更新计数，但保留玩家已有的词条与译法。

自带术语导入只添加新词或填补空白，不覆盖默认词条或玩家已有译法；同一英文出现不同中文译法时不自动导入。简繁体不会混合。导入的短语最多六个英文单词、64 个字符，不从长句中猜测单个单词的对应关系，也不提取只有继承结果而无匹配源文件的标签、非 XML Strings 或缺少对应英文文件的独立汉化包。

词汇修改影响后续新任务；已开始的正文任务保持启动时的语言、服务和词汇规则。**Batch size (next request)** 是实时设置，修改后从下一批请求生效，已经发送的请求不会被拆分。

### 原文保留、缓存与导出

译文的标签、变量和格式占位符会被保护；缺失或重复占位符的响应会被拒绝。请求失败、结果缺失、纯空白、零宽字符或空格式标签不会作为正常译文覆盖游戏文本。明确的 API 过滤或拒译响应会记录为失败，保留已有文本。翻译器自身不进行内容消音，但无法保证外部服务不会拒译或返回非空的错误译文。

可以在 `Preview` 页查看和修改缓存译文；清空自动译文会恢复原始文本。已有自带译文仍受优先级保护。缓存位于 RimWorld 配置目录的 `RuntimeTranslator/cache.xml`，保存时保留备份，取消任务不会清空已经保存的结果。

开启导出后，会在配置目录的 `RuntimeTranslator/GeneratedLanguages/<目标语言>/` 下生成标准语言 XML。导出会排除与已有自带译文冲突的条目，并更新或清理自己生成的 `RuntimeTranslator.xml`；手动维护的文件应使用其他名称。生成目录不是自动启用的独立汉化模组，按需复制到合适的语言目录或自行整理为汉化模组。

退出游戏会中断请求和后台任务；已经保存的批次保留，退出前尚未保存的部分可能需要重新翻译。

### v1.4.0 性能、重试与人工译文保护

- 主翻译扫描按帧执行，扫描阶段也可以取消；游戏对象仍只在主线程读取，XML 与文本整理在后台进行。译文应用采用每帧约 2 ms 预算，单次游戏调用可能超时。
- 预览每页 50 条；搜索与缓存未变化时复用筛选结果。“替换接下来 50 条”仍针对全部搜索结果，而非仅当前页。
- 修改非空译文或批量替换后，自动标记为人工保护。点击 **Unlock translation / 解除译文保护** 后，才允许后续任务覆盖；仅解除保护不会自动发起翻译。清空译文会解除保护并恢复原文。旧版本缓存未记录人工身份，不能自动判断哪些旧记录曾由玩家修改，可在预览中手动保护。
- 条目数是批次上限，还会兼顾约 12,000 字符的请求预算；这不等于模型的精确 token 限额。超长单条不会被截断。临时网络错误、限流和部分服务器错误最多重试两次；已识别的上下文或输出超限会拆小批次。重试仍可能消耗额度。
- 任务页 **Retry failed entries (current provider)** 只处理上次任务中当前语言组合的失败条目，并重新核对原文和自带译文；不重跑已成功条目。失败清单仅保留在本次游戏进程内。重启后可通过普通任务的“跳过已完成译文”继续补齐。
- 缓存继续每批安全保存，并保留备份；独立快照使写盘不占用缓存数据锁，无改动时不重写。内置离线引擎新增批量接口。

### 当前范围与验证情况

目前处理已加载 Def 的 `label`、`description`、`jobString`、`deathMessage`、`gerund` 和 `reportString`，以及所选模组有效语言目录中的标准 Keyed 文本。通用 Keyed 应用会检查游戏实际回退原文，避免覆盖另一个模组同名键的不同内容。嵌套 Def 字段、列表、规则包、硬编码界面和动态生成文本仍未全面覆盖。

当前版本已通过编译和自动回归检查，但仍需在实际游戏及具体模组组合中验证。翻译质量需要玩家校对，内置离线引擎对被标签、术语分割的句子和英文缩写可能处理得不够自然。

### 我们的代码与第三方许可

我们编写的模组主体、测试及构建工具采用 **GPL-3.0-only**，C# 模组附有范围有限的 [RimWorld/Unity 链接例外](LICENSE-EXCEPTION.md)。**LibreTranslate 保持 AGPL-3.0，本地服务集成 `LocalEngine/serve.py` 采用 AGPL-3.0-only**；模型、Python 和其他依赖保留各自许可与署名。

分发时应保留许可证、署名和与二进制匹配的对应源码资料。具体范围见 [LICENSING.md](LICENSING.md)，第三方组件及模型来源见 [THIRD_PARTY_NOTICES.md](LocalEngine/THIRD_PARTY_NOTICES.md)。

---

## English Guide

Gesvd’s RimLingo is a RimWorld internal mod prototype that translates loaded mods from an in-game settings panel.

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
3. Enable **Gesvd’s RimLingo: a runtime translator for RimWorld** in RimWorld's mod list.
4. Start RimWorld normally. The mod stays idle during startup.
5. Open **Options > Mod Settings > Gesvd’s RimLingo**.
6. Choose provider settings, target language, and the loaded mods to translate.
7. Press **Start**. Use **Pause**, **Resume**, or **Cancel** as needed.

Each task keeps the language, provider, glossary, skip-completed choice, and apply/export settings it had when **Start** was pressed. Changes to those settings affect the next task. The batch-size slider ranges from 1 to 50; the numeric field accepts larger positive integers. Very large batches may cause game stuttering or exceed provider request limits. This applies to both text and glossary translation. **Batch size** is live: the next batch uses the current value, including after resuming a paused task; an already-sent request keeps its original size. Manual preview edits use the current apply setting.

**Skip completed translations** is enabled by default. It excludes valid completed entries from API requests and from the pending task total, while applying cached results to the game. The status shows the skipped count separately. Valid cached results can be reused across providers; the selected provider is preferred, otherwise the latest available record is used. Cleared preferred records, changed source text, and changed applicable glossary rules still need translation. Turning this option off retranslates selected unprotected entries and can overwrite earlier automatic translations. Protected manual corrections remain intact.

Runtime translations continue to apply on the main thread after the settings window is closed, using a roughly 2 ms time budget per frame and a 200-entry safety cap. Original field text is retained for this game session, so restarting a task does not translate an earlier translation. Clearing a preview translation restores the original field text and allows that entry to be translated again.

Cache saves first write a temporary file, then replace `cache.xml` while retaining the previous version as `cache.xml.bak`. A damaged or missing primary cache can load the backup. Save failures appear in the status/log and preserve the previous cache file.

Exports include only the chosen target language. For each Mod/Def/field, the most recently updated cached version wins across providers and source versions; a cleared latest version suppresses older translations. Export overwrites its own `RuntimeTranslator.xml` files and removes stale files of that name under the selected language's generated `DefInjected` folders. Put hand-maintained translations in separate files or folders.

Provider responses with missing, repeated, or unknown protected placeholders are rejected rather than cached. Previously saved translations are not automatically repaired.

Cancelled tasks finish with `Cancelled`, including cancellation before a request starts or while paused. Unexpected task errors finish with `Failed` and retain the error message. Results already completed before cancellation remain in the cache.

Preview's **Replace next 50** processes each matching record once per operation, even when the replacement contains the search text. The remaining count excludes processed records. Changing the search/replace fields or starting a translation task begins a new replacement operation; manually editing a record allows that record to be replaced again.

## Translating This Mod

The official Simplified Chinese UI is bundled in `Languages/ChineseSimplified/Keyed/RuntimeTranslator.xml` and follows the game language automatically. Its Chinese brand name is **Gesvd的边缘翻译**. Native translations take priority over old machine-translation caches; the mod skips its own translated UI when targeting Simplified Chinese. Other target languages can still translate its settings by selecting this mod in **Mods**. The English source table remains in `Languages/English/Keyed/RuntimeTranslator.xml`; both tables contain matching keys for the shipped UI. The package ID is `gesvd.rimlingo`; cache paths and diagnostic identifiers remain unchanged. Older self-selection and self-translation cache IDs are migrated automatically.

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

English-to-Simplified-Chinese starts with `pawn` and `pawns` mapped to `小人`. Both entries can be changed, for example to `角色`. Other language pairs have independent terms. English source aliases and Auto use the English glossary. Discovery supports the selected source language: Latin-script languages, Russian and Korean use word/phrase candidates; Chinese and Japanese use conservative 2–16-character short fragments and require manual review. It does not provide full CJK linguistic segmentation.

English discovery reads common text fields in the active mod version's `Defs` XML plus English language files; other source languages read their corresponding language catalogs. Common function words, formatting tokens and technical identifiers are excluded. Malformed or unreadable XML files are skipped with a log warning. Counts are for the selected files, not an exhaustive linguistic analysis; singular/plural forms are separate editable terms.

All providers protect confirmed terms as placeholders and restore the exact preferred wording. Matching ignores case, uses whole-word boundaries, and prioritizes longer phrases. Format tags and variables remain protected. A provider that damages these placeholders fails the batch rather than storing inconsistent terms. A word with several meanings uses the same preferred wording throughout the language pair; clear or remove a term if that is unsuitable.

**Apply glossary to cached translations** starts a normal pausable/cancellable task using the current provider and current language pair. It retranslates affected cached original text from loaded mods with the active glossary, excluding protected manual corrections, and may use API quota. Successful results replace obsolete cache variants for that same original; failed or cancelled items keep their previous translations. It ignores glossary search/page and mod selection, skips cleared/unrelated/other-language/native-protected entries and unloaded or changed-source cache entries, and follows the current apply/export switches.

Candidate scanning counts repeated contiguous English phrases of 2–6 non-stop words, longest first, without also counting their component-word occurrences. Independently used words still count. Frequencies are combined across XML files; punctuation, line breaks, stop words and protected formatting tokens separate phrases. Rescanning updates counts while preserving existing terms and preferred translations.

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

## Remaining Coverage Limits

This first version focuses on common loaded `Def` fields such as `label`, `description`, `jobString`, `deathMessage`, `gerund`, and `reportString`.

Standard Keyed XML from selected mods is scanned and applied through the active language dictionary. Existing native translations take priority, and a different effective fallback source blocks application of a conflicting key. Nested Def fields, lists, grammar/rule packs, code-hardcoded UI and dynamically generated text are not comprehensively covered.

## Bundled offline translation

Windows x64 users can select LibreTranslate and enable **Use bundled offline engine (Windows, installed language packs)**. The included engine runs independently on the local machine, uses the existing glossary and requires no API key or internet at runtime. The base includes English → ChineseSimplified. Optional LanguagePacks add English-pivot translation among Chinese, English, Japanese, Korean, Russian, German, French, Spanish and Portuguese. Language packs are standalone expansion mods: subscribe on Steam Workshop after publication, or extract a pack ZIP into RimWorld/Mods, then enable both RimLingo v1.4.0 and the expansion in the game mod list. Each pack declares its dependency and load-after order. The base discovers enabled mod roots automatically, including numeric Workshop paths; disabled expansions are not loaded. No manual copying into the base mod is needed. Offline language dropdowns show reachable installed routes; Auto and Traditional Chinese remain online-only with the provided packs. See [LocalEngine/README.md](LocalEngine/README.md) for packaging, rebuilding, logs, and [license/source obligations](LocalEngine/THIRD_PARTY_NOTICES.md).

## License / 许可证

我们编写的模组主体、测试及构建/打包工具采用 **GPL-3.0-only**；C# 模组附有允许与 RimWorld/Unity 程序集组合运行的有限[链接例外](LICENSE-EXCEPTION.md)。Copyright (c) 2026 Gesvd；项目作者为 Gesvd & Codex。

**LibreTranslate 保持 AGPL-3.0，我们的本地服务集成 `LocalEngine/serve.py` 采用 AGPL-3.0-only。** 模型、Python 和其他第三方组件保留各自许可，不受根目录 GPL 统一覆盖。分发时须保留适用声明并提供匹配的对应源码。GPL 允许商业使用，但分发修改版须履行其源码及相同许可要求。历史 MIT 副本的授权不被撤回。

完整范围说明见 [LICENSING.md](LICENSING.md)，GPL 全文见 [LICENSE](LICENSE)，第三方来源和模型署名见 [THIRD_PARTY_NOTICES.md](LocalEngine/THIRD_PARTY_NOTICES.md)。


## Official content and bundled target-language translations

RimWorld Core and DLC are excluded using the game's official-content flag, both from mod selection and from translation/glossary scanning. Old saved selections cannot reintroduce them.

**Skip mods with bundled target-language translations (entire mod, even if partial)** is enabled by default. It recognizes valid entries in the selected target language, using active content folders and common language-folder aliases. Empty values do not trigger skipping. Chinese, Japanese, Korean and Russian require text in the corresponding script. A partial catalog can skip the entire mod; disable this option to fill missing entries. Native field/key protection remains active either way.

To reuse existing terminology, select mods and the source/target languages, then press **Import glossary from bundled source/target translations** on Glossary. Import runs locally and pairs language catalogs by XML key within each mod. English can also pair raw Def labels with target DefInjected labels; other source languages need matching language catalogs. Active folder precedence is preserved.

The importer adds short words/phrases (at most six words and 64 characters) or fills blank glossary translations. Existing defaults and manual translations are kept, and conflicting target translations for the same source phrase are omitted. Imported terms remain editable/removable through the existing glossary UI. It does not infer individual word meanings from translated sentences; inherited-only labels, non-XML Strings and separate translation mods without matching source files are not imported.


## Native translation priority and text fallback

Existing target-language translations always take priority, including when **Skip mods with bundled target-language translations** is disabled. Turning off whole-mod skipping allows missing entries to be translated; it does not authorize overwriting existing translations. Scanning and the worker exclude native entries, runtime application rejects conflicting cached/manual replacements, and export filters those entries out of its own generated files.

For the current game language, RimLingo snapshots the game's loaded Keyed and DefInjected tables, including separate translation mods and injections on base Def classes. For a different target language it reads the active mods' matching language XML instead. These indexes are prepared when needed for a task, preview application or export, with no new scan during game startup. A game-language object change rebuilds the snapshot; installing/changing mod files still requires the normal game reload. Native priority applies to the translator's supported fields and standard Keyed catalogs; it does not add translation hooks to hardcoded or dynamically generated UI.

The translator keeps existing text when requests fail, results are missing, or a response contains only whitespace, zero-width format characters or empty markup. Clearing an automatic translation restores the original text rather than displaying an empty string. Explicit OpenAI-compatible `content_filter` and `message.refusal` responses fail the batch without becoming translations. Existing good translations and source text remain available. RimLingo does not perform content censorship; an external provider may still refuse or return an inaccurate nonempty translation, which cannot always be identified automatically.

## v1.4.0 API providers and language packs

DeepSeek has independent base URL, model, key and rules; it uses the existing Chat Completions adapter. Claude has separate configuration and uses the native `/v1/messages` endpoint with `x-api-key` and `anthropic-version: 2023-06-01`. Its request allows 8192 output tokens; incomplete/refused responses fail without replacing existing translations. Both share glossary and JSON/placeholder validation. Default model IDs remain editable.

Source and target languages are dropdowns. Offline choices reflect installed models and reachable pivot routes; online providers offer the supported common languages plus Traditional Chinese and source-only Auto. Install selected ZIPs from `LanguagePacks/` like normal RimWorld mods and enable them alongside the v1.4.0 base mod; individual language READMEs explain the layout. The base release excludes optional model weights. Additional model use increases RAM and disk usage; pivot translation can be slower and less accurate.

## v1.4.0 performance, retries and manual corrections

Scanning yields between frames and can be cancelled before requests begin. Game objects stay on the main thread; file/text processing runs in the background. Runtime application uses a roughly 2 ms frame budget with a 200-entry safety cap; an individual game call may exceed the budget. Preview pages contain 50 entries and filtered snapshots are reused until cache/search changes. Replacement still operates over the full filtered result set.

Nonempty manual edits and replacements are protected automatically. Unlock a record to permit subsequent retranslation; clearing it restores the original text and makes it retryable. Older cache files did not record who edited a translation, so protect older manual work explicitly in Preview. Locks apply to the same original source text; changed mod text can be translated again. Native translations always remain protected.

Requests respect both the live entry limit and an approximate 12,000-character budget, not an exact model-token estimate. A single long entry is never silently truncated. Temporary network/rate/server errors receive at most two retries; recognized context/output limits split multi-entry batches. Successful split results are accepted and saved before later parts complete. Retrying can still consume quota. Single-text external LibreTranslate and Lara endpoints are processed entry by entry; the bundled engine has a bulk endpoint with per-entry errors.

Use **Retry failed entries (current provider)** for the last task's failed entries in the selected language pair. Source text and native protection are checked again. This failure list is session-only; after restarting, normal cache skipping can continue unfinished work. Glossary import and whole-mod native skipping support the selected target language, including folder aliases. CJK discovery is conservative and needs review. Standard Keyed catalogs are supported; nested Def text and hardcoded/dynamic strings remain coverage limits.
