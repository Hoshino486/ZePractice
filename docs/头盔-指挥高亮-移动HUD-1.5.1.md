# 头盔、指挥人物高亮、个人移动 HUD — 1.5.1

> **2026-10-16 后续修复说明：** 下文是 1.5.1 当时的验收记录。资源现已通过并分发；实际查明服务器安装提交被旧 VPK 挂载锁阻塞，另有 HUD 热加载与显示状态问题。1.6.0 已在正式服修复、完成 Steam 安装提交，HUD 资源状态 `Ready=true`。本轮没有再上传资源或等待审核；详情见 `E:\mapper\chana\ZePractice\docs\个人练习Bot-移动HUD-1.6.0.md`。

## 本轮修改

### 僵尸护甲包含头盔

原实现只设置 `CCSPlayerPawn.ArmorValue`，没有设置引擎判断头部防护使用的 `CCSPlayer_ItemServices.HasHelmet`。现在僵尸初始化与 0.1 秒维护同时设置真正的头盔，通知网络变化，并同步控制器的 `PawnHasHelmet` / `PawnArmor` 摘要。

- T 阵营出生、CT 切 T、人类死亡转僵尸都获得 2500 护甲和头盔。
- 头盔标志被覆盖后自动补回；不是每次受伤直接补满护甲。原受伤后延迟 5 秒再回血回甲规则不变。
- 人类不强制赋予头盔，不改变人类护甲指令、默认装备和不回血的规则。
- 本轮没有全局清除瞄准后坐力或地图镜头震动。已验证真实头盔字段与换阵营流程；具体武器爆头受击画面仍需真人客户端确认。

源码：`E:\mapper\chana\ZePractice\src\PublicGameplayService.cs`。

### 指挥人物高亮

原实现只向玩家 pawn 写入 Glow 字段，服务器读回成功不代表客户端渲染人物轮廓。现在使用 CS2Fixes 的两层动态模型跟随方式：

```text
玩家 pawn → 隐藏 skeleton relay → 发光 outline
```

- 两个代理都使用指挥当前人物模型；人物原材质与 pawn 原有 Glow 不作改写。
- 金黄色轮廓、`glowtype=3 / glowteam=-1 / glowrange=0`；人物与两个代理一起强制传输，不按普通可见区域或距离剔除。
- 两个代理均无碰撞；轮廓参数被覆盖时恢复，代理被删除或人物模型变化时重建。
- 下指挥、死亡、换阵营、回合重开、换图和卸载清理人物高亮。
- 下指挥仍保留标点和画线的剩余时间；五色标点、续时、FIFO 和旧到新画线过期规则不变。

源码：`E:\mapper\chana\ZePractice\src\CommanderLeaderGlow.cs`、`CommanderService.cs`。

### 个人移动 HUD

默认开启，人类和僵尸均可使用，不要求权限。位置为底部中央、阵营徽章上方，与用户红框对应；不占用准星、不抢中央地图字幕、不接管键盘或鼠标。

显示：

```text
地速 250 u/s    垂速 +302 u/s
跳高 56.8 u · 跳跃
```

- 地速是 XY 水平速度，垂速带正负号，单位 `u/s` 为游戏单位/秒。
- 跳高是脚底相对起跳位置的最大上升高度，单位 `u`，不是地图海拔。落地保留上一跳结果，新跳跃开始重新统计。
- 每个游戏 tick 采样，文字最多每 0.1 秒刷新；文字没变化时不重复写网络属性。
- 传送、换 pawn、死亡、梯子/游泳/飞行、异常采样间隔和非有限输入均防止污染跳高记录。
- 每人独立 `custom_hud_layout`，仅向本人传输。仍使用现有 `ZeHudText` Windows HUD 兼容接口。

公开指令已加入原 F9 / `!ze` 菜单：

| 指令 | 作用 |
| --- | --- |
| `!ze_speed` | 切换本人移动 HUD |
| `!ze_speed on` | 开启 |
| `!ze_speed off` | 关闭，不影响别人 |
| `!ze_movement_status` | 本人读数及资源状态 |

源码：`E:\mapper\chana\ZePractice\src\MovementHudService.cs`、`MovementTelemetryLogic.cs`。
布局：`E:\mapper\chana\ZePractice\panorama\layout\custom_game\zepractice\movement.xml`。
样式：`E:\mapper\chana\ZePractice\panorama\styles\custom_game\zepractice\movement.css`。

## 资源更新与发布

