"""Merge preserved essays and explicitly scoped, deterministic practice rubrics."""
from collections import Counter
import hashlib
import heapq
import json
import math
from pathlib import Path
import copy
import sys
from essay_calculations import rubrics as extended_rubrics
from essay_scope import POLICY as scope_policy, validate_exclusions
sys.path.insert(0, str(Path(__file__).resolve().parent/'ai'))
from full_essay_rubrics import parts as semantic_parts, POLICY_VERSION

ROOT = Path(__file__).resolve().parents[1]
NAMES = dict(ds='数据结构', co='计算机组成原理', os='操作系统', cn='计算机网络')

def number(prompt, value, unit='', tolerance=0):
    return dict(prompt=prompt, kind='number', expected=value, unit=unit, tolerance=tolerance,
                reference=f'{value}{unit}', accepted=[])

def exact(prompt, *answers):
    return dict(prompt=prompt, kind='exact', reference=answers[0], accepted=list(answers))

def chapter(subject, topic):
    if subject == 'co':
        if any(x in topic.lower() for x in ('cache','存储','虚存')): return '3'
        if any(x in topic for x in ('浮点','补码','溢出','数据表示','计算易错')): return '2'
        if any(x in topic for x in ('流水','中断','异常','CPU','数据通路','微程序')): return '5'
        if '总线' in topic: return '6'
        if 'I/O' in topic or 'DMA' in topic: return '7'
        return '4'
    if subject == 'ds':
        if any(x in topic for x in ('生成树','图','路径')): return '6'
        if any(x in topic for x in ('树','哈夫曼','遍历')) and '图' not in topic: return '5'
        if any(x in topic for x in ('散列','二分','中位数','主元素','选择算法','查找')): return '7'
        if any(x in topic for x in ('栈','队列','表达式转换')): return '3'
        return '2'
    if subject == 'os':
        if any(x in topic for x in ('文件','索引','位示图','磁盘')): return '4'
        if any(x in topic for x in ('存储','分页','页面','缺页','调页','分段','虚拟')): return '3'
        return '2'
    if any(x in topic for x in ('子网','路由','IP','NAT','VLSM','ARP')): return '4'
    if any(x in topic for x in ('TCP','拥塞')): return '5'
    if any(x in topic for x in ('DHCP','DNS','HTTP','Web','访问','通信过程','配置')): return '6'
    return '3'

# Each tuple was checked against the pinned question's explicit parameters.
# These grade address-field calculations only; they do not grade prose/code.
CACHES = {
 'q-2015-44':(20,16384,8,2), 'q-2016-43':(20,16384,32,4),
 'q-2018-44':(32,65536,128,2), 'q-2020-43':(28,65536,16,1),
 'q-2021-44':(32,8192,32,1), 'q-2024-44':(24,8192,16,1),
 'q-2025-44':(32,65536,32,4), 'mock01-43':(32,32768,64,2),
 'mock02-43':(24,8192,16,1), 'mock03-43':(30,131072,64,0),
 'mock04-44':(20,16384,32,1), 'mock07-43':(24,16384,32,1),
 'mock08-44':(26,32768,16,2),
}

