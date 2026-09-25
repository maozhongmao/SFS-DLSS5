# Spaceflight Simulator — DLSS 5

把 **DLSS 5（神经渲染）** 带到 Spaceflight Simulator 的集成项目：一个游戏内管理模组（Mods 文件夹形态）+ 配套渲染后端。

## 组成

- **SFSDLSS5**（本仓库）：SFS code mod —— 游戏内控制面板（`F9`），提供后端状态检测、一键暂停/恢复 DLSS 5、运行日志查看
- **渲染后端**（不随本仓库分发）：DLSS 5 的注入与神经渲染由社区工具链在游戏目录中安装（ReShade + 社区 add-on + NVIDIA 运行时组件），各组件遵循其自身许可

## 使用前提

- Spaceflight Simulator **1.6.x**（PC / Steam）
- NVIDIA RTX 显卡（DLSS 5 神经渲染的可用性与驱动要求以 NVIDIA 官方为准）
- 后端由第三方工具安装；本仓库不含任何 NVIDIA 或第三方二进制文件

## 构建

1. 从 SFS 安装目录复制引用文件到 `Lib\`：

   ```
   Spaceflight Simulator_Data\Managed\
     ├ Assembly-CSharp.dll
     ├ 0Harmony.dll
     ├ UnityEngine*.dll
     ├ Newtonsoft.Json.dll
     └ UniTask.dll
   ```

2. `dotnet build -c Release`
3. 把 `bin/Release/SFSDLSS5.dll` 放到：

   ```
   Spaceflight Simulator Game\Mods\SFSDLSS5\SFSDLSS5.dll
   ```

## 用法

- 游戏内按 **F9** 打开控制面板
- **Pause / Resume DLSS 5**：一键暂停/恢复神经渲染（写入后端配置）
- 面板底部实时显示后端日志

## 许可

MPL-2.0（见 [LICENSE.md](LICENSE.md)）。本仓库只包含模组源码，不含任何 NVIDIA 组件或第三方二进制。
