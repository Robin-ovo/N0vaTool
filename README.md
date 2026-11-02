# N0va 缓存壁纸工具

把本地 MP4 动态壁纸写入 N0va Desktop 2.2.x 当前选中的官方动态缓存槽。
项目不修改 `N0vaDesktop.exe`，不修改 `DPPlayer.exe`，也不注入 DLL。

当前源码版本：`1.0.4`

## 1.0.4 改进

- 自动检测非默认安装目录：手动保存路径、运行中的 N0vaDesktop 进程、注册表安装项和常见目录。
- 增加“选择安装目录”和“自动检测”按钮，手动路径会持久化保存。
- 支持兼容的 N0va Desktop `2.2.x`，不再只接受 `2.2.1.4`。
- 分别提示缺少 EXE、版本不兼容、缺少 DPPlayer、缺少配置、缺少缓存目录和尚未选择动态壁纸。
- `game` 目录不存在时提示先选择官方动态壁纸，不再误报安装不完整。
- 状态栏可点击查看完整原因。
- 日志写入程序目录下的 `logs` 文件夹，并提供“打开日志”和“删除日志”按钮。

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
4. 如果工具没有自动找到安装位置，点击“选择安装目录”，选择包含
   `N0vaDesktop.exe` 的目录。
5. 导入 MP4，选择记录，点击“替换到人工桌面”。

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
- N0va Desktop 2.2.x
- 管理员权限

默认安装路径 `C:\Program Files\N0vaDesktop` 可以直接自动检测；安装在其他目录时，
使用“选择安装目录”即可。

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

日志默认位于：

```text
程序目录\logs
```

点击“打开日志”可以查看失败原因，点击“删除日志”可以清空日志文件。

## 限制

- 支持 N0va Desktop 2.2.x；实际替换前仍会验证缓存结构，未知版本不会被盲目写入。
- 只复用当前官方动态视频槽，不支持静态 PNG 槽。
- 建议使用 H.264 视频和 AAC 音轨；其他编码取决于 Windows Media Foundation。
- 标题、游戏、作者和类型保存在工具库中，不会修改 N0va 私有卡片索引。

## 非官方声明与使用边界

本项目是一个独立的、非官方的本地文件管理实验项目，与 miHoYo、HoYoverse、
N0va Desktop 的开发或运营主体及其关联公司不存在隶属、授权、合作、赞助、认可
或其他官方关系。

`miHoYo`、`HoYoverse`、`N0va Desktop` 及相关名称、商标和作品的权利归各自权利人
所有。本项目仅在描述兼容性时提及这些名称，不代表获得任何官方授权。

- 仓库不包含官方视频缓存、游戏资源、账号凭据、付费内容、激活信息或 DRM 绕过工具。
- 用户必须仅处理自己拥有合法权利或已获授权的媒体文件。
- 用户应自行遵守软件许可协议、服务条款、著作权规定及所在地法律。
- 禁止将本项目用于绕过付费或访问控制、未经授权传播他人作品、商业侵权或其他违法用途。
- 本项目会修改本机缓存文件，使用前应自行备份；不保证不会出现文件损坏、壁纸失效、
  软件异常或其他风险。
- 软件按现状提供，不附带任何明示或默示担保。在法律允许的范围内，项目维护者不对
  使用或无法使用本项目造成的直接或间接损失承担责任。

上述声明不能替代专业法律意见，也不能保证当然免除所有法律责任。若你计划公开分发、
商业使用或重新发布本项目，请先咨询合格律师并取得必要授权。

完整条款见 [DISCLAIMER.md](DISCLAIMER.md)。

仓库未附带开源许可证。若要将源码作为开源软件发布，请自行选择并添加 `LICENSE`
文件；没有许可证时，默认不授予他人复制、修改或再分发源码的权利。
