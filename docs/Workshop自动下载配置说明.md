# Workshop 自动下载配置说明

更新日期：2026-10-11。适用于当前 E:\mapper\chana\ZePractice 跑图服务器。

## 现在怎么用

1. 如果服务器已经在运行，不要重复启动。正常开服仍双击 `E:\mapper\chana\ZePractice\run.bat`。
2. 启动器先加载官方 `de_dust2` 完成 Steam/GameServer 初始化，再自动执行 `host_workshop_map 3817448617`。不必手动输这两条命令。
3. 等目标 `ze_forhidden_slicoo_cn` 加载完成后，在本机正常 CS2 客户端控制台执行 `connect 127.0.0.1:27015`。
4. 玩家缺少该 Workshop 地图时，应由游戏/Steam 的原生 Workshop 流程取得地图。不是弹出第三方网页下载器，也不需要每个人修改 gameinfo.gi 或复制 VPK。
5. 本轮已经实测服务器下载、挂载、地图 active、A2S 查询以及管理员切图。真人客户端从零下载并进入地图尚未实测，不把服务端成功当成完整客户端验收。

当前仍绑定 127.0.0.1，保持 sv_lan 1。朋友远程进入还需要单独配置地址/网络可达性；本轮没有开放公网、改防火墙或替换密码。

## 地图发布项

- Workshop ID：`3817448617`
- 条目标题与目标地图名：`ze_forhidden_slicoo_cn`
- Steam 官方元数据本轮返回：公开 CS2 项目，AppID 730，2026-10-11 16:04:17 +08 更新。
- 所下载发布包共 7 个 VPK，631,994,815 bytes；加发布数据文件与接口公布大小一致。约 603 MiB 的磁盘文件大小不是压缩网络传输流量。
- 外包中包含主图/skybox 内嵌 VPK、18 个编译 JS、模型/材质/粒子/声音等。共 2,624 个外层与内嵌条目 CRC32 校验通过。没有发现必须追加另一 Workshop ID 的证据；这不等于所有关卡逻辑均已完整验收。

## 修改了哪些文件

### 1. 启动设置

`E:\mapper\chana\ZePractice\server-settings.json`

保留原有 GameRoot、BindAddress、Port、AssetMounts、DiagnosticsRoot、RconPassword，只增加：

```json
"StartupWorkshopId": "3817448617"
```

`StartupMap` 保持 `ze_forhidden_slicoo_cn`。不要把该 ID 改成不对应这张图的别的项目，也不要把 RCON 密码发送给别人。

### 2. 启动器

`E:\mapper\chana\ZePractice\tools\Start-ZePractice.ps1`

- 新增 Workshop ID 校验和 `-WorkshopId` / `-LocalMap` 参数。
- Workshop 模式不再按本地 addon 复制地图，不使用本地 `-addon`，仅撤下本启动器 AssetMounts 指定的旧开发 addon 的 Game 搜索路径。
- 保留 Metamod 搜索路径、官方 gameinfo 的其他内容及版本绑定恢复机制。
- 先启动 `de_dust2`；最多等待 90 秒让引导地图/RCON就绪，然后发送 `host_workshop_map`。90秒不是整个 Workshop 下载的期限。
- `last-run.json` 新记录 WorkshopId、BootstrapMap、MapLoadCommand、WorkshopRequestedAt。
- 验证缓存只在目标 Workshop 图真正 active 后保存，不将引导图 active 当成完成。
- 原 UTF-8 JSON读取、PowerShell5兼容、原生控制台显示、日志机制保留。

### 3. CS2Fixes 地图列表

`E:\mapper\chana\steamapps\common\Counter-Strike Global Offensive\game\csgo\addons\cs2fixes\configs\maplist.jsonc`

在 `Maps.ze_forhidden_slicoo_cn` 对象内增加数字（不是带引号的字符串）：

```json
"workshop_id": 3817448617
```

其他地图条目未删除。CS2Fixes 和本服管理菜单据此保留原生 Workshop 切图路径。

### 4. 自建管理插件

