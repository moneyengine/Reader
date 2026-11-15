"""LightReader IPC adapter for the separately installed edge-tts package.

The package is not modified or bundled. Requests stay in memory; never write
reader text/audio to files or put text into process command arguments.
"""
import asyncio
import base64
import importlib.metadata
import json
import sys


def reply(value):
    print(json.dumps(value, ensure_ascii=False), flush=True)


async def main():
    try:
        import edge_tts
    except ImportError:
        reply({"error": "缺少 edge-tts。请运行 tools/Install-EdgeTts.ps1，或使用 python -m pip install edge-tts==7.3.1", "code": "dependency_missing"})
        return 2
    version = importlib.metadata.version("edge-tts")
    if tuple(int(x) for x in version.split(".")[:3]) < (7, 3, 1):
        reply({"error": "edge-tts 版本过旧，请运行依赖安装脚本升级至 7.3.1", "code": "dependency_missing"})
        return 2
    if "--check" in sys.argv:
        reply({"version": importlib.metadata.version("edge-tts"), "python": sys.executable})
        return 0
    if "--voices" in sys.argv:
        voices = await edge_tts.list_voices()
        reply({"voices": [{"id": v["ShortName"], "locale": v["Locale"], "gender": v["Gender"], "name": v.get("FriendlyName", v["ShortName"])} for v in voices]})
        return 0
    request = json.loads(sys.stdin.buffer.readline(1024 * 1024).decode("utf-8"))
    text = request["text"]
    if not isinstance(text, str) or not text.strip() or len(text) > 2000:
        raise ValueError("朗读段落为空或超过 2000 字符")
    speaker = edge_tts.Communicate(text, request["voice"], rate=request.get("rate", "+0%"), pitch=request.get("pitch", "+0Hz"), volume="+0%", boundary="WordBoundary", connect_timeout=12, receive_timeout=45)
    audio = bytearray()
    marks = []
    async for event in speaker.stream():
        if event["type"] == "audio":
            audio.extend(event["data"])
            if len(audio) > 20 * 1024 * 1024:
                raise ValueError("单段音频超过 20 MB")
        elif event["type"] in ("WordBoundary", "SentenceBoundary"):
            marks.append({"seconds": event["offset"] / 10_000_000, "text": event["text"]})
    if not audio:
        raise ValueError("在线服务没有返回音频")
    reply({"audio": base64.b64encode(audio).decode("ascii"), "marks": marks})
    return 0


if __name__ == "__main__":
    try:
        sys.exit(asyncio.run(main()))
    except Exception as error:
        if getattr(error, "status", None) == 403:
            message = "在线服务拒绝连接（HTTP 403），请确认 edge-tts 为新版并检查网络连接"
        else:
            message = type(error).__name__ + ": " + str(error).split("url=")[0][:350]
        reply({"error": message, "code": "service_error"})
        sys.exit(1)
