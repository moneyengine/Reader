"""Read exported layout tokens and structural classes without executing HTML scripts."""
import argparse
import json
from html.parser import HTMLParser
from pathlib import Path


class LayoutParser(HTMLParser):
    def __init__(self):
        super().__init__()
        self.stack = []
        self.nodes = []
        self.script = None
        self.config = ""

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        node = {"tag": tag, "id": attrs.get("id"), "class": attrs.get("class", ""), "parent": self.stack[-1] if self.stack else None}
        self.nodes.append(node)
        if tag not in {"meta", "link", "img", "input", "br", "hr"}:
            self.stack.append(len(self.nodes) - 1)
        if tag == "script" and attrs.get("id") == "tailwind-config":
            self.script = len(self.stack)

    def handle_endtag(self, tag):
        if tag == "script":
            self.script = None
        for position in range(len(self.stack) - 1, -1, -1):
            if self.nodes[self.stack[position]]["tag"] == tag:
                del self.stack[position:]
                break

    def handle_startendtag(self, tag, attrs):
        self.handle_starttag(tag, attrs)
        if tag not in {"meta", "link", "img", "input", "br", "hr"}:
            self.handle_endtag(tag)

    def handle_data(self, data):
        if self.script is not None:
            self.config += data


def read_layout(path):
    parser = LayoutParser()
    parser.feed(path.read_text(encoding="utf-8-sig"))
    marker = parser.config.index('"colors"')
    tokens, _ = json.JSONDecoder().raw_decode(parser.config[marker - 1:])
    structural = []
    for node in parser.nodes:
        if node["tag"] in {"header", "main", "aside", "footer"} or node["id"] or any(word in node["class"] for word in {"grid-cols-12", "col-span-", "max-w-[", "h-10", "pt-10", "pb-[72px]"}):
            parent = parser.nodes[node["parent"]] if node["parent"] is not None else None
            structural.append({**node, "parent": None if parent is None else {"tag": parent["tag"], "id": parent["id"], "class": parent["class"]}})
    return {"file": str(path), "tokens": tokens, "structure": structural}


if __name__ == "__main__":
    args = argparse.ArgumentParser()
    args.add_argument("path", type=Path)
    selected = args.parse_args()
    print(json.dumps(read_layout(selected.path), ensure_ascii=False, indent=2))
