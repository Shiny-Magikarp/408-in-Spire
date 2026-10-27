"""A small authored paraphrase/partial-answer refinement, with replay data.

Keep the separate test families out of both gradient training and selection.
"""
import collections
import hashlib
import json
from pathlib import Path
import random
from marking_families_081 import rows

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'.tools/ai/refinement-081'

def key(row):return (row['premise'],row['hypothesis'])

def main():
    OUT.mkdir(parents=True,exist_ok=True)
    rng=random.Random(408081)
    old=json.loads((ROOT/'.tools/ai/full-corpus/train.json').read_text(encoding='utf-8'))
    train=rows('train');dev=rows('dev');test=rows('test')
    reserved={key(r) for r in dev+test}
    replay=[]
    for label in (0,1,2):
        pool=[r for r in old if r['label']==label and key(r) not in reserved]
        rng.shuffle(pool);replay+=pool[:800]
    augmented=[]
    for r in train:
        augmented.append(r)
        if r['label']==2:
            augmented.append(dict(r,premise='根据题设条件，'+r['premise'],origin='authored_paraphrase_prefix'))
    for family in {r['family_id'] for r in train}:
        r=next(r for r in train if r['family_id']==family)
        augmented.append(dict(r,premise='这个结论不成立：'+r['hypothesis'],label=0,origin='explicit_denial'))
    authored=json.loads((ROOT/'tools/ai/domain-train.json').read_text(encoding='utf-8'))
    training=replay+augmented*12+authored*2
    validation=dev+json.loads((ROOT/'tools/ai/domain-eval.json').read_text(encoding='utf-8'))
    assert not ({key(r) for r in training}&reserved),'Training/evaluation text collision'
    assert {r['family_id'] for r in train}.isdisjoint(r['family_id'] for r in dev+test)
    manifest=dict(model_version='wakuu-nli-408-dev3-candidate',seed=408081,
        train_families=sorted({r['family_id'] for r in train}),dev_families=sorted({r['family_id'] for r in dev}),
        test_families=sorted({r['family_id'] for r in test}),
        scope='Authored short claims and multi-sentence explanations; no arbitrary code/proof evaluation. Test families are not used for training or checkpoint selection.',splits={})
    for name,data in [('train',training),('dev',validation),('test',test)]:
        rng.shuffle(data);path=OUT/(name+'.json')
        path.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
        manifest['splits'][name]=dict(rows=len(data),labels=dict(collections.Counter(r['label'] for r in data)),sha256=hashlib.sha256(path.read_bytes()).hexdigest())
    (OUT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(manifest['splits'],ensure_ascii=False))

if __name__=='__main__':main()
