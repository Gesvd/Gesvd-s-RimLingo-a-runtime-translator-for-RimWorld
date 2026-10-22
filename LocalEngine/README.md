# Gesvd’s RimLingo — 内置 LibreTranslate（Windows x64，可选语言扩展包）

在模组设置选择 LibreTranslate，勾选 `Use bundled offline engine (Windows, installed language packs)`。来源选 English，目标选 ChineseSimplified。无需 API Key、Docker、系统 Python 或联网；第一次翻译时在后台载入模型。原有的外部 LibreTranslate 地址仍可使用。

运行必需：`runtime/`、`models/packages/`、`serve.py`、`READY.json`。发布包还必须附带 `licenses/`、`sources.zip`、本目录的脚本和说明。只复制 Git 源码不会带上被忽略的大型运行文件。`dependencies.json` 和 `requirements-lock.txt` 记录本次依赖版本；`READY.json` 记录模型校验值。

日志位于 RimWorld 配置目录的 `RuntimeTranslator/LocalEngine/engine.log`。服务仅监听随机的本机回环端口。进程退出时模组停止服务，服务也监视游戏进程，避免游戏被强制结束后残留。运行中禁止向外部地址建立连接。CPU 推理限制两个计算线程。

词汇库、标签和游戏变量由现有保护机制处理；本地桥接在保护符之间翻译文本，保证词汇和格式保留。代价是被术语或标签分割的句子可能不够自然；简单断句也可能把缩写拆开。基础包附带英文到中文模型，其他离线语言模型由独立语言扩展模组提供。C# 在主线程捕获已启用的 `gesvd.rimlingo.language.*` 模组路径，通过 `--pack` 传入服务；服务读取每个扩展自己的 `models/packages`。引擎只读取基础模型及显式传入的已启用扩展路径，运行时不扫描系统已有模型、不下载模型。其他在线语言仍可使用 API。

## 许可与源码

见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。本地 Python 集成脚本 `serve.py` 采用 AGPL-3.0-only，C# 模组及我们编写的构建、测试和打包脚本采用 GPL-3.0-only；C# 模组另附 RimWorld/Unity 链接例外，详见项目根目录的 [许可范围说明](../LICENSING.md) 和 [链接例外](../LICENSE-EXCEPTION.md)。第三方组件保持各自许可，不能用根目录 MIT 覆盖。`sources.zip` 内有本地桥接源码、构建脚本、依赖来源清单、许可及 copyleft 依赖源码。服务的 `/runtime-translator/source` 提供该档案。

发布二进制时必须一起提供这些资料；仅放 GitHub 上的 C# 代码不满足本地引擎的源码义务。如果修改第三方组件、更新版本或新增模型，需要重新检查许可和来源。发布渠道自身条款仍需另行核对。

## 重建（开发者）

需要 Windows x64、uv 和可复制的 CPython 3.12 发行版。当前运行时为 uv python-build-standalone CPython 3.12.13，BUILD 20260623；请保留发行版 LICENSE。使用全新输出目录，避免旧版本依赖残留：

```powershell
.\LocalEngine\Build.ps1 -PythonHome 'C:\path\to\portable-python-3.12'
```

构建阶段下载固定版本依赖、模型、许可和对应源码；游戏运行时不执行安装。`prepare.py` 核对英中模型 SHA-256 并收集依赖许可。`test_engine.py` 启动实际独立进程，检查离线翻译、保护符、非法输入和父进程退出清理。完整发布包应保留整个 LocalEngine，开发缓存 `build-cache/` 不应打包，其中有未采用的原始模型附属文件。

验证完成后，运行 `LocalEngine/runtime/python.exe -X utf8 LocalEngine/package.py`（从项目根目录执行）生成 `Releases/Gesvds-RimLingo-v1.3.0-LibreTranslate-win64.zip`。这个完整包包含运行时、采用的模型文件、许可证和源码包；不会带上开发缓存、原始未采用的断句模型或游戏 DLL。GitHub 源码提交不包含大文件，发布时应上传完整包。
## v1.3.0 可选语言包

在项目根目录运行 `LocalEngine/runtime/python.exe -X utf8 LocalEngine/build_language_packs.py` 可构建并校验下载，生成 `LanguagePacks/<语言>` 目录及独立的 `RimLingo-v1.3.0-<语言>.zip`。模型按上游索引版本下载，保留原始 README、模型版本、来源与 SHA-256；实际采用的模型权重不修改，省略未使用的 Stanza/MiniSBD 权重。使用 Argos 官方目录镜像下载，支持断点续传和校验后重试。基础包与各语言包分别打包，基础包不重复携带扩展模型。

英文是中转语言；中文包补充中文→英文，其他语言包提供英语双向模型。将扩展作为普通模组安装并启用后重启，服务会读取已启用扩展并检查翻译路径；不支持的组合返回明确错误。没有联网自动识别功能，离线源语言必须指定。语言包的说明与许可随包保留，具体来源见各自 manifest.json。测试命令：`RT_TEST_LANGUAGE_PACKS=1` 环境变量配合 `LocalEngine/runtime/python.exe -X utf8 LocalEngine/test_engine.py`，检查全部打包语言的双向翻译及日语→中文中转。使用新的脚本或文档后重新运行 `package.py`，它会先更新 `sources.zip` 内对应的本地服务和构建源码。

发布前运行 `LocalEngine/runtime/python.exe -X utf8 LocalEngine/verify_language_packs.py`，核对全部语言包的原始下载 SHA-256、模型方向、ZIP CRC、安装目录与随包说明。

每个扩展含 `About/About.xml`、封面、独立包 ID、基础模组依赖及 loadAfter。ZIP 根目录为 `RimLingo-Language-<语言>`，直接解压到游戏 Mods，或发布为独立创意工坊条目。开发输出仍保存在本项目 LanguagePacks 下；不能靠目录存在自动启用扩展。

语言扩展的封面来自 `Artwork/LanguagePacks/<语言>-Preview.png`；构建时复制到扩展 About/Preview.png。原尺寸 PNG 与生成说明保存在同目录。校验脚本检查 8 个独立封面、800×450 PNG、小于 1 MiB，且 ZIP 内封面与发布源一致。
