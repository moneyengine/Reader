"""Generate the icon map from the exact bundled Material Symbols font (fonttools required)."""
from pathlib import Path
from fontTools.ttLib import TTFont

root = Path(__file__).resolve().parent.parent
font_dir = root / "src" / "Assets" / "Fonts"
with TTFont(font_dir / "MaterialSymbolsOutlined.ttf") as font:
    icons = {}
    for codepoint, name in font.getBestCmap().items():
        if codepoint >= 0xE000:
            icons.setdefault(name, codepoint)
for alias, canonical in {"file_download": "download", "file_upload": "upload", "message": "chat", "https": "lock"}.items():
    icons[alias] = icons[canonical]
output = font_dir / "MaterialSymbolsOutlined.codepoints"
output.write_text("".join(f"{name} {codepoint:x}\n" for name, codepoint in sorted(icons.items())), encoding="ascii")
print(f"Generated {len(icons)} icons: {output}")
