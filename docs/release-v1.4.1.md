# v1.4.1：相对路径修复

从[最新 Release](https://github.com/lyfhc0305/dawn-of-man-trainer/releases/latest) 下载 `DawnOfManTrainer-v1.4.1.zip` 完整解压，退出游戏后运行 `安装.cmd`。源码包仍需构建。

安装器默认查找相对于自身目录的 `..\DawnOfMan`。显式相对游戏路径也以安装器所在目录为起点；`Build.ps1` / `Test.ps1` 的相对 `-GamePath` 则以各自脚本目录为起点。工具继续接受绝对路径参数。

```text
同一父目录/
├─ DawnOfMan/             已安装的游戏
│  └─ DawnOfMan_Data/
└─ DawnOfManTrainer/      解压的安装包
   ├─ Installer.exe
   ├─ DawnOfManTrainer.dll
   ├─ Mono.Cecil.dll
   ├─ 安装.cmd
   └─ 卸载.cmd
```

在源码根目录构建与测试：

```powershell
.\Build.ps1 -GamePath '..\DawnOfMan'
.\Test.ps1 -GamePath '..\DawnOfMan'
.\TestInstaller.ps1
```

游戏功能沿用 v1.4.0：仓储 1–10×、建造 1–20×、成年与老年村民普通搬运 1–10×、拖板与拖车 1–10×；全部为整数，最低和默认均为 1×，保留紧凑滑块界面。

验证：本版离线检查共505项通过、0失败：功能与程序集133项、界面218项、安装器154项（含新增17项路径检查）。已从不同工作目录实际运行默认路径构建与显式相对路径测试，并验证不完整盘符路径会被拒绝。 v1.4.0的112项原生游戏自检属于历史验证，未作为本版重跑结果。

第三方依赖：随附 Mono.Cecil 0.11.6 及其许可证。第三方 Mono.Cecil 保留有效签名的上游原版；其调试元数据含上游构建路径，不是用户电脑路径，也不用于定位游戏。直接改写会破坏签名，因此它不属于本项目配置路径的替换范围。
