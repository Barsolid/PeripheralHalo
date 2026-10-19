# 1.1.0 耳机与键盘适配

本次加入的是实际查询和解析协议，不只是设备名称或图标。以下新适配均通过协议样本与模拟接收器测试，尚未在本机逐台实测；型号、固件或接收器版本差异可能影响读取。

| 类别 | 新增/完善范围 | 电量来源 |
| --- | --- | --- |
| 雷蛇无线键盘 | BlackWidow V3 Pro、V3 Mini HyperSpeed、V4 Mini HyperSpeed、V4 Tenkeyless HyperSpeed、DeathStalker V2 Pro、V2 Pro TKL、Joro 的已登记协议配置，共 13 个 PID | 标准 Razer 07/80 电量与 07/84 充电，区分接收器/线缆 transaction ID 与 USB 接口 |
| 雷蛇耳机 | BlackShark V2 Pro 2020（0528）、BlackShark V2 Pro 2023（0555 / 0556）、Barracuda Pro（053A） | 2020 专用 FF feature；2023 与 Barracuda 使用各自 PA 报文布局 |
| 罗技耳机 | G533、G535、G633、G635、G733、G933、G935、G PRO、G PRO X，以及已登记的 G PRO X 2 版本 | HID++ 1F20 电压估算；G535 增加 consumer 集合例外 |
| 罗技 G PRO X 2 LIGHTSPEED 0AF7 | Centurion 接收器桥接、0104 电量特性、部分旧固件回退 | 设备直接报告的电量和充电状态 |
| 罗技无线键盘 | 原有通用 HID++ 自动发现继续保留，修正键盘/数字键盘类型映射 | 接收器上的 1004 / 1000 / 1001 或 HID++ 1.0 |

13 个键盘 PID 是协议配置数量，包含同一产品的有线与无线 ID，不等于 13 款产品。Joro 等蓝牙模式能否读取还取决于 Windows 暴露的 HID 接口；本版本没有通用 BLE GATT 电量服务，不保证所有蓝牙键盘。罗技、雷蛇 HFP 耳机仍可在 Windows 上报电量时走原有蓝牙路径。

**G435 LIGHTSPEED 的精确百分比与充电状态仍未实现。** 接收器存在不能证明耳机在线；蓝牙 HFP 能否读取仍取决于 Windows。没有把 G435 标记为已支持精确电量。

**仍未覆盖的例子：** 雷蛇 Nari 系列、Kraken 各无线版本、BlackShark V3、Barracuda X / 普通 Barracuda 等采用不同协议的型号；罗技 G522、G930、ASTRO 各代等。本版不向未登记型号盲发耳机专用命令，也不宣称两家品牌全型号兼容。

## 状态规则

- BlackShark 2020 上报较粗电量档位，以 ≈ 标记。该型号充电时的百分比不可靠，显示充电状态并隐藏百分比。
- PA 协议查询会短暂申请设备通讯会话，完成或失败时释放；不修改灯效、均衡器、DPI 或固件。
- 不同耳机协议严格分开：2020 的 FF feature 不会发送到 2023 PA 接收器。
- 没有充电回复时保持充电状态未知，不推测“未充电”。
- 设备不回复、返回旧头部、回显请求或非法百分比时，不用历史读数冒充当前电量。
- 移除无内置电池的 Basilisk V3 35K 配置，避免读取到无意义的电量。保留 68 个鼠标 PID 和 13 个键盘 PID。

新增协议依据：[OpenRazer 键盘驱动](https://github.com/openrazer/openrazer/blob/master/driver/razerkbd_driver.c)、[HaloBattery 耳机协议](https://github.com/HeyOkay/HaloBattery/tree/main/providers)。归属与许可证全文见 THIRD_PARTY_NOTICES.md。
