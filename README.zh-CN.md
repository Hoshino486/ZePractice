> **详细操作教程**：`E:\mapper\chana\ZePractice\服务器使用教程.md`。包括朋友上管理、时间倍率、回合/地图时长、全部常用指令、Boss/翻译配置、更新和排错。2026-10-11 已修复 Windows PowerShell 5.1 中文编码兼容，正式入口仍为 `E:\mapper\chana\ZePractice\run.bat`。

# ZE 跑图整合服（本机安装说明）

> **2026-10-16 当前版本 1.7.0：实时服务器运行诊断。** 新增全员默认开启的彩色实体数、网络索引实体数、真实模拟均值/峰值、TPS、内存与人数 HUD；`!ze_diag [on|off]` 独立开关、`!ze_diag_status` 查看快照，已加入公开菜单。正式服已停服部署、未代为启动；新资源已提交原工坊 `3818340815`，Steam 下载验证仍返回上一修订，不将上传成功当成客户端下载完成。详情见 `E:\mapper\chana\ZePractice\docs\服务器运行诊断-1.7.0.md`。保留 1.6.0 的个人 Bot、移动 HUD 和资源安装锁修复。

> **2026-10-15 阶段版本 1.4.0：人类指挥、五色标点、R键画线与卢娜模型。** 当时原工坊 `3818340815` 更新为16成员完整资源包（保留原6个界面文件）。使用原 `luna_arms.fbx`，补齐的是独立手模 VMDL 配置，不是另做手模。下指挥保留标点计时，接任共享5点；按住R画线，每段保留10秒、由旧到新消退。详情及真实验收范围见 `E:\mapper\chana\ZePractice\docs\指挥与卢娜资源-1.4.0.md`。

> **2026-10-13 阶段版本 1.3.7：地图枪发光内嵌字段网络同步。** 补齐实际网络成员逐项通知，包含拾取时恢复；服务端状态不再被当作客户端已显示的证明。813 项回归通过，已空服热更新且保留原回合与按钮触发状态，游戏画面仍待进服确认。详见 `E:\mapper\chana\ZePractice\docs\地图枪发光网络同步-1.3.7.md`。

> **2026-10-13 阶段版本 1.3.6：掉落地图枪状态维护与工坊自动更新。** 已安装工坊内容每 60 秒检查并由 Steam 后台更新，不强制换图或结束回合，新地图内容在下次经 RTV／服主授权加载时生效。旧发光验证仅覆盖服务器字段，后续同步修正在 1.3.7。详见 `E:\mapper\chana\ZePractice\docs\掉落发光与工坊自动更新-1.3.6.md`。

> **2026-10-13 阶段版本 1.3.5：个人无扩散预测同步与地图身份补发。** 保持 `!nospread on/off` 无需权限，只影响本人并保留后坐力；补齐个人客户端预测同步，已经完成服务器真实射击的开启/关闭对照。服务器改为 `sv_lan 0`，保持地图 SteamID 开关开启，仅补发本人经引擎授权的真实身份。赞助通道与客户端弹孔仍需真人重连验收，不把空服测试当作恢复证明。详见 `E:\mapper\chana\ZePractice\docs\无扩散与地图身份修复-1.3.5.md`。

> **2026-10-12 阶段版本 1.3.4：地图神器刀与个人无扩散。** 僵尸默认允许地图神器刀，禁枪与禁买枪不变；关闭自杀惩罚和自动踢人。详见 `E:\mapper\chana\ZePractice\docs\地图刀与个人无扩散-1.3.4.md`。

> **2026-10-12 阶段版本 1.3.3：全服默认免疫闪光与烟雾。** 所有玩家自动免疫原生闪白与烟雾遮挡，换队、复活、感染和重新加入后持续生效，无需权限或开启指令。保留特殊手雷的屏障/黑洞、地图粒子与地图脚本；不需要更新工坊资源。详见 `E:\mapper\chana\ZePractice\docs\默认闪光烟雾免疫-1.3.3.md`。

