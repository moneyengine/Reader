# 免费自然语音：Edge 接入

在朗读引擎下拉框选择「Edge 自然语音」，再选择「晓晓」「云希」等中文音色。音色按语言、地区/口音、性别逐级展开，支持按分类路径及名称搜索全部在线音色；右侧「更多 → 刷新音色」可从服务重新获取目录。默认不会替换已有的 Windows 或火山引擎选择。

这是开源 edge-tts 客户端调用微软在线服务，模型不是开源的，也不是离线合成。无需 API Key。正文将发送至微软；断网或服务变动时可能不可用，可手动切回 Windows 本机。不会自动改用收费引擎。

## 当前机器与新机器

当前开发机器已存在 Python 3.12 和 edge-tts 7.3.1，软件会自动检测。新机器需要安装 Python 3.10+ x64： https://www.python.org/downloads/windows/ 。安装时启用 PATH，重开终端。

从项目根目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Install-EdgeTts.ps1
```

该脚本仅为自身进程指定执行策略，不修改全局策略。它会在 `%LOCALAPPDATA%/LightReader/edge-tts-env` 创建独立 Python 环境并安装已测试的 edge-tts 7.3.1，不替换系统 Python 包。也可手动执行 `python -m pip install edge-tts==7.3.1`；若多个 Python 并存，在服务设置填写已安装依赖的 python.exe 完整路径，并点击检测。

不需要安装 Edge 浏览器、FFmpeg、mpv、CUDA 或大模型权重。WPF / .NET 前置环境仍按交接文档安装；Python 只是 Edge 引擎的可选依赖，Windows 与火山引擎不依赖它。

## 运行与数据

- 命令行参数仅传桥接脚本和操作类型，文本通过 UTF-8 JSON 标准输入传递。
- 本文和生成音频只在内存中传输，不为合成创建正文/音频临时文件。软件原有的加密阅读历史仍按既有设置保存。
- 短段按标点切分，最多预取下一段；暂停保留播放位置，停止取消子进程及预取。
- 进度使用服务返回的真实边界事件；缺失边界时仅显示段落进度，不伪造逐字时间戳。
- 音量由本机播放器调整；语速与音调传给生成端。自然度应先以语速 1.0×、音调 1.0 试听；过快和较大音高变化可能影响听感。
- 音色目录是公开元数据，可离线展示；真正生成必须联网。初始目录为本次服务实际返回的 322 个音色，其中 14 个中文音色，不代表服务永久固定数量。

## 测试与维护

```powershell
dotnet run --project tests -- --check-edge-live
dotnet run --project tests -- --check-edge-ui
```

这两个入口只发送项目自带的合成测试文字，不上传用户正文，也不调用火山服务。一般协议测试仍可离线运行。

客户端采用独立 Python 依赖方式使用上游，不捆绑或修改上游包；更新时需遵循其 LGPLv3 / 部分文件 MIT 许可，参考 https://github.com/rany2/edge-tts 与上游 LICENSE。若未来向他人分发上游包或其修改版本，应附带完整相应许可和源码获取方式，不能将免费访问误称为开源语音模型。

本地开源模型的后续选项：Kokoro 中文模型（Apache-2.0） https://huggingface.co/hexgrad/Kokoro-82M-v1.1-zh ，CosyVoice https://github.com/QwenAudio/CosyVoice 。本轮没有静默下载模型或安装 GPU 环境。
