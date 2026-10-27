# 个人练习 Bot、移动 HUD 与资源安装锁修复 — 1.6.0

## 正式服交付

2026-10-16 12:49–12:50（北京时间）完成空服热部署与服务端验证：

- ZePractice **1.6.0**。
- MultiAddonManager **v1.6.2-0-gd6b4f4b-saibochana.20261016**，修正资源卸载路径与热加载地图身份恢复。
- 正式进程 PID **228** 保持不变；地图 `ze_forhidden_slicoo_cn / 3817448617`、`spawn=2`、`roundStart=4.15625` 均未变化。
- 没有执行正式服换图、回合重启、客户端重新绑定或地图脚本修改；测试探针只在独立 27025 隔离服运行。
- 原有配置值保持不变，仅新增 `PracticeBots.MaximumHealth=1000000`。
- 工坊仍使用 **3818340815 / saibochana_Server**。本轮修服务器代码，没有重新上传、新建条目或手动覆盖 Steam 缓存。

## 一、所有玩家可用的个人 Bot

聊天输入以下指令；客户端控制台对应写成 `css_ze_bot_*`，不加 `!`。

| 聊天指令 | 行为 |
| --- | --- |
| `!ze_bot_hp 35000` | 设置之后新加 Bot 的生命和生命上限；不改已有 Bot |
| `!ze_bot_add` | 在发出指令时本人的位置添加一只敌方 Bot |
| `!ze_bot_move` | 将自己的全部 Bot 移到本人当前位置；保留生命、阵营与统计 |
| `!ze_bot_move 3` | 仅移动自己编号为 3 的 Bot |
| `!ze_bot_kill` | 处死自己的全部 Bot，并逐只结算 |
| `!ze_bot_kill 3` | 仅处死自己编号为 3 的 Bot |
| `!ze_bot_clear` | 删除自己全部 Bot，包括待创建请求；不影响别人 |
| `!ze_bot_status` | 查看自己的 Bot 编号、血量、实时伤害统计与下次预设血量 |

这些入口已放入 F9 / `!ze` / `!ze_public` 的“ZE 公开指令 · 选择即用”菜单，菜单总计 43 项。血量入口可选预设或输入数值。

### 规则

- 人类 CT 放置僵尸 T；僵尸 T 放置人类 CT。添加和移动需要本人存活且换队已完成。
- 普通玩家最多两只，**正在创建的也计入额度**。已认证服主没有个人两只限制，但仍受引擎总槽位限制，并保留至少一个真人连接位置；普通管理员不自动获得服主豁免。
- 预设血量有效范围为 **1–1,000,000**。服主可在配置中调整上限，配置允许范围为 1–10,000,000；聊天输入超出当前上限会被拒绝。
- 默认僵尸靶 35000 HP / 2500 甲，带真正的头盔；默认人类靶沿用服务器普通生命。两类均为静止练习目标，不开全局 `bot_stop` 控制产品行为。
- Bot 保留正常移动类型与受伤物理，不通过冻结全服或锁死速度倍率实现静止。
- 个人 Bot 不走真人的感染、自动复活、回血、回甲和补给流程。死亡后结算并断开，不补出替代 Bot。
- 所有操作按连接身份、控制器句柄、UserID 和连接序号归属；不是只看名字或槽位。重复使用槽位不会接管其他玩家的目标。
- 拥有者离线、回合重开、换图或插件卸载时清理对应目标。额外覆盖“引擎已创建但还未绑定身份”的两 tick 窗口，避免留下无人管理的 Bot。
- 连续添加最短间隔半秒，用于串行化引擎创建，不限制正常重复添加。

### 死亡/处死结算

向拥有者的聊天框输出：初始 HP、有效扣血、累计事件伤害、护甲损失、命中数、头部命中数、受击减速累计秒数、最低速度倍率对应的最大减速比例、存活时间及最后攻击者。

有效扣血和游戏事件伤害分别记录；过量伤害与整数舍入不会伪装成额外实际扣血。处死清空的生命不冒充射击伤害。`!ze_bot_clear` 是直接删除，不输出伪造击杀结算。

减速时间由受伤后的真实 `VelocityModifier` 积分得到，不把“站着不动”算成减速。本轮实弹样本该倍率始终为 1，因此实测结果为 **0 秒**；非零累计有单元测试覆盖，但不宣称实测过非零枪击减速恢复。

## 二、移动 HUD 修复

默认对人类、僵尸开启，位置仍在屏幕底部中央、队徽上方，显示地速、垂直速度、跳跃高度与状态。

```text
!ze_speed on
!ze_speed off
!ze_speed
!ze_movement_status
```

修复了三个独立问题：

1. `custom_hud_layout.layout` 改用逻辑资源名 `panorama/layout/custom_game/zepractice/movement.vxml`；安装完整性检查仍验证 `.vxml_c` 和 `.vcss_c`。
2. 热加载与地图开始均重新检查实际 **installed/content** 中的 VPK，不再只等不会重放的 precache 事件；不会把 downloads 暂存包当作已安装。
3. 显示状态改为实体的持久网络状态，再通过 CheckTransmit 仅传给拥有者，避免实体尚未到达客户端时一次性个人显示消息丢失。

每人独立面板，关闭只删除自己的实体；面板不捕获键鼠。个人练习 Bot 不创建 HUD。