> **2026-10-12 阶段版本 1.3.2：移动与换队重置。** 精灵不再覆盖已开启的个人穿墙；修复持空格连跳；T 只可持允许的刀，禁止枪械及人类神器。所有玩家无限制换队，旧角色的神器标签、实体名和附着状态在复活前清理，不强制丢神器刀、不修改地图脚本。详见 `E:\mapper\chana\ZePractice\docs\移动与僵尸武器修复-1.3.2.md`。

> **2026-10-12 资源分发迁移**：两个旧界面 VPK 已合并为 `saibochana_Server.vpk`，现用 Workshop ID `3818340815`；普通客户端不再使用全局 gameinfo 挂载。专服 MultiAddonManager 已加载验证。发布/更新/真人验收见 `E:\mapper\chana\ZePractice\docs\工坊统一资源包-20261012.md`，不要按旧说明重新开启全局无线电挂载。

> **工坊启动修复**：此前包内的原生 `hudradio` 布局违反 addon 的 `custom_game` 限制，现已从包、发布目录和源目录移除。当时的修复包为6文件（1.4.0现为16文件），右侧显示动态选项，官方无线电接收数字键，9关闭面板；不要恢复旧8文件包。

> **2026-10-12 阶段版本 1.3.0：换图投票与地图标记。** 聊天 `host_workshop_map +ID` 只添加候选，`!ze_maps` 选图，`rtv` 严格超过80%才换图；只有服主可立即换图。地图实体按钮显示绿色鸡，地图枪落地显示青绿色轮廓。详见 `E:\mapper\chana\ZePractice\docs\换图投票与地图标记-1.3.0.md`。1.2.0 僵尸和全员自助玩法保持；下方 1.1.3 为上一阶段记录。

> 2026-10-12：ZePractice 1.1.3 数字菜单与冻结修复收尾。F9/Z 主菜单，F10 激活聊天指令打开的数字菜单，7/8 翻页、9 返回、0 关闭。当前安装范围、验证和回滚见 `E:\mapper\chana\ZePractice\docs\无线电菜单与冻结修复验收.md`。

**这是基于现有 CS2Fixes 的自建 ZE / 跑图服，不是 FYS 线上服务器插件的原版复刻。**从你的 `Hoshino486/cs2` fork 导入地图参数、Boss 与翻译配置，配合 CounterStrikeSharp、StripperCS2 和本项目的 `ZePractice`；未导入奖励、积分、商店、管理员名单和私有地图本体。本人创建的功能（职业/特殊手雷等）不表示等同于 FYS 私有服务端实现。FYS 原始地图列表不提供地图文件及多数地图的 Workshop ID，不能替代地图下载/分发。当前这张图使用单独确认的 Workshop ID `3817448617`，对应条目标题 `ze_forhidden_slicoo_cn`；地图与 FYS 配置是两条独立更新链。

## 安装位置与版本

