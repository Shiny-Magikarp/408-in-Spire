"""Publish aggregate evidence and authored test decisions, never imported banks."""
import hashlib
import json
from pathlib import Path
from holdout_082 import rows

ROOT = Path(__file__).resolve().parents[2]


def metrics(report):
    cases = report['records']
    positive = sum(r['label'] == 2 for r in cases)
    true_awards = sum(r['award'] and r['label'] == 2 for r in cases)
    false_awards = sum(r['award'] and r['label'] != 2 for r in cases)
    return dict(total=len(cases), positive=positive, negative=len(cases)-positive,
                true_awards=true_awards, false_awards=false_awards, missed_correct=positive-true_awards,
                correct_rejections=len(cases)-positive-false_awards,
                decision_accuracy=sum(r['correct'] for r in cases)/len(cases),
                correct_answer_recall=true_awards/positive,
                false_award_rate=false_awards/(len(cases)-positive))


def main():
    read = lambda path: json.loads((ROOT / path).read_text(encoding='utf-8-sig'))
    before = read('reports/wakuu-holdout-082-baseline.json')
    after = read('reports/wakuu-holdout-082-candidate.json')
    fp32 = read('reports/wakuu-holdout-082-fp32.json')
    model = read('models/wakuu/model.json')
    training = read('reports/wakuu-curriculum-training-082.json')
    canonical = read('reports/wakuu-canonical-integration.json')
    old = read('docs/evaluations/wakuu-081.json')['reference_integration']
    with (ROOT / 'models/wakuu/wakuu.onnx').open('rb') as stream:
        assert hashlib.file_digest(stream, 'sha256').hexdigest() == model['sha256'] == after['model_sha256']
    assert before['dataset_sha256'] == after['dataset_sha256'] == fp32['dataset_sha256']
    expected = [(r['id'], r['label']) for r in rows()]
    assert all([(r['id'], r['label']) for r in result['records']] == expected for result in (before, after, fp32))
    mismatch = sum(a['award'] != b['award'] for a, b in zip(after['records'], fp32['records']))
    records = [dict(id=b['id'], label=b['label'], expected_award=b['label']==2,
                    baseline_award=a['award'], dev3_award=b['award'],
                    dev3_entailment=b['entailment'], dev3_contradiction=b['contradiction'])
               for a, b in zip(before['records'], after['records'])]
    result = dict(version='0.8.2', date='2026-10-27', model_version=model['model_version'],
                  model_sha256=model['sha256'], model_bytes=model['bytes'],
                  parent_model_version=model['parent_model_version'], parent_sha256=model['parent_sha256'],
                  weights_changed=True, scoring_version='passages-081',
                  entailment_threshold=.85, contradiction_ceiling=.1,
                  dataset=dict(source='tools/ai/holdout_082.py', items=100, positive=48, negative=52,
                               subjects=4, knowledge_families=24, prompt_attacks=4,
                               sha256=after['dataset_sha256'],
                               used_for_gradients_or_epoch_or_threshold_selection=False,
                               used_for_final_promotion_check=True,
                               nature='Project-authored new wording, not independent human-marked student essays. Knowledge can overlap prior domain training. Now a fixed regression set.'),
                  baseline=metrics(before), improved=metrics(after),
                  fp16_fp32_award_mismatches=mismatch,
                  training=dict(pairs=training['train_pairs'], development_pairs=training['development_pairs'],
                                seed=training['seed'], learning_rate=training['learning_rate'], batch_size=12,
                                train_last_layers=training['train_last_layers'], epochs=training['epochs'],
                                selected_epoch=training['selected']['epoch'], history=training['history'],
                                data=training['corpus']['splits'], old_081_test_retired_to_development=True,
                                scope=training['corpus']['scope']),
                  reference_integration=dict(questions=canonical['questions'],
                                             baseline_mean=old['improved_mean'], baseline_minimum=old['improved_minimum'],
                                             improved_mean=canonical['mean'], improved_minimum=canonical['minimum'],
                                             scope=canonical['scope']),
                  limitations=['Small project-authored wording test; no independent human student answer labels.',
                               'Four false awards and four missed correct answers remain.',
                               'Arbitrary code, proofs and diagrams are not verified; process review remains required.'],
                  records=records)
    output = ROOT / 'docs/evaluations/wakuu-082.json'
    output.write_text(json.dumps(result, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    print(json.dumps(dict(baseline=result['baseline'], improved=result['improved'],
                          fp16_fp32_award_mismatches=mismatch)))


if __name__ == '__main__':
    main()
