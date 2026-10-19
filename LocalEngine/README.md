# Gesvd’s RimLingo — 内置 LibreTranslate（Windows x64，英文 → 简体中文）

在模组设置选择 LibreTranslate，勾选 `Use bundled offline engine (Windows, English to Chinese)`。来源选 English，目标选 ChineseSimplified。无需 API Key、Docker、系统 Python 或联网；第一次翻译时在后台载入模型。原有的外部 LibreTranslate 地址仍可使用。

运行必需：`runtime/`、`models/packages/`、`serve.py`、`READY.json`。发布包还必须附带 `licenses/`、`sources.zip`、本目录的脚本和说明。只复制 Git 源码不会带上被忽略的大型运行文件。`dependencies.json` 和 `requirements-lock.txt` 记录本次依赖版本；`READY.json` 记录模型校验值。

日志位于 RimWorld 配置目录的 `RuntimeTranslator/LocalEngine/engine.log`。服务仅监听随机的本机回环端口。进程退出时模组停止服务，服务也监视游戏进程，避免游戏被强制结束后残留。运行中禁止向外部地址建立连接。CPU 推理限制两个计算线程。

词汇库、标签和游戏变量由现有保护机制处理；本地桥接在保护符之间翻译文本，保证词汇和格式保留。代价是被术语或标签分割的句子可能不够自然；简单断句也可能把缩写拆开。只附带英文到中文模型，其他语言继续使用已有 API。

## 许可与源码

见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。本地 Python 集成脚本 `serve.py` 采用 AGPL-3.0-only，C# 模组保持项目原有 MIT 许可。第三方组件保持各自许可，不能用根目录 MIT 覆盖。`sources.zip` 内有本地桥接源码、构建脚本、依赖来源清单、许可及 copyleft 依赖源码。服务的 `/runtime-translator/source` 提供该档案。

发布二进制时必须一起提供这些资料；仅放 GitHub 上的 C# 代码不满足本地引擎的源码义务。如果修改第三方组件、更新版本或新增模型，需要重新检查许可和来源。发布渠道自身条款仍需另行核对。

## 重建（开发者）

需要 Windows x64、uv 和可复制的 CPython 3.12 发行版。当前运行时为 uv python-build-standalone CPython 3.12.13，BUILD 20260623；请保留发行版 LICENSE。使用全新输出目录，避免旧版本依赖残留：

```powershell
.\LocalEngine\Build.ps1 -PythonHome 'C:\path\to\portable-python-3.12'
```

构建阶段下载固定版本依赖、模型、许可和对应源码；游戏运行时不执行安装。`prepare.py` 核对英中模型 SHA-256 并收集依赖许可。`test_engine.py` 启动实际独立进程，检查离线翻译、保护符、非法输入和父进程退出清理。完整发布包应保留整个 LocalEngine，开发缓存 `build-cache/` 不应打包，其中有未采用的原始模型附属文件。

验证完成后，运行 `LocalEngine/runtime/python.exe -X utf8 LocalEngine/package.py`（从项目根目录执行）生成 `Releases/Gesvds-RimLingo-LibreTranslate-win64.zip`。这个完整包包含运行时、采用的模型文件、许可证和源码包；不会带上开发缓存、原始未采用的断句模型或游戏 DLL。GitHub 源码提交不包含大文件，发布时应上传完整包。