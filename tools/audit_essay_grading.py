"""Account for every previously self-reviewed essay and preserve honest grading scope."""
import collections
import hashlib
import json
from pathlib import Path
import re
from essay_calculations import rubrics

ROOT=Path(__file__).resolve().parents[1]


def main():
    source=json.loads((ROOT/'data/essay-upstream.json').read_text(encoding='utf-8'))['questions']
    bank=json.loads((ROOT/'game/Spire408/Data/essays.json').read_text(encoding='utf-8'))['questions']
    policy=json.loads((ROOT/'tools/essay-quality-policy.json').read_text(encoding='utf-8'))
    new=rubrics(source)
    by_id={q['id']:q for q in bank}
    semantic_policy=json.loads((ROOT/'tools/essay-semantic-policy.json').read_text(encoding='utf-8'))
    records=[]
    for q in source:
        if 'cs408-'+q['id'] in policy['legacy_grading_revisions']:continue
        if q['id'] in new:
            classification='deterministic_results_only'
            reason='逐项核验输入和计算结果，未自动评价原卷代码、证明或绘图过程。'
        elif re.search(r'证明|反例|画出|绘制|画图',q['question']):
            classification='needs_proof_or_diagram_validation'
            reason='需覆盖不同正确推理/图形表达和过程分，不能把参考文本相似度当作正确性。'
        elif re.search(r'算法|代码|C\+\+|C 语言|信号量|PV|P、V',q['question']):
            classification='needs_algorithm_or_concurrency_validation'
            reason='需验证功能、边界情况、复杂度或同步安全性；现有NLI模型不执行/证明算法。'
        else:
            classification='needs_rubric_and_independent_answer_labels'
            reason='需先核实参考答案、拆分评分点并收集不同正确/错误表达的独立标注测试集。'
        records.append(dict(id='cs408-'+q['id'],topic=q['topic'],classification=classification,reason=reason))
    assert len(records)==169
    for id,parts in new.items():
        item=by_id['cs408-'+id]
        assert len(parts)==len(item['parts']) and all(p['kind'] in ('number','exact') for p in item['parts'])
    for id,correction in policy['corrections'].items():
        item=by_id['cs408-'+id]
        for field,patch in correction['fields'].items():
            key={'answerText':'reference_answer','explanation':'explanation'}[field]
            assert item[key]==patch['replacement'],id
    semantic=[q for q in bank if q.get('experimental')]
    assert len(semantic)==124
    for q in semantic:
        assert all(p['kind']=='rubric' and 1<=len(p['checks'])<=128 and all(0<len(c)<=80 for c in p['checks']) for p in q['parts'])
        assert sum(p['points'] for p in q['parts'])==100
        assert q['rubric_policy']==semantic_policy['policy_version']
    report=dict(previous_review_only=169,new_result_scopes=len(new),new_semantic_scopes=len(semantic),remaining_review_only=sum(q['parts'][0]['kind']=='review' for q in bank),
                automatic=sum(q['parts'][0]['kind']!='review' for q in bank),classifications=dict(collections.Counter(r['classification'] for r in records)),
                previous_review_only_items=records,local_errata=policy['corrections'],model_weights_changed_in_080=True,
                model_weights_changed_in_081=False,model_weights_changed_in_082=True,
                validation_scope='204 questions have grading entries; 124 use experimental reference-derived semantic checks, not verified arbitrary code/proof grading.')
    (ROOT/'reports/essay-grading-audit.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({k:report[k] for k in ('previous_review_only','new_result_scopes','new_semantic_scopes','remaining_review_only','automatic','classifications')},ensure_ascii=True))


if __name__=='__main__':main()
