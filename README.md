# Takeover 2 修改项目：简体中文与手枪弹药补丁

> **非商业项目，仅供个人学习、研究和非商业使用。禁止出售、收费分发或商业捆绑。**

适用于 **Windows Steam 版，Build 26580614**。汉化菜单、设置、教程、剧情字幕、挑战和结算，并可在安装时启用：**手枪弹药上限 10,000 发、每个弹药包增加 500 发；2000 分获得一条命**。保留原版配音；贴图和 Steam 成就页面可能仍显示英文。

**自动注入安装（推荐）：**从 Releases 下载 `Takeover2-Auto-*.zip` 并完整解压。退出游戏，双击 **AutoInstall.cmd** 一次，然后从 Steam 启动游戏。它会安装 BepInEx 预加载器，启动时在内存中应用汉化、手枪弹药和 **2000 分加命门槛**；Steam 更新后通常不需要重新安装。游戏语言保持 English，补丁会把英文语言表替换为简体中文。

**传统安装：**从 Releases 下载 `Takeover2-ZH-*.zip` 并完整解压。退出游戏，双击 **Install.cmd**，然后从 Steam 启动游戏。这个版本会改写游戏 DLL，适合不使用自动注入的情况。

**卸载：**退出游戏，双击 **Uninstall.cmd**。Steam 更新可能覆盖补丁；更新后重新运行新版安装器即可修复。

**自动注入卸载：**退出游戏，双击 **AutoUninstall.cmd**。它只移除本项目安装的文件，会保留 BepInEx 的其他文件。

仓库包含源码和译文。安装器在本机生成补丁并备份原文件，压缩包不含游戏原文件。手枪修改源码见 [src/PatchPistol.cs](src/PatchPistol.cs)，安装器会把它和汉化补丁一起编译；机枪、弹药包掉落频率和其他武器保持原版逻辑。完整限制见 [非商业使用声明](NON-COMMERCIAL-NOTICE.md)。Mono.Cecil 的许可证见 [vendor](vendor/Mono.Cecil.LICENSE.txt)。本项目是非官方补丁。
