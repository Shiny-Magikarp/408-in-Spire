"""Copy exact upstream license notices, without redistributing question banks."""
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor
import shutil
import urllib.request

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'third_party/licenses'
SOURCES={
 'RitsuLib-MIT.txt':'https://raw.githubusercontent.com/BAKAOLC/STS2-RitsuLib/v0.6.9/LICENSE',
 'Harmony-MIT.txt':'https://raw.githubusercontent.com/pardeike/Harmony/master/LICENSE',
 'Godot-MIT.txt':'https://raw.githubusercontent.com/godotengine/godot/4.5.1-stable/LICENSE.txt',
 'MathJax-Apache-2.0.txt':'https://raw.githubusercontent.com/mathjax/MathJax-src/3.2.2/LICENSE',
 'sharp-Apache-2.0.txt':'https://raw.githubusercontent.com/lovell/sharp/v0.34.3/LICENSE',
 'Transformers-Apache-2.0.txt':'https://raw.githubusercontent.com/huggingface/transformers/v4.46.2/LICENSE',
 'PyTorch-BSD.txt':'https://raw.githubusercontent.com/pytorch/pytorch/v2.5.1/LICENSE',
}

def fetch(item):
    name,url=item
    data=urllib.request.urlopen(url,timeout=30).read()
    (OUT/name).write_bytes(data)
    return name,len(data)

def main():
    OUT.mkdir(parents=True,exist_ok=True)
    for source,name in [('data/UPSTREAM-LICENSE.txt','408-choice-ISC.txt'),
        ('data/CS408-ESSAYS-LICENSE.txt','cs408-MIT-and-content-note.txt'),
        ('models/wakuu/LICENSE.txt','Fengshenbang-Apache-2.0.txt'),
        ('tools/ai/ONNX-RUNTIME-LICENSE.txt','ONNX-Runtime-MIT.txt'),
        ('tools/ai/ONNX-THIRD-PARTY-NOTICES.txt','ONNX-Runtime-third-party.txt')]:
        shutil.copy2(ROOT/source,OUT/name)
    with ThreadPoolExecutor(max_workers=4) as pool:
        for name,size in pool.map(fetch,SOURCES.items()):print(name,size,flush=True)

if __name__=='__main__':main()
