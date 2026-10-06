# 显示器调节 · monitor-adjust

**A native Windows GUI to adjust monitor brightness, contrast and input source over DDC/CI.**
通过 DDC/CI 直接控制显示器硬件的亮度、对比度与输入信号源，自动识别任意数量的显示器。

一个 Windows 桌面小工具，用来直接控制显示器的**亮度、对比度**和**切换输入信号源**。
走 DDC/CI 协议，调的是显示器硬件本身，和 Windows 自带的亮度滑块不是一回事 ——
所以外接显示器、以及系统滑块管不到的对比度和信号源，都能调。

原生 C# WinForms 单文件程序，不含任何脚本宿主（不用 bat/ps1/PowerShell）。

> 本仓库只包含**源代码**和**编译好的 exe**。
> 运行需要另外下载 `winddcutil.exe`（见下）。

## 功能

- 界面**中文 / English 实时切换**（见下）
- 自动识别接入的显示器数量，**插几台认几台**；界面按屏幕大小自适应排布，显示器多时自动分列 / 滚动
- 每台显示器独立调节：亮度、对比度
- 切换输入信号源（HDMI / DP / DVI / VGA / 分量 等），带二次确认
- 「应用」只写亮度和对比度，**不会误切信号源**
- 「恢复原值」回到打开窗口那一刻的数值
- 「重新读取」刷新全部数值，并把当前值记为新的「原值」
- 亮度量程支持 0-100 / 0-255：默认自动识别，也可手动指定

## 界面语言 / Language

界面支持**中文 / English 实时切换**：窗口底部「语言 / Language」下拉框里选，切换即时生效，选择会被记住。

- **首次运行**按系统语言自动判断：中文系统用中文，其它用 English
- 选择保存在 exe 同目录的 `monitor-adjust.ini`；该目录不可写时（例如装在 Program Files）自动退到
  `%LOCALAPPDATA%\monitor-adjust\settings.ini`
- 也可以用命令行强制启动语言：

```powershell
.\显示器调节.exe --lang en
```

## 运行依赖

需要 [**winddcutil**](https://github.com/ubihazard/winddcutil)（ddcutil 的 Windows 移植版，
一个 PyInstaller 单文件程序），**放在和 `显示器调节.exe` 同一个目录下**。

程序会按这个顺序找它：exe 同目录 → exe 同目录的 `winddcutil\` 子目录 → 当前目录 → `PATH`。

系统要求：Windows 7 SP1 及以上，自带 .NET Framework 4.x 即可，无需管理员权限。

## 使用方法

1. 把 `显示器调节.exe` 和 `winddcutil.exe` 放进同一个文件夹
2. 双击 `显示器调节.exe`
3. 拖动滑块后点「应用」

注意事项：

- **切换信号源请谨慎**：切到没有接线的接口，显示器会黑屏显示「无信号」，
  而且软件可能再也切不回来，需要用显示器自己的实体按键切回去
- 显示器需在自己的 OSD 菜单里开启 DDC/CI（多数显示器默认已开）
- 笔记本内置屏通常不支持 DDC/CI，不会出现在列表里，属正常现象
- 程序会在自己所在目录生成 `gui-log.txt` 运行日志，可随时删除

## 从源码编译

用系统自带的 .NET Framework 编译器，**不需要 Visual Studio**：

```powershell
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" `
  /nologo /target:winexe /codepage:65001 `
  /out:"显示器调节.exe" `
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
  "显示器调节.cs"
```

- `/target:winexe` 不弹控制台窗口
- `/codepage:65001` 源码是 UTF-8，**必须加**，否则中文字符串会乱码
- 输出文件名请保持 `显示器调节.exe`：程序集名取自文件名

## 隐藏参数（调试用）

```powershell
# 自检：把读到的数值写成文本，不显示窗口
Start-Process ".\显示器调节.exe" -ArgumentList '--dump','dump.txt' -Wait

# 离屏渲染：窗口画在屏幕外，截图存 PNG，用来检查排版
Start-Process ".\显示器调节.exe" -ArgumentList '--render','preview.png' -Wait

# 假数据模式：完全不调用 winddcutil，用编造的数据画界面（1~16 台）
Start-Process ".\显示器调节.exe" -ArgumentList '--fake','6','--render','n6.png' -Wait

# 模拟小屏幕，验证多显示器下的排版
Start-Process ".\显示器调节.exe" -ArgumentList '--fake','8','--screen','1024x600','--render','n8.png' -Wait

# 指定启动语言（zh / en），用于截图对比两套界面
Start-Process ".\显示器调节.exe" -ArgumentList '--lang','en','--render','en.png' -Wait
```

`--fake` 会连写入操作一起挡掉，**不会误改真实显示器**。

## 已知限制

- **量程靠猜**：`winddcutil getvcp` 只返回当前值、不返回最大值，也没有任何命令行选项，
  所以程序只能按「读到的值 > 100 就当 0-255」判断，猜错时请手动指定量程
- 信号源列表偶尔读不到（DDC/CI 通信本身不稳定），此时会退回一份常见输入源的兜底列表
- 滑块范围固定 0 起步（显示器的亮度/对比度最低值基本都是 0）

## 许可

代码可自由使用、修改、分发。
`winddcutil` 是其各自作者的独立项目，遵循其自身许可，本仓库不包含它。