仍更新 **3818340815 / saibochana_Server**，没有创建新条目。新包共 31 项：上一轮 29 项中仅版本文本变化，新增 HUD 布局和样式两项；卢娜人物、手模、标点和十个发光倒计时数字逐文件保持不变。

VPK SHA256：`5A367D80B2626AFF8CFC22ED89E395E206776F13313104353482532B17E1F49B`。

2026-10-15 23:55:47（北京时间）Steam 上传返回：

```text
SUBMIT item=3818340815 result=1 needsAgreement=0
ManifestID 7625509608324204902
time_updated=1792079747
```

标题、封面、描述、标签、可见性保持不变。首个上传请求成功，无重复提交；后续仅给发布工具补充回调轮询与进度诊断，不再重复上传。

**上传完成不等于新客户端资源已分发。** 正式服恢复检查时，实际下载缓存仍为旧版，HUD 门控状态为 `awaiting-workshop-required-assets`。不手动覆盖 Steam 下载缓存伪装更新，不永久安装散装资源。服务器已看到新的工坊更新时间，继续每 60 秒检查并按下载状态重试；实际下载成功后，下次正常地图加载自动启用新 HUD 和立体倒计时，不自动换图、不结束回合、不强制重启。

编译命令：

```powershell
dotnet build 'E:\mapper\chana\ZePractice\ZePractice.csproj' -c Release
& 'E:\mapper\chana\ZePractice\tools\Build-ZeWorkshopResources.ps1' `
  -ExtraAssetsRoot 'E:\mapper\chana\ZePractice\artifacts\helmet-leader-movement-20261015\extra-assets' `
  -OutputRoot 'E:\mapper\chana\ZePractice\artifacts\helmet-leader-movement-20261015\bundle'
```

## 验证结果

证据目录：`E:\mapper\chana\ZePractice\artifacts\helmet-leader-movement-20261015`。

- 主插件及原生探针均编译通过，0 警告、0 错误。
- **21 组离线测试，1414 项检查通过**。附加地图参数测试使用正式配置快照，解析 710 个 CFG、21965 行设置；过时临时目录的首次失败保存在日志中，改用实际目录后重跑通过。
- 神器解析器的 62 项是合成 schema/state 测试；当前 FYS 快照无公开 EntWatch 目录，不声称通过了所有地图神器测试。
- **49/49** 纯工坊旧包冷启动指挥回归：真实模型句柄、双层跟随链、无限范围字段、无碰撞、覆盖修复、卸任删除、交接、标点、画线和第三人称。
- **17/17** 纯工坊旧包冷启动本轮回归：头盔覆盖修复、控制器摘要、人类不强制头盔、指挥更换模型/删除代理后重建、各类转僵尸及旧资源门控。
- **20/20** 新 HUD 候选资源隔离回归：在 27025 / 0 真人下临时放入两项编译资源；验证每人独立布局、真实文本写入、单人关闭隔离和相同头盔/高亮流程。此测试明确不是工坊下载验收。
- 测试后已按 SHA256 删除两项临时散装资源；测试插件已移出正式插件扫描目录。
- 原生断言验证服务器实体、字段、资源和生命周期，**不等于真人画面验收**。HUD 实际排版、爆头屏幕表现、远距离/隔墙人物高亮仍需进服确认；没有伪造截图。

## 正式部署

正式服已恢复到 **27015**，原地图 **ze_forhidden_slicoo_cn / 3817448617**，ZePractice **1.5.1**。RCON 确认 CS2Fixes、CounterStrikeSharp、MultiAddonManager 和 Stripper 正常列出；无测试插件。

保留并复核：35000 僵尸血量、2500 护甲、99999 金钱、99 备弹、僵尸地图神器刀、全服闪光/烟雾免疫、无跳跃疲劳、默认空速 `sv_airaccelerate=12`、`cs2f_map_steamids_enable=true`、不自动结束回合。

没有改地图脚本、人物原素材、RCON 凭据、管理员身份、正式启动地图或客户端全局 gameinfo。主维护源码与本次编译源码按哈希同步。

## 回退

在无真人时正常停止正式服，将证据目录 `baseline\deployed` 的 DLL/PDB/deps 恢复到正式 ZePractice 插件目录，即可回到 **1.5.0**。本轮未修改运行配置；原 `baseline\ZePractice.json` 仅用于比对，不要覆盖之后的人工配置。资源更新为向后兼容增量，不因插件回退自动重新上传旧工坊包。
