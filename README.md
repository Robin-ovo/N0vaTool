# N0va 缓存壁纸工具

把本地 MP4 动态壁纸写入 N0va Desktop 2.2.1.4 当前选中的官方动态缓存槽。
项目不修改 `N0vaDesktop.exe`，不修改 `DPPlayer.exe`，也不注入 DLL。

当前源码版本：`1.0.3`

## 1.0.3 修复

旧版本把缓存文件名、资源 ID 和槽大小写死。删除替换壁纸后，N0va 会切换到新的
`.ndf` 缓存槽，旧版本仍在寻找已经不存在的文件。

1.0.3 改为：

- 读取当前 `cur_sel_res_id`，不再覆盖成旧 ID。
- 优先使用上次成功替换记录定位当前槽。
- 其次关联 N0va 主日志和 DPPlayer 当前播放路径。
- 最后根据配置时间、进程启动时间和唯一合法视频槽定位。
- 按目标槽的真实大小生成 `00 00 + MP4 + free box` NDF。
- 每次替换前保存当前槽原文件、大小、哈希、资源 ID 和配置快照。
- 写入后校验哈希，失败时自动尝试回滚。

如果无法唯一确定缓存槽，工具会停止，不会盲写文件。

## 使用流程

1. N0va 当前壁纸被删除或需要换槽时，先在人工桌面选择一个官方**动态**壁纸。
2. 确认官方动态壁纸已经开始播放。
3. 保持人工桌面打开，运行本工具。
4. 导入 MP4，选择记录，点击“替换到人工桌面”。

当前官方视频太短时，它的缓存槽可能放不下待替换 MP4。此时工具会停止写入，需要
改选一个时长更长、缓存更大的官方动态壁纸。

## 项目结构

```text
src/N0vaCacheWallpaperTool/                  WinForms 主程序
tests/N0vaCacheWallpaperTool.Tests/          NDF 与槽发现回归
tests/N0vaCacheWallpaperTool.Integration/    替换、恢复和换槽集成回归
```

测试会自动生成临时 MP4 和模拟 N0va 目录，不需要真实视频或管理员权限。

## 构建

需要 Windows 和 .NET 10 SDK：

```powershell
dotnet restore .\N0vaCacheWallpaperTool.sln
dotnet build .\N0vaCacheWallpaperTool.sln -c Release --no-restore
```

发布：

```powershell
dotnet publish .\src\N0vaCacheWallpaperTool\N0vaCacheWallpaperTool.csproj `
  -c Release -o .\artifacts\publish
```

运行要求：

- Windows 10/11 x64
- Microsoft .NET Desktop Runtime 10
- N0va Desktop 2.2.1.4，默认安装路径 `C:\Program Files\N0vaDesktop`
- 管理员权限

## 回归测试

```powershell
dotnet run --project .\tests\N0vaCacheWallpaperTool.Tests -c Release
dotnet run --project .\tests\N0vaCacheWallpaperTool.Integration -c Release
```

第一条测试覆盖动态槽长度、过小槽拒绝以及删除旧槽后的重新发现。第二条测试覆盖：

1. 写入当前官方槽并校验哈希。
2. 恢复当前槽官方备份并校验哈希。
3. 删除该槽、创建新文件名/新大小/新资源 ID 的槽。
4. 再次替换和恢复。

## 数据目录

默认数据目录：

```text
%LOCALAPPDATA%\N0vaCacheWallpaperTool
```

其中包含壁纸记录、导入视频、当前槽官方备份和配置快照。不要把它提交到 Git。

## 限制

- 只支持已验证的 N0va Desktop 2.2.1.4。
- 只复用当前官方动态视频槽，不支持静态 PNG 槽。
- 建议使用 H.264 视频和 AAC 音轨；其他编码取决于 Windows Media Foundation。
- 标题、游戏、作者和类型保存在工具库中，不会修改 N0va 私有卡片索引。

## 安全与声明

本项目仅操作本机已有缓存文件。请自行确认壁纸素材、软件使用条款和本地修改权限。
项目不包含 N0va、miHoYo 或 HoYoverse 的官方视频缓存，也不隶属于相关公司。

仓库未附带开源许可证。若要将项目作为开源软件发布，请自行选择并添加 `LICENSE`
文件。
