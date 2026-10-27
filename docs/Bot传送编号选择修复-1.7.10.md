# Bot 传送编号选择修复 — 1.7.10

日期：2026-10-20（Asia/Shanghai）。

## 问题与根因

原公开菜单虽然写着 `!ze_bot_move [编号]`，点击时却只执行没有参数的 `css_ze_bot_move`；生产服务把空参数解释成自己的全部 Bot，因此没有选择编号的步骤，点击后直接一起传送。

## 修复后的用法

| 操作 | 行为 |
|---|---|
| `!ze_public` → `!ze_bot_move 选择编号传送自己的 Bot 到脚下` | 打开自己的 Bot 列表，不移动任何 Bot |
| 聊天 `!ze_bot_move` | 同样打开编号选择列表 |
| 点击 `!ze_bot_move 编号 → 阵营 · HP 血量` | 只将选中的 Bot 传送到点击时自己的脚下 |
| 聊天 `!ze_bot_move <编号>` | 直接传送指定编号，编号与 `!ze_bot_status` 一致 |
| 点击“传送我放置的全部 Bot”或输入 `!ze_bot_move all` | 明确传送自己的全部 Bot |

列表按真实 Bot 编号排序，显示 T/CT 阵营与当前统计血量；待创建的 Bot 会标记“待创建”，选中后只更新它自己的放置位置。无 Bot 时显示说明，不显示全部传送按钮。

传送后保留当前菜单和页码。需要主动更新条目时使用“刷新我的 Bot 编号列表”；另有返回公开指令与手动关闭。服主 Bot 多于一页时正常分页，页面上的选项数字不代替实际 Bot 编号。

失效/已删除/其他玩家的编号不会降级为全部传送；点击时重新校验连接身份、玩家存活与换队就绪状态、地图代次和权限。地图切换、重连或插件卸载后的旧回调失效。移动只改位置/朝向，不改阵营、血甲或伤害统计。添加双方 Bot、两只共享额度与服主额度规则不变。

**兼容性变化：无参数 `!ze_bot_move` 现在打开菜单。原来依赖无参数传送全部的用法，应改为明确的 `!ze_bot_move all`。处死和删除指令语义没有改变。**

## 实现与验证

工作工程：`D:\34184\Documents\ChatGPT\cs2服务器\ZePractice`。

- `src\PracticeBotService.Menus.cs`：真实 Bot 编号列表、无参数入口、动态标签与回调检查。
- `src\PracticeBotService.cs`：空参数不再传送全部、无匹配编号直接结束；地图代次保护。
- `src\PublicGameplayService.Menus.cs`：公开菜单入口文案。
- `tests\PublicMenuTests\BotMoveMenuCases.cs`：生产菜单源码测试，覆盖公开入口、单只/全部、过期/越权、保持打开/关闭、刷新与跨页编号。
- `tests\PracticeBotNativeProbe\TeamProbe.cs`：真实引擎中检查空参数不移动、外部/无效编号不回退全部、只移动指定编号、明确 all 后才移动全部。

证据目录：`D:\34184\Documents\ChatGPT\cs2服务器\ZePractice\artifacts\bot-move-selector-20261020`。

插件及测试探针构建均为 0 错误、0 警告。15 组自动化回归共 **1657** 项通过（公开菜单 **115** 项）；隔离服双方 Bot/传送探针 **24/24**，原有生命周期探针 **25/25**，合计 **49** 项实际引擎检查通过。具体记录见该目录的 `build.log`、`regression-summary.json`、`native-summary.json`。

菜单测试运行真实生产菜单/无线电/Panorama 代码配合引擎桩；引擎探针运行在 `127.0.0.1:27025` 的隔离服，使用两个普通引擎 Bot 作为拥有者夹具，调用实际服务端方法验证位置，没有真人客户端画面验收。

## 部署与范围

正式插件目录：`E:\mapper\chana\steamapps\common\Counter-Strike Global Offensive\game\csgo\addons\counterstrikesharp\plugins\ZePractice`。

更新四个 ZePractice 构建文件到 1.7.10；本次修改的源码、测试、说明及构建产物同步到维护工程 `E:\mapper\chana\ZePractice`。备份和哈希清单保存在 `E:\mapper\chana\ZePractice\repair-evidence\bot-move-selector-20261020`，不覆盖服务器配置、权限、地图或玩家数据。

不修改 CS2Fixes、地图脚本、人物模型、手模或工坊包，不需要新玩家启动项。正式服保持停服，下次正常开服加载修复；测试结束后关闭隔离服，并恢复隔离服原有插件文件。
