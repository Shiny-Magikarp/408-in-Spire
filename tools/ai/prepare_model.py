"""Download a pinned, hash-checked Chinese NLI model for local fine-tuning."""
from pathlib import Path
import hashlib
import json
import urllib.request

ROOT = Path(__file__).resolve().parents[2]
REPO = 'IDEA-CCNL/Erlangshen-Roberta-110M-NLI'
REVISION = '864d25be3ce5e90d9193cb49d9fbd52722cdc6b0'
HASHES = {
    'config.json': 'bb64591cc2f1580f30f0b34d634eb96f7b5f95ebacb86078076381ed0afd6986',
    'vocab.txt': '45bbac6b341c319adc98a532532882e91a9cefc0329aa57bac9ae761c27b291c',
    'README.md': '8ee197acb40e6d3cfcfdc9d064fbf37aed6914d14531eb5e593ee01615c0dc69',
    'pytorch_model.bin': '3ee7de12d0dc27d21a3e4a8713e270fa431102c20d74351b876d76f9c8a30b03',
}
target = ROOT / '.tools/ai/base'
target.mkdir(parents=True, exist_ok=True)
for name in ('config.json', 'vocab.txt', 'README.md', 'pytorch_model.bin'):
    path = target / name
    if not path.exists():
        print('Downloading', name, flush=True)
        request = urllib.request.Request(f'https://huggingface.co/{REPO}/resolve/{REVISION}/{name}', headers={'User-Agent': 'Spire408-development/0.3'})
        with urllib.request.urlopen(request, timeout=120) as response, path.with_suffix('.download').open('wb') as output:
            while chunk := response.read(1024 * 1024): output.write(chunk)
        path.with_suffix('.download').replace(path)
    digest = hashlib.sha256(path.read_bytes()).hexdigest()
    if digest != HASHES[name]: raise ValueError('Base model checksum mismatch: ' + name)
    print(name, path.stat().st_size, digest, flush=True)
(target / 'source.json').write_text(json.dumps({'repository':REPO,'revision':REVISION,'sha256':HASHES}, indent=2), encoding='utf-8')
