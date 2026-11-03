# Cassette Studio · 磁带包工作室

A Windows desktop tool for creating cassette music mods for **Probably Stolen**.

为《深空当铺：可能是偷的》制作磁带音乐包的 Windows 桌面工具。导入自己的音频和包装图，编辑曲名与介绍，生成可安装的原生 Mod 和创意工坊上传配置。

## 功能

- 独立磁带包工程：配置集中在 pack.json，各包使用独立 Mod 和程序集 ID。
- 导入 OGG、WAV、MP3、FLAC、M4A，编辑中英文曲名与简介、调整顺序、试听原曲。
- 礼盒 PNG 导入、透明背景预览、64×64 游戏贴图处理。
- 按整曲综合响度匹配音量，保留动态，编码后检查真峰值，支持缓存。
- 首次加载存档后延迟指定游戏日，由配送员赠送礼盒，每个批次每档领取一次。
- 编译、游戏模组扫描、ZIP 导出、本地安装、旧包备份和安装失败恢复。
- Steam Workshop 发布资料、封面、VDF 配置、SteamCMD 调用和条目 ID 回填。
- 根据当前歌曲自动生成工坊介绍、歌曲列表、领取方式和播放说明。

## 环境

Windows 10/11、.NET 8 SDK、Python 3.10+，以及加入 PATH 的 FFmpeg / FFprobe / FFplay。上传工坊还需 SteamCMD。

实际 Mod 构建需要本地安装的 Probably Stolen，以引用游戏 Managed 程序集。源码和界面检查可在没有游戏的电脑上运行。本项目不附带游戏程序集、SteamCMD 或商业音乐。

## 快速开始

```powershell
git clone https://github.com/LoveLoliii/cassette-studio.git
cd cassette-studio
./start-studio.ps1
```

启动脚本生成两首短原创测试旋律，然后编译并打开桌面应用。演示工程位于 packs/demo/pack.json。

1. 新建包，选择存放位置，工具自动创建独立子文件夹。
2. 填写名称、作者、版本和游戏兼容版本，选择本机游戏目录。
3. 添加歌曲、编辑曲名和简介、导入礼盒 PNG。
4. 保存工程，再点击“构建 / 导出 ZIP”。首次处理歌曲较慢，之后使用缓存。
5. 完全退出游戏后点击“构建并安装”，在游戏 Mods 菜单中启用并重启游戏。

游戏兼容版本需填写游戏实际报告的值。开发时验证的构建为 VERSION 100-Rev6，其 Unity Application.version 为 0.46D；未来游戏更新仍需验证。

## 创意工坊

顶部“创意工坊发布”中填写资料并选择小于 1 MB 的 PNG/JPG 封面。首次条目 ID 填 0，默认隐藏。

点击“构建并准备发布”，核对固定上传快照，再点击“上传 / 更新”。SteamCMD 在独立窗口打开，由你完成登录及 Steam Guard；工具不读取密码或验证码。

上传后保留返回的条目 ID，之后更新同一条目。退出码不代表上传一定成功，请打开工坊页面确认。介绍使用真实换行，避免字面反斜线 n 的显示问题。

## 命令行

```powershell
python tools/create_demo_pack.py
./build.ps1 -Pack ./packs/demo/pack.json -GameDirectory 'D:\SteamLibrary\steamapps\common\Probably Stolen'
./build.ps1 -Pack ./packs/demo/pack.json -GameDirectory 'D:\SteamLibrary\steamapps\common\Probably Stolen' -Install
```

## 检查

```powershell
python tools/create_demo_pack.py
dotnet build desktop/CassetteStudio.csproj -c Release
python -m unittest discover -s builder -p test_builder.py -v
dotnet run --project desktop-tests/WorkshopChecks.csproj -c Release
dotnet run --project tests/ScheduleChecks.csproj -c Release
```

这些检查不登录 Steam、不上传内容、不修改真实游戏目录，安装测试使用临时目录。GitHub Actions 在 Windows 上执行基础检查。

## 结构

| 目录 | 内容 |
| --- | --- |
| desktop/ | WPF 界面、工坊发布与工程模型 |
| builder/ | 校验、代码生成、音频处理、构建、扫描、安装 |
| src/ | 通用游戏运行时模板 |
| schemas/ | 工程配置格式 |
| packs/demo/ | 不含第三方音乐的公开演示工程 |
| tools/Scanner/ | 调用本机游戏扫描器的辅助工具 |
| desktop-tests/、tests/ | 发布与配送日程检查 |

个人工程、音频、SteamCMD 设置、工坊快照、缓存和构建产物默认忽略。自己的工程放在 packs/<包名>/ 中。更多操作见 [使用指南](STUDIO.md)。

发布后保持 Mod ID 和曲目稳定 ID 不变。排序和改名不改变物品 ID，删除已发布曲目可能影响旧档。只有需要给已有存档补发礼包时才改领取批次 ID。

## 许可

原创源码采用 [MIT License](LICENSE)。游戏、用户音乐、商标及其他第三方资源不适用本项目许可证，参见 [第三方说明](THIRD_PARTY_NOTICES.md)。

本项目为非官方社区工具，游戏 API 与发布资料参考 [Probably Stolen Modding Guide](https://github.com/dragonpan2/probably-stolen-modding)。
