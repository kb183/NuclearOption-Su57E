# 工具说明

所有工具均需维护者自行选择运行。仓库整理过程中没有执行构建、资源制作、包体验证或游戏测试。

| 文件 | 用途与前提 |
| --- | --- |
| `build_su57.ps1` | 完整 Unity 构建并编译单 DLL；资源必须已恢复 |
| `import_local_assets.ps1` | 从已有制作工程复制本地资源，不复制游戏导入 |
| `SU57GearMotion/*.cs` | 实际随单 DLL 编译的运行时代码 |
| `prepare_su57_nozzle_skin.py` | 制作喷口选择数据，需要 OBJ 与 nozzle 权重缓存 |
| `prepare_su57_wing_skin.py` | 制作翼面数据，需要 OBJ、层级与权重缓存 |
| `prepare_su57_surface.py`、`prepare_su57_liveries.py` | 制作表面与五套涂装，需要源纹理和数据 |
| `prepare_su57_damage_sections.py` | 制作可见分件与基础碰撞壳 |
| `su57_collision_cells.py`、`su57_side_bays.py` | 损伤制作工具调用的几何模块 |
| `prepare_su57_damage_details.py` | 制作断面、损伤贴图、站位与舱门相关数据 |
| `prepare_su57_mass_properties.py` | 根据已导入的原生主碰撞网格制作质量参考 |
| `inspect_fs41_structure.py`、`prepare_fs41_structure_reference.py` | 本地参考资料研究，需要另备参考 MOD |
| `inspect_flight_references.py` | 汇总本地历史参考资料 |
| `validate_su57_bundle.py` | 历史中间 nobp 序列化检查，含早期断言，非当前验收结论 |
| `RuntimeProbe.cs`、`test_su57_flight.ps1` | 历史诊断/飞行工具，会启动游戏与临时安装探针；不随 MOD 分发 |
| `summarize_su57_flight.py` | 汇总已有遥测，不负责运行游戏 |

## 可选 Python 依赖

资源制作依赖原工程已记录于 `requirements_su57_authoring.txt` 的版本；示例安装：

```powershell
python -m pip install --target Tools/AuthoringDeps -r Tools/requirements_su57_authoring.txt
```

需要 UnityPy 的本地提取或检查工具可另外安装：

```powershell
python -m pip install -r Tools/requirements_su57_optional.txt
```

工具保留历史制作顺序和报告目录，不保证所有脚本可以任意顺序从空目录执行。某些参考图生成使用 Windows 的 Segoe UI 字体。

## 环境变量

| 变量 | 内容 |
| --- | --- |
| `SU57_GAME_DIR` | 包含 NuclearOption.exe 的游戏目录 |
| `SU57_UNITY_EDITOR` | Unity 2022.3.62f2 的 Unity.exe 完整路径 |
| `SU57_BROKEN_ARROW_BUNDLE` | 可选的本地 Broken Arrow 原模型 bundle 路径，也可用 `--bundle` |
| `SU57_PROJECT_ROOT` | 诊断脚本传递给探针的输出工程目录；通常由飞行脚本自动设置 |

`test_su57_flight.ps1` 使用未占用的两位 Run 编号，显式传入 Game、Unity 后才执行。该旧版工具对 0.2.34 未复测；不能将它的源码存在视为游戏验证完成。
