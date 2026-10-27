# 练习 Bot 双方阵营选择 — 1.7.9

日期：2026-10-20（Asia/Shanghai）。本次只修改服务端练习 Bot 的阵营选择、公开菜单及说明，不改地图脚本、玩家模型、手模、CS2Fixes 或工坊资源。

## 使用方式

所有玩家可使用，必须存活并已完成换队：

| 聊天指令 | 作用 |
|---|---|
| `!ze_bot_add t` | 在自己发出指令时的位置添加一个僵尸 T Bot |
| `!ze_bot_add ct` | 在自己发出指令时的位置添加一个人类 CT Bot |
| `!ze_bot_add ally` | 添加自己当前阵营的 Bot |
| `!ze_bot_add enemy` | 添加敌方阵营的 Bot |
| `!ze_bot_add` | 兼容原行为，默认添加敌方 |

菜单：`!ze_public` → `!ze_bot_add 选择 Bot 阵营并在脚下添加` → 选择阵营。执行后保留子菜单，便于重复添加；返回和主动关闭规则不变。

- 每次只添加一个，不提供一次添加两个的 `both` 参数。
- 同一玩家可以同时拥有 T 和 CT Bot；普通玩家**双方合计最多两个**，待创建请求也计入。服主不限个人数量，仍受服务器空闲玩家槽位限制。
- 阵营、位置、血量和护甲在发出请求时保存，异步创建时不重新推导敌方阵营。
- 没有自定义血量时按所选 Bot 阵营取默认值：T 使用服务器僵尸生命/护甲（当前 35000 / 2500，有头盔）；CT 使用默认生命（当前 100）、护甲 0。
- `!ze_bot_hp <数值>` 对之后添加的任一阵营生效，生命上限同步，受现有 `PracticeBots.MaximumHealth` 限制。已经放置的 Bot 不因改预设而回血或清空统计。
- `!ze_bot_move [编号]` 移动自己的 Bot，保留原阵营、血量与伤害记录；省略编号移动自己的全部双方 Bot。
- `!ze_bot_kill [编号]` 处死并结算自己的 Bot；`!ze_bot_clear` 删除自己的全部双方 Bot，不影响其他玩家。死亡后删除、不复活的规则不变。
- 练习 Bot 的持枪例外保持原行为；没有放开真人僵尸拿枪限制。

## 代码位置

工作工程：`D:\34184\Documents\ChatGPT\cs2服务器\ZePractice`。

- `src\PracticeBotLogic.cs`：阵营参数解析及兼容的敌方默认。
- `src\PracticeBotService.cs`：参数入口、按目标阵营生成血甲快照、状态说明；双方共享原有归属和数量限制。
- `src\PublicGameplayService.Menus.cs`：阵营选择子菜单，继续走原权限/指令入口。
- `tests\PracticeBotTests`、`tests\PublicMenuTests`、`tests\PracticeBotNativeProbe`：逻辑、菜单、实际引擎回归。

## 验证结果及边界

证据目录：`D:\34184\Documents\ChatGPT\cs2服务器\ZePractice\artifacts\bot-team-choice-20261020`。

- 正式插件及原生引擎测试插件均构建成功：0 错误、0 警告。
- 15 组回归共 **1626** 项通过，其中练习 Bot 96 项、公开菜单 84 项。菜单测试使用生产源码与引擎桩，不是本轮真人客户端画面验收。
- 隔离 CS2 `127.0.0.1:27025` / `de_dust2`，实际加载 1.7.9；双方阵营探针 **20/20**，原默认敌方生命周期探针 **25/25**。
- 实际引擎检查包括：同队添加、双方并存、目标阵营默认生命/甲/头盔/武器、自定义血量、合计额度、服主角色测试分支、跨拥有者处死拦截、移动保留状态、死亡删除、只清除本人 Bot、取消待创建请求不泄漏 Bot。
- 拥有者夹具是两个真实引擎 Bot；探针调用生产服务方法，服主分支由探针显式传入角色。没有把它描述成真人聊天入口或真实服主身份端到端验收。
- 首次新探针比较“创建完成后的拥有者坐标”时空间断言未通过；夹具随后改用平坦位置、清零速度并验证请求坐标快照。生产位移逻辑没有为迁就测试而修改。最终结果来自单实例冷启动后的完整两组探针，早期失败记录保留。
- RCON 最终诊断长文本存在控制台截断，原始输出另存；成功判断使用完整探针 JSON 及其末项完成断言，不把截断 JSON 当完整状态证据。
- 最后无个人练习 Bot 残留；隔离服已退出，隔离服原有八个插件文件按备份 SHA256 恢复。未启动正式服，未操作真人客户端。

## 部署与回退

正式插件目录：`E:\mapper\chana\steamapps\common\Counter-Strike Global Offensive\game\csgo\addons\counterstrikesharp\plugins\ZePractice`。

本次将 1.7.8 更新为 1.7.9，仅替换 `ZePractice.dll`、`ZePractice.pdb`、`ZePractice.deps.json`、`ZePractice.runtimeconfig.json`。维护工程 `E:\mapper\chana\ZePractice` 同步本次源文件、测试、文档及四个构建产物；不覆盖配置、权限、地图或玩家数据。

备份根目录：`E:\mapper\chana\ZePractice\repair-evidence\bot-team-choice-20261020`。

- `deployment-backup\installed-files.json`：正式文件新旧哈希与原文件备份。
- `source-backup\installed-files.json`：维护工程本次更新文件的备份清单。
- 工作工程证据目录中的 `deployment-verification.json`：正式/维护文件与构建产物的最终比对。

正式服保持原来的停服状态，下次正常启动加载新版。本次是服务端动态菜单与逻辑更新，不需新增玩家启动参数，也不需重新上传或下载工坊资源；此前工坊分发问题的状态不因本次更新而作额外保证。

如需回退，先停服，依据各清单逐项恢复存在的原文件并核对 `OldSHA256`；原先不存在的文件须按清单单独处理，不批量覆盖其他新数据。
