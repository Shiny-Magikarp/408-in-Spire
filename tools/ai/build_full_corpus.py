"""Local reference-derived corpus with question-disjoint synthetic splits."""
import hashlib
import json
import random
import re
from pathlib import Path
from full_essay_rubrics import checkpoints, POLICY_VERSION

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / '.tools/ai/full-corpus'

def mutations(text):
    variants = []
    swaps = [('O(1)', 'O(n)'), ('O(n)', 'O(n²)'), ('先进先出', '后进先出'),
             ('非负', '负'), ('先资源、后互斥', '先互斥、后资源'),
             ('先资源后互斥', '先互斥后资源'), ('大于', '小于'), ('小于', '大于'),
             ('同步', '互不等待'), ('逻辑地址', '物理地址'), ('互斥', '不需要互斥'),
             ('不能', '可以'), ('必须', '无需'), ('无需', '必须'),
             ('非抢占', '抢占'), ('不变', '不断增加')]
    for left, right in swaps:
        if left in text:
            variants.append(text.replace(left, right, 1))
    # Change a result literal, avoiding subquestion numbering and variable names.
    match = re.search(r'(?<![A-Za-z\d(（])\d+(?:\.\d+)?(?=\s*(?:个|位|字节|次|条|页|ns|ms|MB|=|，|。|；|$))', text)
    if match:
        number = float(match[0])+1
        variants.append(text[:match.start()]+format(number,'g')+text[match.end():])
    return list(dict.fromkeys(v for v in variants if v != text))

def main():
    OUT.mkdir(parents=True,exist_ok=True)
    bank = json.loads((ROOT/'game/Spire408/Data/essays.json').read_text(encoding='utf-8'))
    splits = {name:[] for name in ('train','dev','test')}
    assignments = {}
    by_subject = {}
    for q in bank['questions']:
        by_subject.setdefault(q['subject'],[]).append(q)
    for subject, questions in by_subject.items():
        questions.sort(key=lambda q: hashlib.sha256(('408080/'+q['id']).encode()).hexdigest())
        for i,q in enumerate(questions):
            split = 'test' if i % 10 == 0 else 'dev' if i % 10 == 1 else 'train'
            assignments[q['id']] = split
            references = checkpoints(q.get('reference_answer') or '\n'.join(p['reference'] for p in q['parts']))
            for ref in references:
                base = dict(question_id=q['id'],subject=subject,hypothesis=ref)
                positives = [ref, '我的结论是：'+ref, ref.replace('因此','所以').replace('首先','先').replace('然后','随后').replace('结点','节点')]
                for text in dict.fromkeys(positives):
                    splits[split].append(dict(**base,premise=text,label=2,origin='reference_surface_variant'))
                negatives = mutations(ref)[:2]
                for text in negatives:
                    splits[split].append(dict(**base,premise=text,label=0,origin='literal_or_relation_counterfactual'))
                # Explicitly uncertain and missing content are neutral, not
                # contradictions. No arbitrary other reference is labelled false.
                splits[split].append(dict(**base,premise='我还没有完成这一步，无法给出结论。',label=1,origin='missing_answer'))
    original_train = json.loads((ROOT/'tools/ai/domain-train.json').read_text(encoding='utf-8'))
    original_dev = json.loads((ROOT/'tools/ai/domain-eval.json').read_text(encoding='utf-8'))
    splits['train'] += [dict(r,question_id='authored-domain',origin='authored-domain') for r in original_train]
    splits['dev'] += [dict(r,question_id='authored-domain-dev',origin='existing-development') for r in original_dev]
    from collections import Counter
    summary = dict(policy=POLICY_VERSION, question_count=len(assignments), assignments=assignments,
        limitation='Synthetic reference-derived labels; shared generation templates across splits. Not independent human exam answers.',splits={})
    for name,rows in splits.items():
        random.Random(408).shuffle(rows)
        path=OUT/(name+'.json')
        path.write_text(json.dumps(rows,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
        summary['splits'][name]=dict(rows=len(rows),labels=dict(Counter(r['label'] for r in rows)),sha256=hashlib.sha256(path.read_bytes()).hexdigest())
    (OUT/'manifest.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({k:v for k,v in summary.items() if k!='assignments'},ensure_ascii=False))

if __name__ == '__main__': main()