- 项目源码：`D:\34184\Documents\ChatGPT\CS2脚本\ZePractice\`。
- 独立入口：`E:\mapper\chana\ZePractice\run.bat`，调用 `E:\mapper\chana\ZePractice\tools\Start-ZePractice.ps1`。**不是** `E:\mapper\chana\run.bat`（Steam 更新），也不是 `E:\mapper\chana\steamapps\common\Counter-Strike Global Offensive\game\bin\win64\run.bat`（旧服务器启动）。
- 原服游戏根：`E:\mapper\chana\steamapps\common\Counter-Strike Global Offensive\game\`。本项目不改动旧两份 `run.bat`。
- 服务端安装了 Metamod 2.0.0-dev+1480、CS2Fixes `2.0-saibochana.20261015`；本方案新增 CounterStrikeSharp 1.0.376（含 .NET 10 运行环境）、StripperCS2 v2.0.1、`ZePractice.dll`。
- 插件 DLL：`E:\mapper\chana\steamapps\common\Counter-Strike Global Offensive\game\csgo\addons\counterstrikesharp\plugins\ZePractice\ZePractice.dll`。
- 插件配置：`E:\mapper\chana\steamapps\common\Counter-Strike Global Offensive\game\csgo\addons\counterstrikesharp\configs\plugins\ZePractice\ZePractice.json`；服主 SteamID64 配在 `OwnerSteamIds`。**FYS 公共仓库不用于自动授予任何用户管理员权限。**
- 独立启动参数：`E:\mapper\chana\ZePractice\server-settings.json`，默认只绑定 `127.0.0.1:27015`；本图保留 `"StartupMap": "ze_forhidden_slicoo_cn"` 与字符串 `"StartupWorkshopId": "3817448617"`，通过 Workshop 下载已发布地图。该文件现在是 RCON 密码唯一配置源；启动器生成独立私有 `rcon.generated.cfg`，服务端与工具使用同一值，见教程 9.4。勿复制密码到截图/聊天。
- 独立服务器 cfg：`E:\mapper\chana\steamapps\common\Counter-Strike Global Offensive\game\csgo\cfg\ze-practice\server.cfg`；启动器通过 `+servercfgfile ze-practice/server.cfg` 保证每次换图都使用它，不再自动加载旧的通用 `server.cfg`。当前默认**练习模式**，多人 ZE 需要结合实际玩家数和地图资源验证。
- 当前无奖励配置快照：`E:\mapper\chana\steamapps\common\Counter-Strike Global Offensive\game\csgo\addons\counterstrikesharp\plugins\ZePractice\Hoshino486-cs2-20261011-122010\`；导入清单 `import-manifest.json` 写明来源 `Hoshino486/cs2`、710 份地图参数、316 份 Boss、677 份翻译与 `rewardsIncluded=false`。更新后名称会改变，以插件配置 `FysRoot` 为准。
- 诊断、构建备份和运行日志：`E:\mapper\chana\临时文件夹\ze-practice-build-20261011\`，每次启动日志位于其 `runtime\<时间>\console.log`，运行记录也可查 `E:\mapper\chana\ZePractice\last-run.json`。后者只记录启动/退出状态，不要把日志中可能含有的信息公开分享。

## 启动与资源更新

1. 如需更新游戏文件，在**没有运行本服进程时**使用原有 SteamCMD 更新脚本：`E:\mapper\chana\run.bat`。Steam 校验可能恢复官方 `gameinfo.gi`；独立启动器会在校验当前 `steam.inf` 版本后仅补回 Metamod 和本服挂载的搜索路径，版本不匹配时停止并给出原因，**不要把以前版本的 `gameinfo.gi` 整份拷回去**。
2. 启动前先执行安全预检（不会启动游戏进程）：

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File "E:\mapper\chana\ZePractice\tools\Start-ZePractice.ps1" -PrepareOnly
   ```

3. 双击 `E:\mapper\chana\ZePractice\run.bat` 启动；它先加载内置 `de_dust2`，等待地图 active 且 RCON 就绪，再自动发送 `host_workshop_map 3817448617`。不需要手动换图；等最终 `ze_forhidden_slicoo_cn` 进入 active 后，在客户端控制台输入 `connect 127.0.0.1:27015`。服务器控制台应显示文字，日志保存在上述诊断目录。默认只允许本机连接；不要同时启动旧版专服到同一端口，启动器也会阻止重复启动同一 `cs2.exe`。
4. 从 **Hoshino486/cs2 的 master 分支更新配置**（不更新 CS2 游戏）的脚本在 `E:\mapper\chana\ZePractice\tools\Update-Fys.ps1`。先运行 `-CheckOnly` 检查导入与配置结构；此步骤需要网络但不替换已安装快照。非检查模式会部署新配置快照、转换的 Stripper 数据并更新 FYS 路径，按脚本要求先自行正常关闭本服；更新后再正常开服以加载新图实体。FYS 地图配置更新≠地图本体安装。GitHub 目录/格式变动可能导致导入被拒绝；以脚本的实际验证输出为准。

## Workshop 地图自动下载（当前联网方式）

本图使用 **`3817448617` → `ze_forhidden_slicoo_cn`**。应让服务器通过原生 Workshop 路径加载已经发布的版本，玩家连接时由 CS2/Steam 根据当前地图的 Workshop 标识检查并下载所需地图，而不是服务器把 D/E 盘松散文件直接传给玩家。

已经正常加载地图的服务器，需要手动换到本图时，在服务端控制台输入：

```text
host_workshop_map 3817448617
```

