# RimLingo v1.3.0 语言扩展模组 / Language expansion mods

语言包现为独立扩展模组，保持 **v1.3.0**。

玩家只需：

1. 安装并启用 **Gesvd的边缘翻译 / RimLingo v1.3.0** 基础模组。
2. 在 Steam 创意工坊订阅所需语言扩展并在模组列表启用；手动安装时，像普通模组一样把对应 ZIP 解压到游戏 `Mods` 目录，再在模组列表启用。
3. 基础模组排在语言扩展之前，按游戏提示重启。在翻译服务页启用内置 LibreTranslate，从下拉框选择语言。

无需搬动模型文件、修改配置或输入路径。基础模组自动识别已启用扩展的实际目录，包括 Steam 创意工坊数字目录；禁用的扩展不会加载。每个扩展的 About.xml 已声明基础模组依赖及加载顺序。封面左下角用国旗、方向箭头和语言代码标明模型方向；中文反向包为 ZH → EN，其他模型包为 EN ↔ 对应语言。

本项目中的各语言目录可直接作为模组发布：

- ChineseSimplified：中文→英语；英语→中文已随基础包提供。
- Japanese / Korean / Russian / German / French / Spanish / Portuguese：各语言与英语双向模型。
- English：仅基础功能与中转说明，不需要独立英语扩展。

每个 ZIP 内直接包含 `RimLingo-Language-<Language>/About/About.xml`、`models/packages`、本语言及中文 README、manifest.json 和许可。发布到创意工坊时上传对应语言文件夹本身（直接包含 About 和 models），每种语言一个条目，并保留许可文件。当前只生成本地发布包，尚未上传创意工坊。

非英语之间经英语中转，需要启用两端扩展。例如日语→中文只需基础模组和日语扩展；中文→日语还需中文反向扩展。所有扩展共享基础模组的离线引擎，不重复安装 Python。繁体中文和自动语种识别不在本批离线扩展范围内。

## English

Install the v1.3.0 base mod and each desired language expansion as ordinary RimWorld mods. Subscribe on Steam Workshop after publication, or extract the selected ZIP into RimWorld/Mods. Enable both the base and expansion in the mod list, base first, and restart. No model copying or path setup is required; enabled Workshop/local mod roots are discovered automatically. Disabled expansion mods are not loaded. Each pack includes About metadata, dependency/load-after declarations, a preview, native/Chinese instructions, model manifest and attribution. Publish each language folder as a separate Workshop item. Non-English pairs may pivot through English and require both endpoint packs.
