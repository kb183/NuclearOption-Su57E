# 本地构建

本仓库是 0.2.34 的源码与 Unity 工程配置导出。首次构建必须自行准备游戏导入和 Su-57 制作资源，公开源码 ZIP 不等于完整资源工程。

## 环境

| 项目 | 当前工程所用版本或要求 |
| --- | --- |
| 操作系统 | Windows |
| Unity Editor | 2022.3.62f2，具有有效 Editor 许可证 |
| Nuclear Option | 本机已有开发基线 0.34.6，其他版本未验收 |
| Blueprinter 运行时 | 本机已有开发基线 2.1.1 |
| Blueprinter Editor 包 | 固定提交 `6a9b43c8f245b27538700810ea769b79736d277d` |
| BepInEx / Harmony | 已在游戏中安装，单 DLL 编译引用其 core 程序集 |
| .NET | `dotnet` 可从 PATH 调用；编译器使用 Unity 自带 `Data/DotNetSdkRoslyn/csc.dll` |
| Git | Unity 下载 Git 包所需 |
| Python | 资源制作工具需要；不需要重做数据时可以不运行这些脚本 |

Packages/manifest.json 和 packages-lock.json 已去除维护者本机的 Unity MCP tgz 依赖。不要把个人工具路径重新提交。

## 1. 准备游戏导入

先安装所需工具，并按[固定版本的 Blueprinter 官方工程指南](https://github.com/nikkorap/NOBlueprinter-Editor/blob/6a9b43c8f245b27538700810ea769b79736d277d/GUIDE.md)准备本机游戏资源。大致顺序：

1. 通过 AssetRipper 从自己安装的 Nuclear Option 导出制作所需资源。
2. 使用 Unity Hub 打开本仓库，选择 Unity 2022.3.62f2。
3. 在 `Blueprinter > Project Setup` 填游戏版本、选择游戏的 NuclearOption.exe。
4. Import Game Assemblies、Import Game Assets；需要时导入 TMP Essentials。
5. Refresh Op References，然后 Build _donotship。

`Packages/nuclearoption`、`Assets/Blueprinter/_donotship` 和缓存仅留本机。Su-57 编辑器程序集以本地 nuclearoption 包为编译条件，完成 Project Setup 导入后才启用，便于先完成首次配置。条件使用 Unity 的 [Version Defines 与 Define Constraints](https://docs.unity3d.com/2022.3/Documentation/Manual/ScriptCompilationAssemblyDefinitionFiles.html)。不要用从别人的游戏安装目录复制 DLL 的方式补进 Git。

## 2. 恢复 Su-57 本地资源

从已有可用的制作工程恢复：

```powershell
.\Tools\import_local_assets.ps1 -SourceProject 'X:\YourAuthoringProject'
```

该脚本复制 Su-57 资源和两份可选权重缓存，不会复制游戏程序集、修改原工程、启动 Unity 或执行验证。会覆盖目标本地 Su-57 文件，因此请在目标尚无未保存资源修改时使用。

原工程的 `Assets/Blueprinter/Mods/SU57` 整体资源及 Unity .meta 必须保持配套。详情见[资源说明](ASSETS_ZH.md)。首次资源恢复后不必重新运行所有 prepare 脚本。

若本机游戏导入与旧制作数据的质量来源记录不一致，需要在完成导入后自行重新制作质量参考：

```powershell
python -m pip install --target Tools/AuthoringDeps -r Tools/requirements_su57_authoring.txt
python Tools/prepare_su57_mass_properties.py
```

这是离线数据制作。后续构建会核对对应本地源文件哈希；不同版本的原生布局仍须维护者自行处理与验收。

## 3. 选择游戏路径并构建

优先使用 Blueprinter > Project Setup 中已保存的游戏可执行文件路径。也可在启动 Unity 前设置 `SU57_GAME_DIR`：

```powershell
$env:SU57_GAME_DIR = 'X:\SteamLibrary\steamapps\common\Nuclear Option'
$env:SU57_UNITY_EDITOR = 'X:\Unity\2022.3.62f2\Editor\Unity.exe'
.\Tools\build_su57.ps1
```

支持显式传参：

```powershell
.\Tools\build_su57.ps1 -Unity 'X:\Unity\2022.3.62f2\Editor\Unity.exe' -Game 'X:\SteamLibrary\steamapps\common\Nuclear Option'
```

从编辑器操作时，菜单仍为 `Blueprinter > Su-57 > Build and Validate`。脚本会生成当前 prefab、资源包与单个 DLL，包含原有构建一致性检查。不会自动启动游戏、飞行测试或额外的 Python 包体检查。

## 产物

| 路径 | 用途 |
| --- | --- |
| `Builds/SU57/Install/Su-57.dll` | 唯一游戏安装文件 |
| `Builds/SU57/Internal/` | 开发中间 .nobp、编译响应文件与质量数据 |
| `Builds/SU57/Source/` | Blueprinter 制作资源导出，不含工程外全部源码 |
| `Builds/SU57/SU57_preflight.txt` | 构建说明与内部检查记录 |
| `Logs/su57-build.log` | Unity 命令行构建日志 |
| `Logs/su57-singlefile-build.log` | 单 DLL 编译日志 |

不要把 Internal、Source 或探针 DLL 放进玩家安装包。一般 Blueprinter 的 Mod Builder 只会生成 nobp，不能代替本项目的完整构建入口。

## 验收由维护者手动安排

本次仓库整理没有执行上述命令，也没有验证移植后的构建脚本。需要包体检查时，维护者可以显式传 `-ValidateBundle`；所需 UnityPy 见 `Tools/requirements_su57_optional.txt`。

历史 `Tools/validate_su57_bundle.py` 含早期版本断言，只用于辅助检查中间 nobp，不等于当前单 DLL 或整机验收。历史 `Tools/test_su57_flight.ps1` 会临时安装探针、启动游戏并执行飞行，只能由维护者明确选择运行，当前 0.2.34 兼容性未复测。

继续开发时保持相同版本号于 `BuildSu57.cs` 与 `Su57GearMotion.cs`，并在 CHANGELOG 中分别记录“源码已改”“已编译”“游戏已验收”。
