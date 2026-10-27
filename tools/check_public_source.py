"""Inspect the Git index before publishing source; never print secret values."""
import re
import subprocess
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
FORBIDDEN_PREFIXES=('.tools/','.cache/','models/','data/','dist/','reports/',
    'game/Spire408/Data/','game/Spire408/Rendered/','game/images/','game/scenes/',
    'game/fonts/','game/themes/','game/shaders/','game/src/','game/addons/mega_text/')
FORBIDDEN_SUFFIXES={'.dll','.exe','.onnx','.safetensors','.pck','.pdb','.save','.bin','.zip','.pt','.pth'}
SECRETS=[re.compile(rb'gh[pousr]_[A-Za-z0-9]{30,}'),re.compile(rb'github_pat_[A-Za-z0-9_]{40,}'),
         re.compile(rb'-----BEGIN (?:RSA |OPENSSH |EC )?PRIVATE KEY-----'),re.compile(rb'sk-[A-Za-z0-9_-]{35,}')]

def main():
    names=subprocess.check_output(['git','ls-files','-z'],cwd=ROOT).decode().split('\0')
    names=[n for n in names if n]
    if not names:raise ValueError('No staged/tracked source files')
    total=0
    for name in names:
        path=ROOT/name
        if name.startswith(FORBIDDEN_PREFIXES) or Path(name).suffix.lower() in FORBIDDEN_SUFFIXES:
            raise ValueError('Forbidden generated/native/private file in index: '+name)
        if any(segment in ('node_modules','bin','obj','.godot','__pycache__') for segment in Path(name).parts):
            raise ValueError('Development cache in index: '+name)
        data=path.read_bytes();total+=len(data)
        if len(data)>20_000_000:raise ValueError('Oversized source file: '+name)
        if path.suffix.lower() not in ('.png','.res') and any(p.search(data) for p in SECRETS):
            raise ValueError('Potential credential in index: '+name)
    print(f'Public source check passed: {len(names)} files, {total} bytes; no game binaries, banks, weights, saves, caches or detected credentials.')

if __name__=='__main__':main()
