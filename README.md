# ZePractice

**CS2 ZE 跑图服插件** —— 基于 CounterStrikeSharp 的自建服务器玩法层。

练习模式、人类指挥、换图投票、工坊自动更新、Bot 陪练、神器监听、运行诊断 HUD，都在这一个 DLL 里。

> 这不是 FYS 线上服插件的复刻。地图参数、Boss 与翻译配置来自 `Hoshino486/cs2` 的公开数据；奖励、积分、商店、管理员名单和私有地图本体都没有包含。

## 功能

| 模块 | 内容 |
|---|---|
| **练习跑图** | `!ze_god`、`!ze_noclip`、`!ze_save`、`!ze_tp`、`!ze_reset`、`!ze_respawn`、`!ze_goto`、`!zerefill`；存档点按地图隔离，切回合/换图自动清理 |
| **人类指挥** | 五色标点带计时、按住 R 画线（每段保留 10 秒由旧到新消退）、接任共享标点、指挥官高亮与倒计时 |
| **换图投票** | `!ze_maps` 候选列表、`rtv` 严格超过 80% 才换图、服主可立即换图；接管原生投票并关闭它 |
| **工坊资源** | 已安装工坊内容每 60 秒后台检查更新，不强制换图；新内容在下次加载时生效 |
| **练习 Bot** | 个人 Bot 陪练、传送编号选择、队伍选择 |
| **神器监听** | 取代原生 EntWatch，读取真实规则并显示持有者与冷却（`!items`） |
| **地图标记** | 实体按钮显示绿色鸡、落地地图枪显示青绿轮廓、地图枪发光字段网络同步 |
| **诊断 HUD** | 彩色实体数、网络索引实体数、模拟均值/峰值、TPS、内存与人数；`!ze_diag`、`!ze_diag_status` |
| **权限** | 服主 SteamID64 + CSS `@css/root`；处罚（ban/mute/gag）单独保存，不覆写 CS2Fixes |
| **默认玩法** | 全员闪光/烟雾免疫、个人无扩散（保留后坐力）、T 只可持允许的刀、无限换队 |
| **署名** | 进图与首次出生时在屏幕中央显示「跑图插件制作：赛博刹那」，聊天同步一条提示 |

## 署名

插件加载后会自动署名，不需要玩家敲任何命令：

- **地图加载后**（等 4 秒避开开局提示）屏幕中央显示 8 秒：`跑图插件制作` / `赛博刹那` / `ZePractice v1.7.27`
- **每位玩家首次出生**再显示 4 秒，晚进服的人也能看到
- 同时发一条聊天提示

想换文字或时长，改 `ZePractice.json`：

```jsonc
"Watermark": {
  "Enabled": true,
  "Headline": "跑图插件制作",
  "Author": "赛博刹那",
  "ShowInChat": true,
  "OnEverySpawn": false,
  "MapStartDelaySeconds": 4,
  "MapStartSeconds": 8,
  "SpawnSeconds": 4
}
```

管理员随时可以用 `!ze_watermark` 再放一次。

## 依赖

| 组件 | 版本 |
|---|---|
| CounterStrikeSharp | 1.0.376（含 .NET 10 运行时） |
| Metamod:Source | 2.0.0-dev+1480 |
| CS2Fixes | 2.0（地图参数与实体修补） |
| StripperCS2 | v2.0.1（部分地图实体修改） |
| .NET SDK | 10.0（仅编译需要） |

## 构建

```powershell
dotnet build ZePractice.csproj -c Release -p:CssApiDir="<CS2>\game\csgo\addons\counterstrikesharp\api"
```

输出 `bin\Release\net10.0\ZePractice.dll`。

`CssApiDir` 默认指向开发机的服务器目录，换机器时用参数覆盖。项目不打包 CSS 的 API DLL，直接从你安装的 CounterStrikeSharp 引用。

## 安装

1. 把 `ZePractice.dll` 放到 `game\csgo\addons\counterstrikesharp\plugins\ZePractice\`
2. 首次启动会生成 `configs\plugins\ZePractice\ZePractice.json`
3. 把自己的 SteamID64 填进 `OwnerSteamIds`
4. 重启服务器，聊天框输入 `!zemenu` 验证

SteamID64 可以用 `!ze_whoami` 查到。

## 常用指令

玩家（聊天框前缀 `!`）：

| 指令 | 说明 |
|---|---|
| `!zemenu` / `!zhelp` / `!ze_status` | 主菜单、帮助、地图与配置状态 |
| `!ze_maps [筛选]` / `rtv` | 选图与换图投票 |
| `!items` | 当前地图神器持有者与冷却 |
| `!zeweapon` / `!zebuy <名称>` | 补给武器与手雷 |
| `!ze_god` / `!ze_noclip` / `!ze_save` / `!ze_tp` | 练习模式专用 |
| `!ze_whoami` | 本人 SteamID64 与权限 |
| `!ze_watermark` | 重新显示插件署名 |

管理员（`!zeadmin` 打开菜单）：

| 指令 | 说明 |
|---|---|
| `!ze_mode practice` / `!ze_mode ze` | 切换练习 / 多人 ZE |
| `!ze_map <地图名>` | 切换到本机已安装地图 |
| `!ze_respawn` / `!ze_bring` / `!ze_slay` / `!ze_kick` / `!ze_team` | 玩家管理 |
| `!ze_ban` / `!ze_mute` / `!ze_gag` | 处罚，默认 60 / 30 / 30 分钟，`0` 为永久 |
| `!itemwatch` | 神器绑定诊断 |

完整指令与配置说明见 [服务器使用教程.md](./服务器使用教程.md)。

## 目录

```text
src/        插件源码（CS2 玩法、菜单、HUD、工坊、诊断）
config/     默认配置示例
panorama/   界面资源
tools/      启动、预检、更新与回滚脚本
docs/       按版本归档的改动记录
tests/      测试与验证脚本
```

## 说明

- 仓库不含游戏资源、地图 VPK 或工坊模型。界面与资源包另行发布在 Steam 创意工坊。
- 地图数据保留原作者版权；本项目只提供服务器玩法层。
- 本插件按现状提供，未在公网大流量环境下验证。开服前请自行检查 RCON 密码强度、绑定地址与防火墙设置。
