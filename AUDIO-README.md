# 轻读 · 音频生成接入版

当前文字朗读按用户指定文档调用 `POST https://openspeech.bytedance.com/api/v3/tts/create`，模型 `seed-audio-1.0`，鉴权 `X-Api-Key`。当前主界面不再调用 Seeduplex。图片识别本轮暂不继续处理。

## 输入密钥

本机 CMD 中运行 `tools/Enter-AudioKey.ps1`，粘贴音频生成服务的 API Key 后按 Enter。输入隐藏，不写命令历史。保存后密钥位于 `%LOCALAPPDATA%\LightReader\audio-key.dat`，通过当前 Windows 用户 DPAPI 加密。也可在新版设置页填写音频生成 Key。

启动 `LightReader.exe`，粘贴文本，选择 Vivi 2.0 等音色，点击播放。关闭旧版后再运行新版，避免混淆。既有方舟配置和 Seeduplex 配置不会被自动覆盖。新增音频密钥单独保存。

## 行为

- 当前接口非流式，需要每段生成完成后播放；状态栏显示首段生成耗时，不能按实时语音接口的延迟预期衡量。
- 长文按标点分段，最多每段 260 个 UTF-16 字符。暂停保留播放位置、阻止后续请求；正在生成的一段可能继续生成。停止取消本机请求并丢弃结果，服务端可能已产生用量。
- `references[0].speaker` 指定语音合成 2.0 音色，提示词用 `@音频1` 引用。原有 jupiter 音色不会直接用于新接口。
- 请求 WAV，解析实际采样率和声道，转换为播放器使用的 24 kHz、单声道、16-bit PCM。避免将 WAV 文件头当作音频播放。
- 生成式接口可能存在原文遵循和跨段韵律差异，真实音质需要试听验收。不保证零漏字或零改写。
- 401 检查密钥，403 检查 Seed-Audio 1.0 开通权限，429 检查额度。

已通过 29 项本地检查。2026-11-11 使用本机保存密钥实测成功：测试文本“你好，这是轻读的朗读测试。请放松，慢慢听。”生成耗时 21.90 秒，返回 6.68 秒音频，成功解码为 320640 字节 PCM。这是单次实测，不代表稳定延迟或完整音质验收。该非流式接口尚未达到低首音延迟目标。

官方文档：https://docs.volcengine.com/docs/doubao-voice/audio-generation-http?lang=zh
