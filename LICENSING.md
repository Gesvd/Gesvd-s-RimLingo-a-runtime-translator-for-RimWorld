# 许可范围 / Licensing

Gesvd’s RimLingo: a runtime translator for RimWorld

Copyright (c) 2026 Gesvd。项目作者：Gesvd & Codex。

本项目包含采用不同许可证的独立组件。根目录 GPL 许可证适用于我们编写的项目内容；第三方程序、模型和运行库保留各自的授权。

## 一、我们编写的部分：GPL-3.0-only

适用范围：`Source/` 中的 C# 模组代码、`Tests/`、项目文件、我们编写的说明文档，以及 `LocalEngine/Build.ps1`、`prepare.py`、`test_engine.py`、`package.py`。第三方许可证原文及另有标注的文件除外。

使用 GNU General Public License version 3 only（SPDX：`GPL-3.0-only`），全文见 [LICENSE](LICENSE)。C# 模组同时适用下述链接例外。

- 允许使用、修改、分发及商业使用；GPL 不禁止收费。
- 分发 GPL 部分的二进制或修改版时，须按 GPL 提供对应源码，保留版权、许可声明，并标明修改。
- 普通玩家只安装和运行模组，不需要公开存档、设置或自己电脑上的文件。
- 无担保。具体权利和义务以 GPL 原文为准。

### RimWorld / Unity 链接例外

模组需要引用游戏自带的 RimWorld、Verse 和 Unity 程序集。我们为自己的 C# 模组代码授予有限的额外链接许可，允许它与这些专有组件组合运行及分发模组本身。正式条款见 [LICENSE-EXCEPTION.md](LICENSE-EXCEPTION.md)。这不会授权分发游戏或 Unity 的 DLL，也不会改变 LibreTranslate 的许可。

## 二、LibreTranslate 及本地集成：AGPL-3.0

LibreTranslate 1.9.6 保持上游 AGPL-3.0 许可。我们编写的 `LocalEngine/serve.py` 是本地服务的集成代码，单独采用 `AGPL-3.0-only`，不适用上述链接例外。

- 上游项目：[LibreTranslate](https://github.com/LibreTranslate/LibreTranslate)。
- 许可全文：[AGPL-3.0](LocalEngine/licenses/AGPL-3.0.txt)。
- 分发时须保留上游版权、许可及对应源码资料。
- 修改服务并允许用户通过网络交互时，须遵守 AGPL 第 13 条的源码提供要求。
- 本地服务与 C# 模组通过 HTTP 通信；本项目分别标明各组件许可，并不把 AGPL 组件重新许可为 GPL。

完整安装包包含 `LocalEngine/sources.zip`，其中有集成代码、构建资料及所收集的 copyleft 依赖源码；运行的服务通过 `/runtime-translator/source` 提供该档案。模组自身的 C# 源码随包放在 `Source/`，项目文件位于根目录。公开发布时，须按相应许可持续提供与二进制匹配的完整对应源码及必要的构建资料；不能用一个失效的仓库链接替代。

## 三、模型、Python 和其他第三方组件

| 组件 | 适用许可与说明 |
|---|---|
| Argos 英中模型 1.9 | 维护者说明模型为 MIT/CC0；模型 README 另标明原始 OPUS 模型为 CC BY 4.0，须保留来源及署名 |
| CPython | PSF 及其附属组件许可 |
| Argos、CTranslate2、ONNX Runtime 等 | 各自的 MIT、Apache、GPL/AGPL 或其他适用条款；原生库还可能有单独许可 |

详细来源、作者署名和分发说明见 [THIRD_PARTY_NOTICES.md](LocalEngine/THIRD_PARTY_NOTICES.md)。依赖清单见 [dependencies.json](LocalEngine/dependencies.json)，许可原文在 [LocalEngine/licenses/](LocalEngine/licenses/)。根目录 GPL 不覆盖这些第三方许可。

## 四、历史版本与品牌

本次许可范围调整日期为 2026-10-20。此前已经按 MIT 授权分发的副本，继续享有其原有 MIT 授权；本次改为 GPL 不撤回旧副本的授权。

项目名称和作者署名不代表对任何第三方分支的认可。代码许可本身不等于对项目商标或其他第三方权利的授权。

---

English summary: Original project code, tests, documentation and our build/package tools are GPL-3.0-only, with the additional permission in LICENSE-EXCEPTION.md for the C# mod. LocalEngine/serve.py remains AGPL-3.0-only; LibreTranslate and all bundled dependencies/models retain their own licenses. Keep the applicable notices and provide matching Corresponding Source when distributing covered binaries. Previously distributed MIT copies retain their original permissions.
