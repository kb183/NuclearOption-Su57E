# 上传 GitHub

本目录已整理为独立源码仓库。维护者的原制作工程继续保留，上传时选择这个目录即可。

建议仓库名：`NuclearOption-Su57`。

建议 Description：

> Unofficial Su-57 aircraft mod for Nuclear Option. Single-DLL Blueprinter plugin, custom animations and damage model.

建议 Topics：`nuclear-option`、`su57`、`modding`、`unity`、`bepinex`、`blueprinter`。

## 方法一：GitHub Desktop

1. 在 GitHub Desktop 选择 `File > Add local repository`，选择本目录。
2. 使用 `Prepare Su-57 source for GitHub` 作为首次提交说明并提交。
3. 点击 `Publish repository`，确认仓库名、说明和可见性后发布。
4. 发布后检查首页 README、许可证与 Issues 模板显示。

仅希望上传源码时，不要把旁边 Release 待上传目录或原制作工程拖进仓库。

## 方法二：命令行

在 GitHub 创建一个空仓库，不要再勾选自动添加 README、LICENSE 或 .gitignore，然后在本目录执行：

```powershell
git add .
git commit -m "Prepare Su-57 source for GitHub"
git remote add origin https://github.com/YOUR_USERNAME/NuclearOption-Su57.git
git push -u origin main
```

将 YOUR_USERNAME 改成你的 GitHub 用户名。本地已初始化 main；没有配置远端、代填提交身份或推送。若 Git 提示没有提交身份，用你自己的 name / email 配置后再提交。

源码 ZIP 适合下载、备份和传给他人；不要把整个 ZIP 直接作为 Code 页的唯一文件。网页上传时先解压并保留目录结构；GitHub Desktop 更方便处理以点开头的配置文件。

## 发布安装文件

源码与安装包分别上传：

- Code 页：本目录中的 Git 源码。
- Releases：`Su-57.dll`，不放独立 nobp、动画 DLL 或诊断探针。

已有 0.2.34 单 DLL 已另存到仓库外的 Release 待上传目录；本仓库 `docs/release-artifact.json` 记录其大小、SHA-256 和内嵌资源版本。

确认资源发布授权后，可以创建 `v0.2.34` 标签和 Release，使用 [0.2.34 发布说明](releases/v0.2.34.md)。当前游戏效果待验收，发布时应勾选 **Set as a pre-release**，不要描述为稳定验证版。

本次仓库导出中的路径与依赖整理没有重新编译，已有 DLL 也没有因为这些整理而重新生成。以后完整重建需恢复本地资源，见构建说明。

## 体积与忽略规则

GitHub 对普通 Git 历史中的单个文件有 100 MiB 上限，较大的二进制应使用合适的发布存储；本项目把安装 DLL 放在 Releases，源码库不携带资源与缓存。参见 [GitHub 大文件说明](https://docs.github.com/en/repositories/working-with-files/managing-large-files/about-large-files-on-github)。

仓库整理参考了 [GitHub 仓库最佳实践](https://docs.github.com/en/repositories/creating-and-managing-repositories/best-practices-for-repositories)，已准备 README、许可说明、贡献说明、Issue 和 PR 模板。没有设置依赖私人游戏文件的自动构建或测试流程。
