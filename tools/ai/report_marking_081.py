"""Publish aggregate, reproducible metrics for authored non-student examples."""
import hashlib
import json
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]

def metrics(rows):
    positive=[r for r in rows if r['label']==2]
    negative=[r for r in rows if r['label']!=2]
    tp=sum(r['award'] for r in positive);fp=sum(r['award'] for r in negative)
    return dict(total=len(rows),positive=len(positive),negative=len(negative),true_awards=tp,false_awards=fp,
        missed_correct=len(positive)-tp,correct_rejections=len(negative)-fp,
        decision_accuracy=(tp+len(negative)-fp)/len(rows),correct_answer_recall=tp/len(positive),
        false_award_rate=fp/len(negative),precision=tp/max(1,tp+fp))

def main():
    reports=ROOT/'reports'
    before=json.loads((reports/'wakuu-heldout-081-baseline.json').read_text())['records']
    after=json.loads((reports/'wakuu-heldout-081-improved.json').read_text())['records']
    assert [(r['id'],r['label']) for r in before]==[(r['id'],r['label']) for r in after]
    reference=json.loads((reports/'wakuu-passages-081.json').read_text())['records']
    model=json.loads((ROOT/'models/wakuu/model.json').read_text(encoding='utf-8'))
    target=ROOT/'docs/evaluations/wakuu-081.json';target.parent.mkdir(exist_ok=True)
    result=dict(version='0.8.1',date='2026-10-27',model_version=model['model_version'],model_sha256=model['sha256'],weights_changed=False,
        scoring_version='passages-081',entailment_threshold=.85,contradiction_ceiling=.1,
        dataset=dict(source='tools/ai/marking_families_081.py',split='test',families=24,items=96,
            sha256=hashlib.sha256((ROOT/'.tools/ai/marking-081/test.json').read_bytes()).hexdigest(),
            nature='Project-authored factual claims and paraphrases, not independent human-marked student essays.',
            used_for_this_round_training_or_checkpoint_selection=False),
        baseline=metrics(before),improved=metrics(after),
        reference_integration=dict(questions=124,baseline_mean=76.45161290322581,baseline_minimum=16,
            improved_mean=sum(r['score'] for r in reference)/len(reference),improved_minimum=min(r['score'] for r in reference),
            scope='Reference text integration only; not accuracy.'),
        refinement_candidate=dict(promoted=False,reason='No improvement on authored development marking decisions and one additional false award in existing diagnostic regression.'),
        decisions=[{k:r[k] for k in ('id','label','award','correct')} for r in after])
    target.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(result['improved'],ensure_ascii=False))

if __name__=='__main__':main()
