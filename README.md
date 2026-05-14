# 不知道啊工具箱

Android Fastboot 刷机工具，支持 AB 分区设备。

## 功能

- Fastboot / ADB 设备检测
- 分区刷写（自动识别逻辑分区，delete+create+flash）
- 一键全刷
- 设备信息（产品型号、系统版本、内核、电池、内存等）
- 重启模式切换
- Bootloader 解锁/上锁
- 擦除分区
- scrcpy 投屏
- payload.bin 解包
- 浅色/深色主题切换

## 运行

1. 下载 `FastbootFlasherGUI_Free.exe`
2. 将 `.img` 映像文件放入 `images/` 文件夹
3. 手机进入 fastboot 模式，连接 USB
4. 双击运行

## 构建

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## 许可

MIT License
