# 协议说明与实现边界

所有外设命令都用于读取。应用不发送 DPI、灯效、配对、固件更新或充电配置命令。

## Logitech HID++

依据 [Logitech 官方 HID++ 文档](https://github.com/Logitech/cpg-docs/tree/master/hidpp20) 与 [Solaar 的实现](https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/hidpp20.py)。

枚举厂商 `046D` 的 HID 集合，在 `FF00` / `FF43` 厂商通道发送 HID++ 长报文或短报文。使用软件 ID `B` 匹配请求与响应，验证设备槽位、特性索引与功能字节，忽略不相关通知。接收器尝试槽位 1–6，直接设备尝试 `FF`。空槽位的 root 请求有超时，特性索引与设备名称缓存五分钟；手动刷新清空缓存。

- Root `0000` 查找特性索引；`0005` 读取名称与类型。
- `1004` function 1：百分比与状态；仅报告档位时明确标记估算。
- `1000` function 0：百分比与状态；0 表示未知。
- `1001` function 0：毫伏与充电标志，用插值曲线估算百分比。
- `1F20` function 0：部分耳机的毫伏与在线/充电标志。
- 在短报文通道回退到 HID++ 1.0 `81/0D` 与 `81/07` 电量寄存器。后者仅为粗略档位。

该实现不覆盖所有罗技耳机协议，例如 Centurion。G435 特别保留为未知状态，不对其发送不相关的 HID++ 探测命令。

## Razer

电量/充电命令及 PID 到 transaction ID 的对应关系来自 [OpenRazer](https://github.com/openrazer/openrazer/tree/master/driver)。`src/razer-models.txt` 为 2026-10-18 获取源码时的 69 个 USB 配置，不代表 69 款全部实测通过。

HID feature report：报告 ID 0 + 90 字节厂商报文。命令类 `07`，电量 `80`，充电 `84`，参数长度 2。返回成功状态、transaction ID、命令类、命令号和 XOR 校验和均需匹配。将参数中第二字节的 0–255 换算为 0–100%。可更换电池的型号不查询充电状态。

Windows 对鼠标/键盘集合的读写句柄有访问限制。feature-only 句柄使用零访问权限，保留系统鼠标输入；不安装或替换驱动。所有查询使用重叠 I/O、超时和取消；取消完成前不释放缓冲区。无报告 ID 的 feature 响应可能返回 90 字节计数，报告缓冲区仍为 91 字节。

## 蓝牙 HFP / G435

Windows 在 HFP Hands-Free 服务实例上可能发布电量属性 `{104EA319-6EE2-4701-BD47-8DDBF425BBE5}, 2`。属性依据 [GearHub 的公开实现](https://github.com/jenesei-software/gearhub/blob/main/src/GearHub.Providers.Windows/Bluetooth/BluetoothHfpBatteryProvider.cs)；本项目额外通过 [Windows Bluetooth API](https://learn.microsoft.com/en-us/windows/win32/api/bluetoothapis/nf-bluetoothapis-bluetoothfindfirstdevice) 仅选择当前已连接设备，不将历史配对记录作为在线证据。

该路径不进行无线询问，不主动扫描或改变音频连接。电量来自 Windows 缓存，更新频率由耳机与系统决定，不推断充电状态。当前没有 G435 蓝牙实机验证结果。

G435 LIGHTSPEED 仅识别接收器与说明限制。官方灯色说明见 [Logitech Support](https://support.logi.com/hc/en-ph/articles/4409384013591-Check-the-battery-level-of-your-G435)。软件没有手工输入、时间推算或模拟百分比来冒充真实读数的回退路径。