**当前启动器不是直接用 `+host_workshop_map` 冷启动。** 本机实测只给该命令时停在 `Idle console`；先加载内置 `de_dust2` 后，Workshop 下载、挂载和目标地图才正常进行。因此正式入口自动执行两段：先以 `+map de_dust2` 启动，在最多 90 秒内等待引导地图 active / RCON 可用，再经 RCON 发送上面的 Workshop 命令。90 秒是引导就绪期限，不是地图下载完成期限。命令行只出现 `+map de_dust2` 属于正常现象，最终状态看日志和 `last-run.json`；不要在启动命令后再叠加本图的普通 `+map`。若引导超时，先检查现有进程、日志与两处 RCON 密码，不要立即双击再开第二个服务器。

仍可双击 `run.bat` 自动完成全部步骤。临时 `-Map ze_forhidden_slicoo_cn` 与默认图同名时继续走 Workshop；只传一个不同名字的 `-Map` 则走本地分支；明确测试未发布版本时才用 `-LocalMap` 强制本地。参数细节见教程 6.2。

CS2Fixes 管理员可在聊天使用 `!map 3817448617`，控制台是 `c_map 3817448617`，需要其独立地图权限 `g` 或 `z`，不是只有 ZePractice 的 `!zeadmin` 权限就足够。

CS2Fixes 地图列表路径：

```text
E:\mapper\chana\steamapps\common\Counter-Strike Global Offensive\game\csgo\addons\cs2fixes\configs\maplist.jsonc
```

本图在 `Maps` 下的条目需保留已有设置并带上**数字**字段 `"workshop_id": 3817448617`；不是 `publishedfileid`、也不是任意相近地图的 ID。本轮 `!ze_map` 源码已补读本机 CS2Fixes 地图列表的 ID，部署对应插件后，`!ze_map ze_forhidden_slicoo_cn` 会保留 Workshop 身份并走 `host_workshop_map`；未配置 ID 的普通本地图仍走本地分支。

玩家只需正常从 Steam 启动 CS2 并连接服务器；本机仍为 `connect 127.0.0.1:27015`，朋友按教程第 9 节使用实际可达的服务器地址。第一次下载可能需要等待。条目必须对玩家可访问、Steam 下载链路可用，服务器也要先成功取得该 Workshop 版本；此流程不承诺在网络失败、条目不可见或被删除时仍能下载。

**资源完整性限制**：主地图、skybox、模型、材质、声音、脚本应包含在已发布的同一个 Workshop 项里。若仍依赖未发布的本地 addon 文件，其他玩家不会自动获得；若依赖另一个独立 Workshop 资源 addon，需另行配置其下载/挂载，必要时使用 MultiAddonManager 等多 addon 分发方案。单一完整主地图不因“自动下载”就必须额外安装 MultiAddonManager。

重新编译只更新本地文件，不会自动更新 Workshop。需要通过制图工具把正确资源更新到同一发布条目，服务器再载入更新后的 Workshop 版本；不要本地使用新 VPK、联网仍分发旧条目。2026-10-11 已实测先加载 `de_dust2` 后，服务端下载并挂载 `3817448617`，日志出现 `Loading custom game "3817448617" with map "ze_forhidden_slicoo_cn"` 且目标图进入 active；这不等于真人客户端已下载并连接成功，也不等于所有关卡验收完成。本机保持 `127.0.0.1` 与 `sv_lan 1` 即完成此服务端验证，无需仅为这条 Workshop 流程改成 `sv_lan 0`，也没有变成公网服。完整步骤、离线开发与验证边界见教程第 6 章。

## 仓库 fork 与实际上线内容

已确认 `Hoshino486` 账号拥有这五个 fork：`Hoshino486/cs2`（上游 `fyscs/cs2`）、`Hoshino486/CS2Fixes`（上游 `Source2ZE/CS2Fixes`）、`Hoshino486/CounterStrikeSharp`（上游 `roflmuffin/CounterStrikeSharp`）、`Hoshino486/StripperCS2`（上游 `Source2ZE/StripperCS2`）、`Hoshino486/metamod-source`（上游 `alliedmodders/metamod-source`）。四个依赖的 fork 只是源码备份：现有服务器二进制仍为已验证的发行版，**不会自动切成你 fork 的代码编译版**。当前更新脚本只从你的 `Hoshino486/cs2` 获取数据，不再从 `fyscs/cs2` 直接下载。地图数据保留 FYS 原作者版权及 CC BY-NC-SA 4.0 署名。