**验收边界：** 已验证真实服务器实体、布局字段、持久显示状态、两行文字、个人开关和无错误。没有真人客户端画面验收，不能将服务端断言等同于最终屏幕显示、排版或截图。

## 三、资源已通过审核；实际问题是安装提交被锁

正式服此前能下载新版资源，但仍挂载旧版 VPK。Steam 返回：

```text
Failed to acquire access lock for this operation. (33)
```

原 MAM 挂载使用逻辑路径 `3818340815.vpk`，卸载却使用物理文件名 `3818340815_dir.vpk`，没有移除实际搜索路径，导致包保持占用。

本轮修复：

- 卸载使用与挂载一致的逻辑 VPK 路径。
- 将“下载完成但安装未提交”独立标记为 `download-complete-awaiting-install`，不把 100% 当作安装成功，也不归因于审核。
- 连续空服十秒、仅挂载这一个资源包时，暂时释放资源挂载，交由 Steam 正常提交，然后重新挂载；保持客户端资源要求，失败恢复并限频重试。
- 玩家进入时恢复原挂载；不卸载当前地图 VPK、不直接改缓存、不自动换图或重启回合。
- 热加载 MAM 时按当前 `GAME` 资源的真实来源恢复工坊地图 ID，避免只下发资源 ID、遗漏地图 ID。不能用 `MOD` 中同名地图文件存在与否判断当前地图来源。

正式服验收结果：

```text
Current map addon: 3817448617
Required client addons: 3817448617,3818340815
resourceInstallRepair.phase = installed-and-remounted
resourceInstallRepair.error = null
status = current
remoteUpdated = installedUpdated = 1792082055
HUD resources.Ready = true
HUD errors = []
```

资源文件大小 **2096978 bytes**；manifest **7625509608324204902**；SHA256：

```text
5A367D80B2626AFF8CFC22ED89E395E206776F13313104353482532B17E1F49B
```

以后仍由现有服务端更新模块每 60 秒检查工坊。地图内容在下一次获准的地图加载时使用新版，不自行打断回合。上面的安装锁恢复仅针对本服资源包，不宣称所有第三方工坊条目都可用：现有清单仍有五个上游不可用条目，本轮没有改删它们。

## 四、验证记录

证据目录：`D:\34184\Documents\ChatGPT\CS2脚本\ZePractice\artifacts\personal-bots-hud-20261016`。

- 托管主插件与测试探针构建：0 警告、0 错误。
- MAM 原生编译成功；保留 SDK 转换警告和 LNK4099 调试符号警告，不称零警告。
- 21 组离线套件退出码均为 0；最终重跑 Bot 32、公开菜单 25、工坊更新 46、移动采样 31、权限 86、公开玩法 348 项，全部通过。
- 当前真实工坊地图上的个人 Bot 原生生命周期 **25/25**。
- 原生移动 HUD **10/10**。
- 实弹伤害及停止射击后 12 秒不回血/回甲 **5/5**。最终样本：100000→99933 HP、2500→2488 甲，3 次命中；有效扣血 67、事件伤害 66。
- 创建完成尚未绑定身份时的取消/地图结束边界 **3/3**。
- 普通地图与真实工坊图的 MAM 热加载均验证；热加载本身保持各自 spawn 不变。
- 正式服 **12 项** 部署断言通过：PID、spawn、地图、roundStart、非冻结状态、地图+资源客户端清单、工坊安装版本、VPK hash、HUD ready、HUD errors、Bot service、配置保留。
- 隔离服的换图、冷启动及 Bot 射击测试不等同于正式服操作。本轮正式服没有换图；早先隔离服冷启动保留了 MAM 原有的一次地图重载行为，新增安装锁恢复动作本身未重载地图。

关键文件：

```text
D:\34184\Documents\ChatGPT\CS2脚本\ZePractice\src\PracticeBotService.cs
D:\34184\Documents\ChatGPT\CS2脚本\ZePractice\src\PracticeBotLogic.cs
D:\34184\Documents\ChatGPT\CS2脚本\ZePractice\src\MovementHudService.cs
D:\34184\Documents\ChatGPT\CS2脚本\ZePractice\src\WorkshopUpdateService.ResourceRepair.cs
D:\34184\Documents\ChatGPT\CS2脚本\ZePractice\native\MultiAddonManager-20261016
D:\34184\Documents\ChatGPT\CS2脚本\ZePractice\artifacts\personal-bots-hud-20261016\production-verification.json
```

## 五、备份与维护

正式部署前原 DLL、配置、PDB、运行元数据和哈希清单：

```text
D:\34184\Documents\ChatGPT\CS2脚本\ZePractice\artifacts\personal-bots-hud-20261016\deployment-backup-20261016-124852
```

原始本轮初始备份 `baseline` 也保留未覆盖。恢复旧版本需先确认空服；原生 MAM DLL 必须成功卸载后才替换，托管插件 DLL 最后复制。不要恢复旧 Steam 缓存来“回滚资源”，也不要把测试插件复制到正式目录。

维护源码同步到 `E:\mapper\chana\ZePractice`。托管重建：

```powershell
dotnet build 'E:\mapper\chana\ZePractice\ZePractice.csproj' -c Release
```

MAM 的源码、GPLv3 LICENSE、补丁、上游基线、工具链与哈希见独立原生目录的 README。重建及安装不需要重新发布工坊资源。
