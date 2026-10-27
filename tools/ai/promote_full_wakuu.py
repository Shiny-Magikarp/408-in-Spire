"""Promote the measured experimental candidate, preserving the previous model."""
from pathlib import Path
import hashlib,json,os,shutil

ROOT=Path(__file__).resolve().parents[2]

def main():
    evaluation=json.loads((ROOT/'reports/wakuu-full-evaluation.json').read_text(encoding='utf-8'))
    training=json.loads((ROOT/'reports/wakuu-full-training.json').read_text(encoding='utf-8'))
    previous=evaluation['comparisons']['dev1'];selected=evaluation['comparisons']['candidate_fp16']
    full=evaluation['comparisons']['candidate_fp32']
    assert selected['false_awards']<=previous['false_awards'] and selected['correct']>=previous['correct'], 'Candidate regressed on the separate authored assertions'
    assert all(a['award']==b['award'] and a['predicted']==b['predicted'] for a,b in zip(selected['details'],full['details'])), 'FP16 decision differs from measured FP32'
    source=ROOT/'.tools/ai/full-train/wakuu.fp16.onnx'
    assert hashlib.sha256(source.read_bytes()).hexdigest()==evaluation['candidate_sha256']
    target=ROOT/'models/wakuu'
    backup=ROOT/'.tools/ai/model-backups/dev1-before-080';backup.mkdir(parents=True,exist_ok=True)
    for name in ('wakuu.onnx','model.json','vocab.txt','NOTICE.txt'):
        if (target/name).exists() and not (backup/name).exists():shutil.copy2(target/name,backup/name)
    manifest=json.loads((target/'model.json').read_text(encoding='utf-8'))
    questions=json.loads((ROOT/'game/Spire408/Data/essays.json').read_text(encoding='utf-8'))['questions']
    manifest.update(model_version='wakuu-nli-408-dev2',precision='fp16',bytes=source.stat().st_size,
        sha256=evaluation['candidate_sha256'],training_pairs=training['train_pairs'],
        development_pairs=training['development_pairs'],supported_question_entries=len(questions),
        experimental_semantic_questions=sum(bool(q.get('experimental')) for q in questions),max_tokens=192,
        limitation='Experimental checkpoint matching. Synthetic and authored development evidence, not independently human-marked essays, code execution or proof checking.')
    staged=target/'wakuu.onnx.next';shutil.copy2(source,staged)
    next_manifest=target/'model.json.next';next_manifest.write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    os.replace(staged,target/'wakuu.onnx');os.replace(next_manifest,target/'model.json')
    notice=(ROOT/'tools/ai/MODEL-NOTICE.txt').read_text(encoding='utf-8')
    notice+=f"\nAdditional modifications, 2026-10-27: dev2 adapts the final two Transformer layers, pooler and classifier on {training['train_pairs']:,} local reference-derived/authored pairs. Question-disjoint synthetic development/test and separate authored assertion comparisons are recorded. Not a validated general examiner. Generated question-derived datasets and weights are excluded from the public source repository.\n"
    (target/'NOTICE.txt').write_text(notice,encoding='utf-8')
    print('Promoted',manifest['model_version'],manifest['bytes'],manifest['sha256'])

if __name__=='__main__':main()
