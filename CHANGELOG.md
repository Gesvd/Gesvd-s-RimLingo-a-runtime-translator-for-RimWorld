# 更新记录 / Changelog

## v1.3.0

- 8 个语言扩展配备独立国旗封面，左下角标注语言方向与代码，适用于国际受众。

- 新增独立多语言离线扩展模组，按语言分文件夹，含 About、封面、包 ID、基础依赖和加载顺序，可作为普通模组安装或单独发布创意工坊，附本语言与中文说明、模型来源和校验值。
- 内置引擎自动发现已启用的语言扩展模组并支持英语中转；运行时不下载模型。
- 源语言和目标语言改为下拉选择；离线模式仅显示已安装且可达的语言组合。
- 新增 DeepSeek 和 Claude API 接入，分别保存地址、模型、密钥和翻译规则。
- Claude 使用原生 Messages API，复用原有术语、占位符和 JSON 校验，拒译及截断不会覆盖旧译文。
- 保留 v1.2.0 设置、包 ID `gesvd.rimlingo` 和缓存目录；基础包与语言包分别发布。

English: adds standalone language expansion mods with automatic enabled-mod discovery, offline routing through English, validated language dropdowns, independent DeepSeek configuration and native Claude Messages API support. Existing settings, package ID and cache paths are preserved.
