"""Promote only the fixed, measured model; preserve installed development weights."""
from pathlib import Path
import hashlib,json,math,os,shutil

ROOT=Path(__file__).resolve().parents[2]

def measure(report):
    rows=report['records'];p=[r for r in rows if r['label']==2];n=[r for r in rows if r['label']!=2]
    return dict(total=len(rows),positive=len(p),negative=len(n),true_awards=sum(r['award'] for r in p),
        false_awards=sum(r['award'] for r in n),decision_correct=sum(r['correct'] for r in rows))

def main():
    reports=ROOT/'reports'
    baseline=json.loads((reports/'wakuu-holdout-082-baseline.json').read_text())
    candidate=json.loads((reports/'wakuu-holdout-082-candidate.json').read_text())
    fp32=json.loads((reports/'wakuu-holdout-082-fp32.json').read_text())
    for other in (baseline,fp32):
        assert [(r['id'],r['label']) for r in other['records']]==[(r['id'],r['label']) for r in candidate['records']]
        assert other['dataset_sha256']==candidate['dataset_sha256']
    b=measure(baseline);c=measure(candidate)
    assert c['true_awards']>=math.ceil(.8*c['positive']), 'Correct-answer recall below 80%'
    assert c['false_awards']<=b['false_awards'], 'False awards increased relative to dev2'
    assert c['decision_correct']>=b['decision_correct'], 'Overall marking regressed'
    assert all(a['award']==b['award'] for a,b in zip(candidate['records'],fp32['records'])), 'FP16 changes measured scoring decisions'
    log=(reports/'core-tests-082-candidate.log').read_text(encoding='utf-8-sig')
    assert 'checks passed.' in log and 'Unhandled exception' not in log, 'Candidate core checks did not complete'
    stage=ROOT/'.tools/ai/curriculum-train-082/candidate'
    source=stage/'wakuu.onnx';checksum=hashlib.sha256(source.read_bytes()).hexdigest()
    assert checksum==candidate['model_sha256'], 'Candidate bytes differ from the measured model'
    target=ROOT/'models/wakuu';backup=ROOT/'.tools/ai/model-backups/dev2-before-082'
    backup.mkdir(parents=True,exist_ok=True)
    for name in ('wakuu.onnx','model.json','vocab.txt','NOTICE.txt'):
        if not (backup/name).exists():shutil.copy2(target/name,backup/name)
    parent=json.loads((backup/'model.json').read_text(encoding='utf-8'))
    assert parent['model_version']=='wakuu-nli-408-dev2', 'Baseline backup must remain dev2'
    assert hashlib.sha256((backup/'wakuu.onnx').read_bytes()).hexdigest()==baseline['model_sha256']==parent['sha256']
    training=json.loads((reports/'wakuu-curriculum-training-082.json').read_text(encoding='utf-8'))
    manifest=json.loads((stage/'model.json').read_text(encoding='utf-8'))
    manifest.update(model_version='wakuu-nli-408-dev3',parent_model_version='wakuu-nli-408-dev2',
        parent_sha256=parent['sha256'],training_pairs=training['train_pairs'],development_pairs=training['development_pairs'],
        train_last_layers=training['train_last_layers'],evaluation_items=c['total'],correct_answer_recall=c['true_awards']/c['positive'],
        false_award_rate=c['false_awards']/c['negative'],
        limitation='Fresh project-authored wording holdout only, not independent human student essays. Prior knowledge overlaps domain training. Arbitrary code and proofs are not verified.')
    shutil.copy2(source,target/'wakuu.onnx.next')
    (target/'model.json.next').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    os.replace(target/'wakuu.onnx.next',target/'wakuu.onnx');os.replace(target/'model.json.next',target/'model.json')
    notice=(backup/'NOTICE.txt').read_text(encoding='utf-8')
    notice+='\nAdditional modifications, 2026-10-27: dev3 fine-tunes the last four Transformer layers, pooler and classifier on 7,128 replay/paraphrase/historical development pairs; selected on 48 development pairs. Previous 0.8.1 tests were retired to development, and a new 100-item wording holdout measured correct-answer recall and false awards. FP16 scoring decisions matched FP32. This is experimental checkpoint matching, not verified code/proof grading.\n'
    (target/'NOTICE.txt').write_text(notice,encoding='utf-8')
    print('Promoted',manifest['model_version'],checksum,c)

if __name__=='__main__':main()
