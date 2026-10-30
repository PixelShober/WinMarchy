"""Checks what WinMarchy's self-test wrote for YASB, Starship and Fastfetch.

config.yaml goes against the JSON schema YASB publishes for the release WinMarchy installs; the
stylesheet must not contain "//" (YASB's CSS engine cuts the rest of the line); starship.toml and
the Fastfetch config must parse.

    python scripts/check-yasb.py <yasb schema.json> <self-test folder>
"""
import glob
import json
import os
import sys
import tomllib

import jsonschema
import yaml

schema_path, folder = sys.argv[1], sys.argv[2]
validator = jsonschema.Draft202012Validator(json.load(open(schema_path, encoding="utf-8")))
failed = False

for path in sorted(glob.glob(os.path.join(folder, "yasb-*.yaml"))):
    errors = sorted(validator.iter_errors(yaml.safe_load(open(path, encoding="utf-8"))), key=lambda e: list(e.path))
    for e in errors[:20]:
        print(f"::error file={os.path.basename(path)}::{'/'.join(map(str, e.path))}: {e.message[:300]}")
    failed |= bool(errors)
    print(f"{os.path.basename(path)}: {'FAILED' if errors else 'OK'}")

for path in sorted(glob.glob(os.path.join(folder, "yasb-*.css"))):
    text = open(path, encoding="utf-8").read()
    problems = (["contains //"] if "//" in text else []) + (["unbalanced braces"] if text.count("{") != text.count("}") else [])
    for p in problems:
        print(f"::error file={os.path.basename(path)}::{p}")
    failed |= bool(problems)
    print(f"{os.path.basename(path)}: {'FAILED' if problems else 'OK'}")

for path in sorted(glob.glob(os.path.join(folder, "*.toml"))):
    tomllib.loads(open(path, encoding="utf-8").read())
    print(f"{os.path.basename(path)}: OK")

for path in sorted(glob.glob(os.path.join(folder, "*.jsonc"))):
    lines = open(path, encoding="utf-8").read().split("\n")
    json.loads("\n".join(l for l in lines if not l.lstrip().startswith("//")))
    print(f"{os.path.basename(path)}: OK")

sys.exit(1 if failed else 0)