def rubrics():
    result = {}
    for id, (bits, size, block, ways) in CACHES.items():
        lines = size // block; groups = lines // ways if ways else 1
        offset = int(math.log2(block)); index = int(math.log2(groups)); tag = bits-offset-index
        prompt = '第(1)问直接映射 Cache 的块内偏移位数' if id=='q-2020-43' else '指令 Cache 的块内偏移位数' if id=='q-2021-44' else 'Cache 块内偏移位数'
        result[id] = [number(prompt,offset),
                      number('行号/组号字段位数（全相联填 0）',index), number('物理地址中 Tag 位数',tag),
                      number('Cache 总行数',lines), number('Cache 组数（全相联按 1 组计）',groups)]
    result['mock01-43'] += [number('串行模型的平均访问时间，单位 ns',16,'ns'),number('包含数据、Tag 和有效位的总存储位数',271872,'bit')]
    result['mock02-43'] += [number('主存总块数（填写整数）',1048576),number('平均访问时间，单位 ns',34,'ns'),number('3000 次访问的总时间，单位 ns',102000,'ns')]
    result['mock03-43'] += [number('平均访问时间，单位 ns',5,'ns')]
    result['mock04-44'] += [number('地址 582BAH 对应的 Cache 行号',21),number('该地址对应的 Tag（十进制）',22),number('该地址的块内偏移（十进制）',26),number('平均访问时间，单位 ns',18,'ns')]
    result['mock08-44'] += [number('原题八次访问的 Cache 缺失次数',5)]
    result['mock04-43'] = [exact('x 的 IEEE754 单精度机器码（十六进制）','40B80000H','0x40B80000','40B80000'),
                           exact('y 的机器码（十六进制）','C0280000H','0xC0280000','C0280000'),
                           number('x+y 的十进制真值',3.125),exact('x+y 的机器码（十六进制）','40480000H','0x40480000','40480000')]
    result['mock07-44'] = [number('5 段机时钟周期，单位 ns',4,'ns'),number('5 段机执行 12 条无相关指令的总时间，单位 ns',64,'ns'),
                           number('无停顿 5 段机相对串行的加速比',2.625),number('8 段机执行 12 条无相关指令的总时间，单位 ns',38,'ns'),
                           number('有 3 次单拍停顿时，5 段机总时间（ns）',76,'ns'),number('相同停顿下 8 段机总时间（ns）',44,'ns')]
    result['mock09-43'] = [number('所需 SRAM 芯片片数',4),number('每组并联芯片数',2),number('芯片组数',2),
                           number('片内地址线根数',12),exact('访问 5B6CH 时的片内地址（十六进制）','B6CH','0xB6C','B6C')]
    result['mock09-44'] = [exact('I1 的 EA（十六进制）','0300H','300H','0x300','0300','300'),
                           exact('I2 的 EA（十六进制）','0500H','500H','0x500','0500','500'),
                           exact('I4 的转移目标（十六进制）','2016H','0x2016','2016'),number('I2 执行阶段访存次数',2),number('四条指令执行阶段总访存次数',4)]
    result['mock06-42'] = [number('哈夫曼树 WPL',296),number('1260 字符报文的哈夫曼编码长度，单位 bit',2960,'bit'),number('比 3 位等长编码节省的 bit 数',820,'bit')]
    result['mock08-42'] = [number('按原题第 n 小定义得到的中位数',9)]
    result['mock09-42'] = [exact('后序遍历（空格可省略）','G D E B H I F C A','GDEBHIFCA'),number('度为 2 的结点数',3),number('树高（根高度为 1）',4)]
    result['mock10-42'] = [number('每 100 字符的 WPL',234),number('5000 字符报文的编码总 bit 数',11700,'bit'),number('比 3 位等长编码节省的 bit 数',3300,'bit')]
    result['mock04-46'] = [number('LRU 缺页次数',11),number('OPT 缺页次数',7)]
    result['mock06-46'] = [number('单个索引块可容纳的指针数',256),number('单文件最大长度（B）',65802*1024,'B'),
                           number('逻辑块 7 的磁盘读次数',1),number('逻辑块 5000 的磁盘读次数',3),number('增加三次间接指针带来的容量增量（B）',256**3*1024,'B')]
    result['mock09-46'] = [number('页内偏移位数',13),number('虚页号位数',19),number('最大页表项数',524288),
                           number('物理页框号最少位数',13),number('最大页表空间（B）',2097152,'B'),
                           number('逻辑地址 0x1A2B3C 的物理地址（十进制）',125756),number('平均有效访问时间（ns）',140,'ns')]
    result['mock01-47'] = [exact('划分后的子网掩码','255.255.255.192','/26'),number('每个子网的可用主机数',62),
                           exact('主机 X 所在网络（带前缀）','192.168.10.64/26'),exact('主机 Y 所在网络（带前缀）','192.168.10.128/26')]
    result['mock09-47'] = [exact('部门 A 的网络/前缀','203.0.113.0/25'),exact('部门 B 的网络/前缀','203.0.113.128/26'),
                           exact('部门 C 的网络/前缀','203.0.113.192/27'),exact('剩余未分配网络/前缀','203.0.113.224/27')]
    result['mock10-47'] = [number('DNS 解析耗时（ms）',50,'ms'),number('TLS1.2 直连总时延（ms）',370,'ms'),number('TLS1.3 直连总时延（ms）',290,'ms'),
                           number('CDN 命中总时延（ms）',110,'ms'),number('CDN 未命中总时延（ms）',170,'ms')]
    return result

