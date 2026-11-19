"""Build an offline static comparison fixture from the supplied Stitch export."""
import hashlib
import importlib.util
import json
import re
import shutil
import urllib.parse
import urllib.request
from pathlib import Path

root = Path(__file__).resolve().parent.parent
source = root / "design" / "stitch" / "stitch_ui"
output = root / "artifacts" / "stitch-web-prototype"
assets = output / "assets"
assets.mkdir(parents=True, exist_ok=True)
spec = importlib.util.spec_from_file_location("stitch_layout", root / "tools" / "Read-StitchLayout.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
tokens = module.read_layout(source / "_1" / "code.html")["tokens"]
font_dir = assets / "fonts"
font_dir.mkdir(exist_ok=True)
for name in ("SourceSans3.ttf", "PlusJakartaSans.ttf", "MaterialSymbolsOutlined.ttf"):
    shutil.copy2(root / "src" / "Assets" / "Fonts" / name, font_dir / name)
image_dir = assets / "images"
image_dir.mkdir(exist_ok=True)
for page in ("_1", "_2", "_3"):
    html = (source / page / "code.html").read_text(encoding="utf-8-sig")
    html = re.sub(r'<link\b[^>]*href="https://fonts\.[^>]*>', "", html)
    html = re.sub(r'<script\b[^>]*src="https://cdn.tailwindcss.com"[^>]*>\s*</script>', "", html)
    html = re.sub(r'<script\b[^>]*id="tailwind-config"[^>]*>[\s\S]*?</script>', "", html)
    for url in set(re.findall(r'<img\b[^>]*src="(https://[^\"]+)"', html)):
        parsed = urllib.parse.urlparse(url)
        if parsed.hostname != "lh3.googleusercontent.com":
            raise ValueError(f"Unapproved remote image host: {parsed.hostname}")
        target = image_dir / (hashlib.sha256(url.encode()).hexdigest()[:20] + ".img")
        if not target.exists():
            with urllib.request.urlopen(url, timeout=30) as response:
                target.write_bytes(response.read())
        html = html.replace(url, "../assets/images/" + target.name)
    fonts = """
<link rel="stylesheet" href="../assets/stitch.css">
<style>
@font-face {font-family:'Source Sans 3';src:url('../assets/fonts/SourceSans3.ttf');font-weight:100 900;}
@font-face {font-family:'Plus Jakarta Sans';src:url('../assets/fonts/PlusJakartaSans.ttf');font-weight:100 900;}
@font-face {font-family:'Material Symbols Outlined';src:url('../assets/fonts/MaterialSymbolsOutlined.ttf');font-weight:100 700;}
.material-symbols-outlined {font-family:'Material Symbols Outlined';font-style:normal;font-weight:400;font-size:24px;line-height:1;letter-spacing:normal;text-transform:none;white-space:nowrap;word-wrap:normal;direction:ltr;font-feature-settings:'liga';font-variation-settings:'FILL' 0,'wght' 400,'GRAD' 0,'opsz' 24;}
</style>
<meta http-equiv="Content-Security-Policy" content="default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; font-src 'self'; img-src 'self' data:; connect-src 'none'; base-uri 'self'; form-action 'none';">
"""
    html = html.replace("</head>", fonts + "</head>")
    folder = output / page
    folder.mkdir(exist_ok=True)
    (folder / "index.html").write_text(html, encoding="utf-8")
config = {"content": [str(output / "_1" / "index.html"), str(output / "_2" / "index.html"), str(output / "_3" / "index.html")], "theme": {"extend": tokens}}
(assets / "tailwind.config.cjs").write_text("module.exports = " + json.dumps(config, ensure_ascii=False) + ";\n", encoding="utf-8")
(assets / "input.css").write_text("@tailwind base;\n@tailwind components;\n@tailwind utilities;\n", encoding="utf-8")
print(f"Prepared offline static design fixture: {output}")