本地图 `ze_forhidden_slicoo_cn` 会映射到 fork 中的 `ze_forhidden_slicoo_cs2` 配置；2026-10-11 实测：**4 条 Boss 规则（当时实体已匹配 2 条）、148 条翻译规则、10 条直接地图参数、19 条托管参数**。原 FYS Boss `Proxy=true` 涉及未公开的私有协议，不能据配置推导生命值；`vip_map_extend_times` 和 `ze_infect_sort_by_immunity` 尚无对应实现；当前已按引擎要求设置 `mp_warmuptime=5` 并在练习模式主动结束热身。FYS 仓库**没有**单图神器 HammerID 文件，本地图 `!items` 会提示 0 条，直到提供实际实体 ID；请勿把空规则说成已可用。地图本体加载成功，但原编译包当前还输出部分实体模型缺失（如 `drg7_hp_zone`、`nuke_zone`、`lfkb_hp_zone`）和部分光照资源错误；这些属于地图包，尚未修复或证明完整 Boss 关卡正常。真人进图、道具使用、菜单/翻译渲染尚未完成客户端验收。

前一版本为测试未发布本地 VPK，使用过 `+map ze_forhidden_slicoo_cn nomapvalidation true`。这条本地加载路径不会因为文件名相同就让其他客户端自动下载地图，**不能代替当前“内置地图引导 → RCON host_workshop_map 3817448617”的两段启动**。本机 `同步本机地图.bat` 只服务于明确选择的本地开发测试，不是朋友联网进服的前置步骤；不应要求每位玩家手工复制 VPK、修改 `gameinfo.gi` 或 Steam 启动项。不能把 `-dedicated` 专服控制台当成图形客户端窗口。
## 玩家指令（聊天框前缀 `!`；控制台使用对应 `css_`）

| 分类 | 命令 | 功能与限制 |
|---|---|---|
| 主菜单 | `!zemenu`、`!zhelp`、`!ze_status` | 中文菜单、帮助及地图/配置状态 |
| 地图 | `!ze_maps [ID筛选]`、`rtv`、`!items` | 候选选图与严格超过80%的 RTV；`!items` 显示真实规则绑定的神器持有者/冷却 |
| 补给 | `!zeweapon`、`!zebuy <名称>` | 武器、原生/特殊手雷的本地补给；实际购买受本图设置限制 |
| 职业/技能 | `!zeclass`、`!zskill` | 本地 ZE 职业菜单与已选职业的本地技能；非 FYS 私有职业数据 |
| 跑图 | `!ze_god`、`!ze_noclip`、`!ze_save`、`!ze_tp`、`!ze_reset`、`!ze_respawn`、`!ze_goto <玩家>`、`!zerefill` | 仅练习模式；管理员可按本服设置限制普通玩家。新回合/切图清理检查点，多人 ZE 会撤销个人辅助 |
| 身份 | `!ze_whoami` | 显示本人 SteamID64 和当前管理权限；不要把服务器秘密填入命令 |

地图部分功能仍依赖地图配置和现有 CS2Fixes。1.3.0 接管候选、选图与 RTV，关闭原生投票管理器；`ztele` 等其他玩法不变。原生 EntWatch 继续关闭，由本项目读取真实神器规则；地图枪原生 Glow 提示不依赖原生 EntWatch。

## 管理员指令

`!zeadmin` 显示中文菜单；`!ze_adminhelp` 查看服主配置说明。脚本中所有 `!ze_` 管理指令都检查插件服主 SteamID64 或 CSS `@css/root` 等主插件权限，且玩家管理二次检查对象有效性、重连后用户 ID 与管理员豁免；服务器控制台具管理权限。