`E:\mapper\chana\steamapps\common\Counter-Strike Global Offensive\game\csgo\addons\counterstrikesharp\plugins\ZePractice\ZePractice.dll`

源码：
- `D:\34184\Documents\ChatGPT\CS2脚本\ZePractice\src\AdminPracticeData.cs`
- `D:\34184\Documents\ChatGPT\CS2脚本\ZePractice\src\AdminPracticeService.Management.cs`

修复 `!ze_map` 之前只按普通地图名加载的问题。仅对已安装地图采用本机 maplist 的显式 Workshop ID，不把 FYS 远程元数据中的旧ID直接当成本图。数字、范围和命令输入均校验。现可用：

```text
!ze_maps ze_forhidden_slicoo_cn
!ze_map ze_forhidden_slicoo_cn
```

管理切图需要本插件管理权限。CS2Fixes 自身 `!map 3817448617` 则使用它自己的权限体系。

### 5. 服务端 gameinfo.gi

`E:\mapper\chana\steamapps\common\Counter-Strike Global Offensive\game\csgo\gameinfo.gi`

Workshop 模式撤下本启动器管理的旧 `Game csgo_addons/ze_forhidden_slicoo_cn` 行，保留 `Game csgo/addons/metamod` 及其余官方配置。实测 RCON `path` 中主图与skybox来自下载的 Workshop 容器而不是旧本地 addon。

没有删除任何本地地图/开发 addon。客户端 gameinfo.gi 未修改。原更新脚本 `E:\mapper\chana\run.bat` 和旧服务器入口 `E:\mapper\chana\steamapps\common\Counter-Strike Global Offensive\game\bin\win64\run.bat` 均未修改。

## 再次验证游戏文件之后

- 使用 `E:\mapper\chana\ZePractice\run.bat`，不要把旧 gameinfo.gi 整份覆盖回去。
- 本启动器会按当前引擎版本检查 gameinfo，只补所需路径。Steam 游戏文件验证通常不应代替备份自建启动器和插件配置；上面列出的文件应保留。
- 地图有修改时，重新发布到同一个 Workshop ID；服务器下一次按ID加载时由Steam检查更新。只在本机重新编译不会自动更新已发布条目。
- `!ze_reload` 只重载本服配置，不负责上传或下载新版 VPK。

## 保留本地开发模式

仅在测试尚未上传的编译成果时用：

```powershell
& "E:\mapper\chana\ZePractice\run.bat" -LocalMap -Map ze_forhidden_slicoo_cn
```

此模式不是Workshop自动分发模式。其他电脑缺少未发布内容时仍需另行取得相同资源；平时让玩家进服不运行“同步本机地图.bat”。

## 验证与记录

- 新启动器 PS5/PS7 参数与挂载测试：20/20通过。
- 原 PS5/PS7 编码/部署兼容性回归：106/106通过。
- 原生服务器写日志时的共享读取/临时缺失回归：PS5/PS7共4项通过，修复监视日志时的并发占用错误。
- 管理插件数据回归：85项通过；Release构建零警告、零错误。
- 真实启动日志已出现 `Loading custom game "3817448617" with map "ze_forhidden_slicoo_cn"`、`Mounting addon '3817448617'`，随后 `ss_loading -> ss_active`。
- A2S返回 `ze_forhidden_slicoo_cn`、64 slots，RCON列出Metamod 3个插件和ZePractice已加载。
- 服务端执行 `css_ze_map ze_forhidden_slicoo_cn` 后再次走上述Workshop加载分支，未退回普通本地map命令。
- 实机仍有地图原有模型资源加载/空脚本警告。这些不等于Workshop包没有下载完成；三个被点名模型已确认存在于包中。本轮未修复这些独立地图制作问题，也未验收全部Boss关卡。

诊断和备份：
`E:\mapper\chana\临时文件夹\ze-workshop-3817448617-20261011`

首次改动前逐文件备份：
`E:\mapper\chana\临时文件夹\ze-workshop-3817448617-20261011\before-164358`

服务器总教程：
`E:\mapper\chana\ZePractice\服务器使用教程.md`