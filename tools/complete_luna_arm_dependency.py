"""Supply the missing arm model from the supplied arm mesh; never edit the input tree."""
import pathlib
import re
import sys

root = pathlib.Path(sys.argv[1])
source = root / "agents/fys/luna/luna.vmdl"
text = source.read_text(encoding="utf-8-sig")

def remove_node(text, class_name, identity):
    pattern = re.compile(r'\{\s*_class\s*=\s*"' + re.escape(class_name) + '"')
    for match in pattern.finditer(text):
        start = match.start()
        depth, quoted, escaped = 0, False, False
        for end in range(start, len(text)):
            c = text[end]
            if quoted:
                if escaped: escaped = False
                elif c == "\\": escaped = True
                elif c == '"': quoted = False
            elif c == '"': quoted = True
            elif c == "{": depth += 1
            elif c == "}":
                depth -= 1
                if depth == 0:
                    block = text[start:end + 1]
                    if re.search(identity, block):
                        end += 1
                        if text[end:end + 1] == ",": end += 1
                        return text[:start] + text[end:]
                    break
    raise ValueError("Expected node missing: " + class_name + ":" + identity)

text = remove_node(text, "RenderMeshFile", r'name\s*=\s*"body"')
text = remove_node(text, "GenericGameData", r'game_class\s*=\s*"character_arm_config"')
# The separate arm mesh has no hair/skirt/body physics bones. The main model's
# physics stay byte-for-byte intact; those nodes do not belong to the arm fallback.
if '"JiggleBoneList"' in text:
    text = remove_node(text, "JiggleBoneList", r'JiggleBoneList')
text = re.sub(r'(meshes\s*=\s*\[\s*)"body"', r'\1"arms"', text)
out = root / "characters/fys/luna/arms/luna_arms.vmdl"
out.parent.mkdir(parents=True, exist_ok=True)
out.write_text(text, encoding="utf-8")
assert "luna_body.fbx" not in text and "luna_arms.fbx" in text
assert "default_glove_arm_model" not in text
print("Derived missing glove-arm dependency from supplied arms; original main model unchanged.")
