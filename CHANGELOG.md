# 更新记录 / Changelog

## v1.4.0

- 主翻译扫描按帧读取游戏对象，可在扫描阶段取消；XML 读取、术语处理和队列整理在后台完成。
- 译文预览每页 50 条，仅在缓存或搜索变化时重新生成筛选结果。
- 建立 Def 与字段索引，译文应用每帧采用约 2 ms 时间预算，并保留 200 条工作上限。单次游戏调用仍可能超过预算。
- 缓存保存使用独立快照，序列化和写盘不占用缓存数据锁；无变化不重写，保留每批保存、原子替换和备份。
- 按条目数及约 12,000 字符预算分批；临时请求错误最多重试两次，已识别的输入/输出超限自动拆分。可仅重试上次任务中当前语言组合的失败条目。
- 内置离线引擎支持批量请求及逐条错误；拆分批次的成功部分及时保存，取消或后续失败不撤销已保存的译文。
- 扫描其他模组有效语言目录中的 Keyed XML，并通过游戏语言字典应用；保留自带翻译，检查实际回退原文以避免键冲突，清空后恢复原始文本。
- 手动修改或批量替换的非空译文自动受保护，可逐条解除；重新翻译、应用术语库和晚到的 API 结果不会覆盖受保护的同一原文。旧缓存中没有人工标记的记录保持原状。
- 术语导入和整模组跳过扩展到所选目标语言；支持拉丁字母、俄语、韩语候选提取，以及中日文保守的短片段提取。
- 基础模组与 8 个语言扩展统一为 v1.4.0，保留原设置、包 ID、缓存路径和模型权重；未声明未经实测的 RimWorld 1.7 兼容性。

English: improves scanning, preview paging, indexed runtime application and cache I/O; adds bounded retries, text-aware batching, failed-entry retry and offline bulk requests; supports generic Keyed catalogs, protected manual corrections and multilingual glossary workflows. Existing settings and cache remain usable. CJK discovery uses short fragments rather than full linguistic segmentation. Game and paid API integration still require user validation.

验证：第一阶段 216 项检查通过；两阶段合并后 263 项检查通过（包含本机模拟 HTTP 与真实 C# 离线集成），DLL 编译 0 警告、0 错误。Python 实际引擎验证全部语言扩展双向路由、日语→中文中转、批量接口、格式保护、非法输入及父进程退出清理。未进行游戏内交互、1.7 或付费 API 的实际验证；未测量实际游戏帧率提升。

## v1.3.0

- 8 个语言扩展配备独立国旗封面，左下角标注语言方向与代码，适用于国际受众。

- 新增独立多语言离线扩展模组，按语言分文件夹，含 About、封面、包 ID、基础依赖和加载顺序，可作为普通模组安装或单独发布创意工坊，附本语言与中文说明、模型来源和校验值。
- 内置引擎自动发现已启用的语言扩展模组并支持英语中转；运行时不下载模型。
- 源语言和目标语言改为下拉选择；离线模式仅显示已安装且可达的语言组合。
- 新增 DeepSeek 和 Claude API 接入，分别保存地址、模型、密钥和翻译规则。
- Claude 使用原生 Messages API，复用原有术语、占位符和 JSON 校验，拒译及截断不会覆盖旧译文。
- 保留 v1.2.0 设置、包 ID `gesvd.rimlingo` 和缓存目录；基础包与语言包分别发布。

English: adds standalone language expansion mods with automatic enabled-mod discovery, offline routing through English, validated language dropdowns, independent DeepSeek configuration and native Claude Messages API support. Existing settings, package ID and cache paths are preserved.