def build():
    originals=json.loads((ROOT/'data/essay-originals.json').read_text(encoding='utf-8'))['questions']
    upstream=json.loads((ROOT/'data/essay-upstream.json').read_text(encoding='utf-8'))['questions']
    reviewed=rubrics() | extended_rubrics(upstream); questions=list(originals)
    quality=json.loads((ROOT/'tools/essay-quality-policy.json').read_text(encoding='utf-8'))
    upstream=copy.deepcopy(upstream)
    for q in upstream:
        if q['id'] not in quality['corrections']: continue
        correction=quality['corrections'][q['id']]
        for field,patch in correction['fields'].items():
            if hashlib.sha256(q[field].encode()).hexdigest()!=patch['expected_sha256']:
                raise ValueError('Stale essay erratum: '+q['id']+'/'+field)
            q[field]=patch['replacement']
    for q in upstream:
        parts=reviewed.get(q['id'])
        auto=parts is not None
        if not auto: parts=semantic_parts(q['answerText'])
        # Practice scores concern these listed check points only, not original
        # exam scoring. Full original questions/answers remain available.
        for i,p in enumerate(parts): p.update(id=f'p{i+1}',points=100//len(parts)+(i<100%len(parts)))
        stem=q['question']
        if q.get('code') and q['code']['text'] not in stem: stem+='\n```'+q['code']['lang']+'\n'+q['code']['text']+'\n```'
        item=dict(id='cs408-'+q['id'],subject=q['subject'],subject_name=NAMES[q['subject']],chapter=chapter(q['subject'],q['topic']),
                  title=(str(q['year'])+' 年 · ' if q['year'] else q['origin']+' · ')+str(q['number'])+' · '+q['topic'],
                  topic=q['topic'],stem=stem,reference_answer=q['answerText'],explanation=q['explanation'],original_points=q['score'],
                  grading_scope='只核对下列计算结果并换算为练习分；原卷的过程、代码与论述请对照完整解析自查。' if auto else '实验性语义核分：按参考结论逐点给练习分，尚无独立人工阅卷验证。不会执行代码或检验证明；复杂过程与待复核项不发额外卡牌、遗物。',
                  parts=parts,exam=dict(kind='practice' if q['source']=='mock' else 'past_exam'),
                  source=dict(**q['_source'],upstream_id=q['id'],kind=q['source'],year=q['year'],number=q['number'],
                              name=q.get('origin') or f"{q['year']} 年真题" ,adapted=q['source']=='adapted'))
        if not auto:
            import re
            item.update(experimental=True,rubric_policy=POLICY_VERSION,
                requires_process_review=bool(q.get('code') or re.search(r'算法|代码|证明|反例|画出|绘制|画图|C ?程序|C 语言|信号量|PV|P、V',q['question'])))
        item['revision']=hashlib.sha256(json.dumps(item,ensure_ascii=False,sort_keys=True).encode()).hexdigest()[:16]
        if q['id'] in quality['corrections']:
            item['local_review']={'verification':quality['corrections'][q['id']]['verification'],'reviewed_on':quality['reviewed_on']}
        questions.append(item)
    by_id={q['id']:q for q in questions}
    for id,revision in quality['legacy_grading_revisions'].items():
        if by_id[id]['revision']!=revision: raise ValueError('Legacy event rubric changed; explicit migration required: '+id)
    semantic_policy=json.loads((ROOT/'tools/essay-semantic-policy.json').read_text(encoding='utf-8'))
    for id,revision in semantic_policy['unchanged_grading_revisions'].items():
        if by_id[id]['revision']!=revision: raise ValueError('0.7.0 rubric changed without migration: '+id)
    compatible_by_bank={semantic_policy['compatible_bank']:semantic_policy['unchanged_grading_revisions']}
    removed=validate_exclusions(upstream,questions)
    for id,revision in scope_policy['compatible_grading_revisions'].items():
        if by_id[id]['revision']!=revision: raise ValueError('0.8.2 rubric changed without migration: '+id)
    compatible_by_bank[scope_policy['compatible_bank']]=scope_policy['compatible_grading_revisions']
    questions=[q for q in questions if q['id'] not in removed]
    target=ROOT/'game/Spire408/Data/essays.json';target.parent.mkdir(parents=True,exist_ok=True)
    target.write_text(json.dumps(dict(schema=3,compatible_from=quality['compatible_from']+list(compatible_by_bank),
        legacy_grading_revisions=quality['legacy_grading_revisions'],compatible_rubrics_by_bank=compatible_by_bank,
        removed_grading_revisions={id:by_id[id]['revision'] for id in sorted(removed)},questions=questions),ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    counts=dict(total=len(questions),imported=len(questions)-len(originals),source_imported=len(upstream),excluded_drawing=len(removed),original=len(originals),
                automatic=len(questions),review_only=0,deterministic_imported=sum(not q.get('experimental') and q['id'].startswith('cs408-') for q in questions),
                experimental_semantic=sum(bool(q.get('experimental')) for q in questions),
                subjects=dict(Counter(q['subject'] for q in questions)))
    counts['local_errata']=list(quality['corrections'])
    counts['new_result_scopes']=[id for id in extended_rubrics(json.loads((ROOT/'data/essay-upstream.json').read_text(encoding='utf-8'))['questions']) if 'cs408-'+id not in removed]
    counts['excluded_questions']={id:rule['reason'] for id,rule in scope_policy['exclusions'].items()}
    (ROOT/'reports/essay-bank-report.json').write_text(json.dumps(counts,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(counts,ensure_ascii=True))

if __name__=='__main__': build()