| 指令 | 说明 |
|---|---|
| `!ze_mode practice` / `!ze_mode ze` | 切换练习/多人 ZE，清理练习辅助并请求重启回合；多人 ZE 的感染需实测 |
| `!ze_restart`、`!ze_reload` | 重启当前回合、重载本图 FYS 配置（实体修补需重载地图） |
| `!ze_map <完整地图名>` | 仅能切换本机已安装的地图；菜单操作额外确认 |
| `!ze_respawn <玩家>`、`!ze_bring <玩家>`、`!ze_slay <玩家>`、`!ze_kick <玩家>`、`!ze_team <玩家> ct|t|spec` | 目标用 `#userid` / SteamID64 / 唯一名称，拒绝名称歧义 |
| `!ze_ban <玩家> [分钟] [原因]`、`!ze_mute`、`!ze_gag` | 默认期限分别为 60 / 30 / 30 分钟；`0` 为永久，最多 525600 分钟；操作单独保存在本插件 `data\punishments.json`，不覆写 CS2Fixes |
| `!ze_unban <SteamID64>`、`!ze_unmute <玩家或SteamID64>`、`!ze_ungag <玩家或SteamID64>`、`!ze_punishments` | 仅解除本插件处罚；其他插件中的封禁仍需另行管理 |
| `!itemwatch` | 本地图神器绑定诊断，只能由管理员查看 |

`!ze_whoami` 可获 SteamID64，只有自己认可的 SteamID64 才能手动录入 `OwnerSteamIds`；默认仅 loopback 玩家可用 `TrustLoopbackAdmin` 获取本机临时管理权限。把服务器开放到 LAN/公网前按需要改 `BindAddress`、网络端口、防火墙与权限配置，检查 RCON 密码强度并确认地图资源及多人 ZE 运行情况；**不要把配置中 RCON 密码贴给别人**。

## 构建、检查与回滚

本地构建工具：Windows + Visual Studio 2022、.NET SDK 10。仅编译项目不接触已安装服务器：

```powershell
 dotnet build "D:\34184\Documents\ChatGPT\CS2脚本\ZePractice\ZePractice.csproj" -c Release
```

插件源码在 `D:\34184\Documents\ChatGPT\CS2脚本\ZePractice\src`，构建输出 `D:\34184\Documents\ChatGPT\CS2脚本\ZePractice\bin\Release\net10.0\ZePractice.dll`。本轮已进行编译和隔离测试；**不因此声称游戏内功能已通过实机全量测试**。如需更新服务器 DLL，先自己正常停服、确认备份，再按已安装插件目录部署；不替换 Steam 原生程序 DLL。

已部署文件逐文件、原始哈希、新哈希及原文件备份清单位于：

```text
E:\mapper\chana\临时文件夹\ze-practice-build-20261011\backup\installed-files.json
```

`D:\34184\Documents\ChatGPT\CS2脚本\ZePractice\tools\Rollback-ZePractice.ps1` **默认只预览**。例如：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "D:\34184\Documents\ChatGPT\CS2脚本\ZePractice\tools\Rollback-ZePractice.ps1"
```

默认 `-Scope Plugin` 只处理独立入口、此项目插件/配置、独立 cfg，保留共享的 CounterStrikeSharp / StripperCS2、地图挂载文件、CS2Fixes 和其他功能。先在预览中检查差异、备份完整性和变更数量；预览若出现 `ModifiedAfterDeployment`，表示文件在部署后被编辑：默认不会覆盖这些内容。如确认要保留编辑并只回滚其余文件，可用 `-Apply -KeepModified`，随后检查保留的配置是否仍引用插件。

管理员确认不再运行本服、预览没有任何 `ModifiedAfterDeployment`、确实要回滚时，才显式添加 `-Apply`；`-Scope AllTracked -Apply` 会尝试恢复/移除清单里整个本项目附加组件和地图资产，还会拒绝与其他 CSS 插件共用共享框架的情形。两种范围都遵守路径白名单、原/新 SHA256 对照；操作前逐文件另存备份到诊断目录 `rollback\<时间>\before`，并保留逐项日志。脚本**不递归删除整个目录**，不动清单外文件、运行数据与 `gameinfo.gi`。它也不会为你关闭服务器。

