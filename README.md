# Su-57 for Nuclear Option

[English](README_EN.md) · [安装](docs/INSTALLING_ZH.md) · [构建](docs/BUILDING_ZH.md) · [上传 GitHub](docs/GITHUB_ZH.md)

《核选项》（Nuclear Option）的非官方 Su-57 飞机 MOD，基于 Blueprinter / BepInEx，使用 Broken Arrow 的 Su-57 外观模型和 Nuclear Option 的原生飞行系统。

**当前版本：0.2.34，第三阶段模型细化中。** 当前 DLL 已编译，接口显示、出生碰撞、脱落与修复表现仍待使用者游戏验收。本仓库整理期间没有重新构建或运行测试。

## 功能

- 独立 `SU57` 飞机注册，军衔 4、价格 100 million，接入 `shelter1` 机库。
- 三级 LOD、十二块可动翼面、喷口矢量和分段起落架动作。
- 五套涂装、透明座舱玻璃、独立表面材质。
- 37 个可见分件、34 个物理部件、自有碰撞壳与断面显示。
- 九个武器站位、主弹舱与侧弹舱门；武器系统继续使用原生配置。
- 飞机资源、动画与运行时补丁合并为一个安装文件：`Su-57.dll`。

飞行、气动、发动机、座舱等以原生 KR-67 为原型基线；本项目不宣称复现真实 Su-57 的飞行性能。

## 玩家安装

需要 Nuclear Option、BepInEx 和通用 Blueprinter 加载器。本项目的已有开发环境为 Nuclear Option 0.34.6、Blueprinter 2.1.1；其他版本兼容性未确认。

从本仓库 Releases 下载 `Su-57.dll`，退出游戏后放入：

```text
Nuclear Option/
└── BepInEx/
    └── plugins/
        └── Su-57/
            └── Su-57.dll
```

保留现有通用 Blueprinter。升级时移除旧 Su-57 的独立 `.nobp`、`SU57.GearMotion.dll` 和其他重复的 Su-57 DLL，避免重复注册。具体步骤见[安装说明](docs/INSTALLING_ZH.md)。

## 开发者入口

本仓库包含自编源码、Unity 工程设置、构建及资源制作工具。**公开源码不包含飞机模型、贴图、提取数据或游戏程序集，单独克隆后不能直接生成完整 MOD。**

1. 使用 Unity **2022.3.62f2**，按[构建说明](docs/BUILDING_ZH.md)完成 Blueprinter 本地游戏资源导入。
2. 从已有授权的制作工程恢复本地 Su-57 资源，见[资源说明](docs/ASSETS_ZH.md)。
3. 在 Unity 选择 `Blueprinter > Su-57 > Build and Validate`。
4. 唯一安装产物为 `Builds/SU57/Install/Su-57.dll`。

菜单名称沿用现有工程，包含构建时的内部一致性检查；游戏与飞行验收由维护者自行执行。

| 路径 | 内容 |
| --- | --- |
| `Assets/Editor/SU57/` | prefab、材质、动作、损伤与单 DLL 构建入口 |
| `Tools/SU57GearMotion/` | 动画、碰撞过滤、质量参考和断面显示补丁 |
| `Tools/prepare_su57_*.py` | 离线资源制作工具 |
| `Packages/`、`ProjectSettings/` | 固定版本依赖与 Unity 配置 |
| `docs/` | 安装、构建、资源、发布及开发状态 |
| `Assets/Blueprinter/Mods/SU57/` | 本地资源恢复位置，实际资源由 Git 忽略 |

## 版本与验收

0.2.34 修正完整机身接口处的多余断面封边。当前已有 DLL 内嵌 0.2.32 飞机资源，并由 0.2.34 运行时补丁控制断面显示；以后完整重建会生成同版本飞机资源。

历史 0.2.2 曾完成无武器自动起降主流程，补给滑行仍有一次未复现的失败记录。该历史结果不代表当前版本已回归通过。人工手感、武器行为和多人同步仍待验收。详见[更新记录](CHANGELOG.md)与[开发计划](docs/ROADMAP_ZH.md)。

## 许可证与贡献

自编代码及项目文档采用 [MIT](LICENSE)。上游 Blueprinter 的署名与许可证保留在 [LICENSES/Blueprinter-MIT.txt](LICENSES/Blueprinter-MIT.txt)。模型、贴图和游戏资源不在本仓库 MIT 授权范围内，见[第三方说明](THIRD_PARTY_NOTICES.md)。

问题反馈和修改建议请使用 Issues / Pull requests，参阅[贡献说明](CONTRIBUTING.md)。
