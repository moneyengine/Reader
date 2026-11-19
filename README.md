# 声阅 / LightReader

Windows 10/11 x64 原生 WPF 朗读工具。发布版无需浏览器或另装 .NET。

## 启动

双击项目目录的 `启动轻读.cmd`，或运行 `release/LightReader-win-x64/LightReader.exe`。发布目录中的文件需要一起保留。

首次启动时填入一篇示例。之后恢复上次的当前文档；清空文档后重启不会重新填入示例。

## 阅读与粘贴

- 只有阅读页面，没有单独的编辑模式。正文可选中、修改、复制和撤销重做，修改后自动保存；朗读中修改正文会停止当前播放。
- 右键文本位置，选择“从这里开始朗读”。左键用于定位和选中文字，不触发朗读。
- 阅读页面右键“粘贴”或 Ctrl+V 支持文字、Markdown 和图片。文字插入光标位置或替换选中内容；Markdown 粘贴后自动排版。剪贴板同时有图片和文字时优先识别图片。
- 支持 Markdown 标题、强调、列表、引用、链接、代码和表格。表格按行列显示，支持列对齐和单元格换行，文字与原文朗读位置保持对应。
- 正文和列表采用紧凑排版；行内代码使用等宽字体与灰底，代码块支持浅色/深色语法高亮和复制。HTTP、HTTPS、邮件链接可打开；缺少文件引用信息时不补造行号。
- 编程语言代码块默认跳过朗读；`text` fenced 流程文本正常朗读。过滤规则可在偏好设置和原图对照面板调整。
- 朗读中滚轮正常滚动页面。停止操作滚轮 2 秒后，页面自动回到最新朗读位置；停止播放会取消自动返回。

## 语音与识字

语音只使用 Microsoft Edge 在线神经语音。支持中文和其他语言音色、分类搜索、语速、音调、音量、暂停、继续、停止和进度跳转。正文发送至微软服务，需要联网。

语速和音调改变后重新合成；音量仅控制本机播放输出，不重新生成语音，也不重置进度。生成音频期间调节音量会作用于后续开始播放的音频。

新电脑需要 Python 3.10+ 和 edge-tts 7.3.1+。运行 `tools/Install-EdgeTts.ps1` 安装依赖，或在偏好设置指定 Python 路径并检测环境。启动时不自动安装软件或下载模型。

OCR 只使用 Windows 本机文字识别组件，图片不上传。支持屏幕框选、打开图片和粘贴图片；识别语言在偏好设置中选择。右侧可查看、缩放、导出和重新识别当前原图。没有豆包 OCR、豆包语音或本机语音入口。

## 文档与窗口

仅保存当前文档、当前原图和阅读偏好，不记录阅读历史。旧版本的历史记录不再载入或继续保存；不再被当前文档引用的图片会清理。正文、原图和偏好使用 Windows 当前用户 DPAPI 加密，位于 `%LOCALAPPDATA%/LightReader`。

偏好设置保留主题、透明度、OCR 语言、Edge 环境、过滤规则和当前文档导入导出。导出 JSON 为明文，请自行保管。原图兼容保存在 `history-images` 目录中，该目录名称不代表仍有历史功能。

支持置顶、悬浮播放器、最小化到系统通知区和原生窗口拖动。关闭主窗口或托盘退出会结束程序。

## 开发与验证

需要 .NET 9 SDK。发布统一运行 `tools/Publish.ps1`，固定覆盖 `release/LightReader-win-x64`。脚本会停止从该目录运行的程序，发布成功后清理旧构建输出并更新启动入口，不提交或推送代码。

```powershell
dotnet build tests/LightReader.Tests.csproj
dotnet run --project tests
dotnet run --project tests -- --check-markdown
dotnet run --project tests -- --check-markdown-replacement
dotnet run --project tests -- --check-reading-typography
dotnet run --project tests -- --check-speech-filter
dotnet run --project tests -- --check-local-ocr
dotnet run --project tests -- --check-slim-reader
dotnet run --project tests -- --check-reading-interactions
# 以下为微软服务联网测试，仅发送测试文字
dotnet run --project tests -- --check-edge-live
dotnet run --project tests -- --check-edge-ui
```

`--check-slim-reader` 需要先运行 `--check-local-ocr` 生成双语图片样本。它使用隔离状态验证首次/重复启动、精简界面、表格排版和编辑、文字/Markdown/图片粘贴、实际本机 OCR、播放输出音量和当前文档加密保存。截图输出到 `artifacts/slim-reader-*.png`。

旧的历史、多引擎、本机语音和云端模型界面测试不再适用于当前产品；当前界面回归使用 `--check-slim-reader`。
