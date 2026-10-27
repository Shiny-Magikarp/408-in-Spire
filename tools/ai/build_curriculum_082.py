"""Paraphrase curriculum; previously inspected 0.8.1 items are now historical.

This deliberately retires the old test split from use as independent evidence.
The fresh 0.8.2 holdout is not read here.
"""
from pathlib import Path
import collections,hashlib,json,random,re
from marking_families_081 import rows
from marking_challenge import rows as diagnostic

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'.tools/ai/curriculum-082'
ALIASES=[
 ('TLB','快表'),('快表','TLB'),('主存','内存'),('CPU','处理器'),('DMA','直接存储器访问'),
 ('NAT','网络地址转换'),('DNS','域名系统'),('MAC 地址','网卡地址'),('MAC地址','网卡地址'),
 ('IEEE754','IEEE 754'),('中断服务程序','中断处理程序'),('页框','物理页框'),
 ('虚页号','虚拟页号'),('结点','节点'),('非递减','允许相等的升序'),
 ('互不相同','两两不等'),('两两不等','互不相同'),('有效位','有效标志'),
 ('块内偏移','块内地址'),('存取','访问'),('压栈','入栈'),('弹栈','出栈'),
 ('循环等待','环路等待'),('不能','不可以'),('不存在','没有'),('能够','可以'),
 ('首先','先'),('随后','然后'),('因此','所以'),('O(n²)','O(n^2)'),
 ('相对次序','先后次序'),('不稳定的排序算法','不能保证相等关键字先后次序的排序算法'),
 ('空间复杂度 O(1)','额外空间为常数'),('空间复杂度为O(1)','只需要常数额外空间'),
 ('时间复杂度 O(n)','耗时为线性阶'),('时间复杂度为O(n²)','需要平方级时间'),
 ('1 个','一个'),('2 个','两个'),('3 个','三个'),('1个','一个'),('2个','两个'),
]

def variants(text):
    results=[]
    for left,right in ALIASES:
        if left in text:results.append(text.replace(left,right))
    return list(dict.fromkeys(results))[:8]

def main():
    OUT.mkdir(parents=True,exist_ok=True);rng=random.Random(408082)
    historical=rows('train')+rows('dev')+rows('test')+diagnostic()
    # Split by knowledge family for selection; all these examples are historical
    # development material, so no result on them is reported as fresh testing.
    for i,r in enumerate(historical):
        r.setdefault('family_id','old-diagnostic-'+r['id'].rsplit('-',1)[0])
    families=sorted({r['family_id'] for r in historical},key=lambda f:hashlib.sha256(('082/'+f).encode()).hexdigest())
    validation_families=set(families[::5])
    authored_train=[r for r in historical if r['family_id'] not in validation_families]
    authored_dev=[r for r in historical if r['family_id'] in validation_families]
    reserved={(r['premise'],r['hypothesis']) for r in authored_dev}
    replay=json.loads((ROOT/'.tools/ai/full-corpus/train.json').read_text(encoding='utf-8'))
    rng.shuffle(replay);replay=[r for r in replay if (r['premise'],r['hypothesis']) not in reserved][:3600]
    augmented=[]
    for r in replay+authored_train:
        if r['label']==2:
            for text in variants(r['premise']):
                if (text,r['hypothesis']) not in reserved:
                    augmented.append(dict(r,premise=text,origin='meaning_preserving_terminology_variant'))
            for text in variants(r['hypothesis']):
                if (r['premise'],text) not in reserved:
                    augmented.append(dict(r,hypothesis=text,origin='reference_terminology_variant'))
    rng.shuffle(augmented);augmented=augmented[:5000]
    original=json.loads((ROOT/'tools/ai/domain-train.json').read_text(encoding='utf-8'))
    original=[r for r in original if (r['premise'],r['hypothesis']) not in reserved]
    training=replay+augmented+authored_train*10+original*2
    assert not ({(r['premise'],r['hypothesis']) for r in training}&reserved)
    manifest=dict(seed=408082,old_081_test_retired=True,
        scope='Historical development examples plus generated meaning-preserving terminology variants. Fresh 082 holdout is never read by this builder.',
        train_families=sorted({r['family_id'] for r in authored_train}),development_families=sorted(validation_families),splits={})
    for name,data in [('train',training),('dev',authored_dev)]:
        rng.shuffle(data);path=OUT/(name+'.json');path.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
        manifest['splits'][name]=dict(rows=len(data),labels=dict(collections.Counter(r['label'] for r in data)),sha256=hashlib.sha256(path.read_bytes()).hexdigest())
    (OUT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(manifest['splits'],ensure_ascii=False))

if __name__=='__main__':main()
