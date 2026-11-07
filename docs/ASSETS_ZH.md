# 资源边界与恢复

公开仓库只提供自编代码、工程配置、资源制作工具与文档。克隆仓库后没有飞机模型和游戏导入，是预期状态。

## 不收录的内容

| 内容 | 本地位置 / 用途 |
| --- | --- |
| Nuclear Option 程序集 | `Packages/nuclearoption`，由 Blueprinter 导入 |
| 原生游戏资源占位副本 | `Assets/Blueprinter/_donotship` |
| Su-57 模型、贴图和制作数据 | `Assets/Blueprinter/Mods/SU57` |
| Blueprinter 生成数据 | `Assets/Blueprinter/Generated`、`BlueprinterCache` |
| 参考 MOD、解包与录像 | `Reference` |
| Python 与反编译工具依赖 | `Tools/AuthoringDeps`、`Tools/VideoLib`、`Tools/ilspy` |
| 编译输出、日志与历史备份 | `Builds`、`Logs`、`_workspace` |

Su-57 目录中的派生 JSON 也可能携带模型顶点、UV、权重或其他第三方数据，不能仅因扩展名是文本就认定属于 MIT 源码。

## 从已有工程恢复

关闭目标仓库的 Unity 编辑器，在仓库根目录执行：

```powershell
.\Tools\import_local_assets.ps1 -SourceProject 'X:\YourAuthoringProject'
```

脚本会复制该工程的整个 `Assets/Blueprinter/Mods/SU57`（保留资源 .meta），并在存在时复制这两份作者工具使用的缓存：

- `Reference/SU57/VectoringVideoResearch/source-nozzle-weights.json`
- `Reference/SU57/ControlSurfaces/v0.2.11/source-wing-weights.json`

脚本不复制整份 Reference、不复制其他 MOD，也不执行游戏导入。核选项的程序集和原生资源请按 Blueprinter Project Setup 在本机导入。

当前构建主要依赖 Source 里的下列制作数据；除它们外，还需要配套 OBJ、贴图、地图图标与 .meta：

- `source_hierarchy.json`
- `SU57_WingSkin.json`、`SU57_NozzleSkin.json`
- `SU57_DamageSections.json`、`SU57_CollisionSections.json`
- `SU57_FractureCaps.json`
- `SU57_Hardpoints.json`、`SU57_WeaponDoors.json`、`SU57_SideBays.json`
- `SU57_Liveries.json`、`SU57_NativeMassProperties.json`

0.2.35 的机体内部细化属于后续工作，不作为本次 0.2.34 源码快照的已实现功能。

## 没有已有制作工程时

本仓库没有提供可公开下载的完整资产补充包，也不保证运行全部 prepare 脚本可以从空目录恢复整个历史制作流程。需要先自行取得可用资源及相应授权，再进行提取、格式转换和接线。

喷口和翼面制作工具依赖旧权重缓存与模型层级；表面、损伤和舱门制作工具依赖配套 OBJ 与纹理。研究工具还依赖本地参考资料。`Tools/README.md` 列出各类用途。

## 发布资源前

自编代码选用 MIT 不会改变原模型、贴图和游戏资源的权属。若以后取得资源再分发许可，应将许可范围与来源记录在 THIRD_PARTY_NOTICES.md，再有针对性地调整忽略规则。不要强制添加整个资源目录来绕过当前规则。
