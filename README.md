# 熠键解压

熠键解压是一款面向 Windows 10/11 x64 的便携式多层压缩包解压工具，支持自动连续解压和可视化自定义工作流。

## 主要功能

- 自动识别 ZIP、RAR/RAR5、7Z、TAR、GZ、BZ2、XZ 等真实格式
- 自动连续逐层解压，可设置最大层数
- `.jpg` / `.jpeg` 伪装为 RAR 或 ZIP 时，在工作区自动生成正确后缀副本
- 可视化添加、删除、排序“解压”和“重命名”步骤
- 多文件及文件夹拖放批处理
- 便携密码库，可按步骤选择多个密码并设置尝试顺序
- 自定义模板保存、导入、导出与管理
- 可选择保留中间文件，或成功后自动清理
- 自包含发布，无需目标电脑安装 .NET、Bandizip 或 7-Zip

## 下载

请前往 [Releases](../../releases) 下载最新的 Windows x64 便携 ZIP。解压后必须保留完整目录，不能只复制 EXE。

## 从源码构建

需要：

- Windows 10/11 x64
- .NET 8 SDK

```bash
dotnet build src/ArchiveChainTool/ArchiveChainTool.csproj -c Release
dotnet run --project src/ArchiveChainTool/ArchiveChainTool.csproj -c Release -- --self-test
dotnet publish src/ArchiveChainTool/ArchiveChainTool.csproj -c Release -r win-x64 --self-contained true
```

运行时需要在程序目录提供：

```text
tools/7zip/7z.exe
tools/7zip/7z.dll
```

正式 Release 中包含来自官方 7-Zip 发行包的未修改组件和对应许可证。仓库不提交构建产物、用户密码库、用户模板或测试样本。

## 密码安全

便携密码库位于 `data/passwords.json`，内容是明文。复制便携目录会同时复制密码，请勿将包含真实密码的目录提交到 GitHub 或交给不可信人员。

模板只保存密码库条目的 GUID 引用，不保存密码明文。

## 第三方组件

本项目运行时调用独立的 7-Zip 命令行组件。7-Zip 按其自身许可证授权，不属于本项目 GPL-3.0 许可范围。详见 [7-Zip 官方许可证](https://www.7-zip.org/license.txt)。

## 许可证

本项目源代码基于 [GNU General Public License v3.0](LICENSE) 开源。

Copyright © 2026 sukixz
