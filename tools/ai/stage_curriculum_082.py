"""Stage both precisions for comparison; never replace the production model."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil

ROOT = Path(__file__).resolve().parents[2]
BASELINE_SHA = 'cd1a655635fec6f9bb33e5b95271212fc550c816489271939ea422bdc73356c3'


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--baseline-only', action='store_true')
    args = parser.parse_args()
    backup = ROOT / '.tools/ai/model-backups/dev2-before-082'
    production = ROOT / 'models/wakuu'
    if not (backup / 'model.json').exists():
        current = json.loads((production / 'model.json').read_text(encoding='utf-8'))
        assert current['model_version'] == 'wakuu-nli-408-dev2', 'Prepare a dev2 baseline before this workflow'
        assert hashlib.sha256((production / 'wakuu.onnx').read_bytes()).hexdigest() == BASELINE_SHA
        backup.mkdir(parents=True, exist_ok=True)
        for name in ('wakuu.onnx', 'model.json', 'vocab.txt', 'NOTICE.txt'):
            shutil.copy2(production / name, backup / name)
    parent = json.loads((backup / 'model.json').read_text(encoding='utf-8'))
    assert parent['model_version'] == 'wakuu-nli-408-dev2'
    assert hashlib.sha256((backup / 'wakuu.onnx').read_bytes()).hexdigest() == parent['sha256'] == BASELINE_SHA
    if args.baseline_only:
        print('Verified fixed dev2 baseline:', backup)
        return
    training = json.loads((ROOT / 'reports/wakuu-curriculum-training-082.json').read_text(encoding='utf-8'))
    work = ROOT / '.tools/ai/curriculum-train-082'
    for precision, directory in [('fp16', 'candidate'), ('fp32', 'candidate-fp32')]:
        stage = work / directory
        stage.mkdir(parents=True, exist_ok=True)
        output = stage / 'wakuu.onnx'
        shutil.copy2(work / f'wakuu.{precision}.onnx', output)
        shutil.copy2(backup / 'vocab.txt', stage / 'vocab.txt')
        manifest = dict(parent, model_version=f'wakuu-nli-408-dev3-candidate-{precision}',
                        precision=precision, bytes=output.stat().st_size,
                        sha256=hashlib.sha256(output.read_bytes()).hexdigest(),
                        training_pairs=training['train_pairs'], development_pairs=training['development_pairs'])
        (stage / 'model.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
        print('Staged', precision, manifest['sha256'])


if __name__ == '__main__':
    main()
