"""Reviewed result-only rubrics. Code, diagrams and proof processes are not auto-scored."""
import collections
import hashlib
import itertools
import json
import math
from pathlib import Path
import re
import struct


def number(prompt, value, unit='', tolerance=0):
    return dict(prompt=prompt, kind='number', expected=value, unit=unit, tolerance=tolerance,
                reference=f'{value}{unit}', accepted=[])


def exact(prompt, *answers):
    return dict(prompt=prompt, kind='exact', reference=answers[0], accepted=list(answers))


def hexpart(prompt, value):
    return exact(prompt, f'{value:X}H', f'0x{value:X}', f'{value:X}', f'{value:08X}H', f'{value:08X}')


def faults(sequence, frames, mode):
    resident, hand, bits, count, trace = [], 0, [], 0, []
    for i, page in enumerate(sequence):
        victim = None
        hit = page in resident
        if hit:
            if mode == 'LRU': resident.remove(page); resident.append(page)
            if mode == 'CLOCK': bits[resident.index(page)] = 1
        else:
            count += 1
            if len(resident) < frames:
                resident.append(page); bits.append(1); hand = len(resident) % frames
            elif mode == 'CLOCK':
                while bits[hand]: bits[hand] = 0; hand = (hand + 1) % frames
                victim = resident[hand]; resident[hand] = page; bits[hand] = 1; hand = (hand + 1) % frames
            else:
                index = 0
                if mode == 'OPT':
                    future = sequence[i+1:]
                    index = max(range(frames), key=lambda j: future.index(resident[j]) if resident[j] in future else math.inf)
                victim = resident.pop(index); resident.append(page)
        trace.append(dict(page=page, hit=hit, victim=victim, resident=resident.copy()))
    return count, trace


def hash_table(keys, size, modulus, multiplier=1):
    table, probes = [None]*size, []
    for key in keys:
        start = key*multiplier % modulus
        for offset in range(size):
            slot = (start+offset) % size
            if table[slot] is None:
                table[slot] = key; probes.append(offset+1); break
        else: raise ValueError('Hash table full during insertion')
    failures = []
    for start in range(modulus):
        failures.append(next((offset+1 for offset in range(size) if table[(start+offset)%size] is None), size))
    return table, sum(probes)/len(probes), sum(failures)/len(failures)


def spanning_weights(vertices, edges):
    weights = []
    for chosen in itertools.combinations(edges, len(vertices)-1):
        parent = {v:v for v in vertices}
        def root(v):
            while parent[v] != v: v = parent[v]
            return v
        for a,b,_ in chosen:
            ra,rb = root(a),root(b)
            if ra == rb: break
            parent[ra] = rb
        else: weights.append(sum(w for _,_,w in chosen))
    return sorted(weights)


def postfix(expression, precedence):
    output, stack = [], []
    for token in re.findall(r'[A-Za-z]|[()+*/-]', expression):
        if token.isalpha(): output.append(token)
        elif token == '(': stack.append(token)
        elif token == ')':
            while stack[-1] != '(': output.append(stack.pop())
            stack.pop()
        else:
            while stack and stack[-1] != '(' and precedence[stack[-1]] >= precedence[token]: output.append(stack.pop())
            stack.append(token)
    return ''.join(output + stack[::-1])


def source_digest(q):
    return hashlib.sha256(json.dumps([q['question'],q.get('code')],ensure_ascii=False,sort_keys=True).encode()).hexdigest()


def rubrics(questions, check_lock=True):
    result = {}
    result['q-2009-43'] = [number('ROM 片内地址线根数',13), number('4K SRAM 片内地址线根数',12),number('第(3)问 4KB 用户区需 1K×4 SRAM 片数',8)]
    result['q-2010-43'] = [hexpart('取指后的 PC（十六进制）',0x2000+2),hexpart('位移 30H 的目标地址',0x2000+2+0x30),
                           hexpart('位移 E7H 的目标地址',0x2000+2+0xe7-256),hexpart('能转移到的最低地址',0x2000+2-128)]
    result['q-2010-44'] = [number('理想总时钟周期数',5+6-1),number('吞吐率（条/周期，小数，可填 3/5）',6/(5+6-1),tolerance=.001),
                           number('加速比',6*5/(5+6-1)),number('效率（填写百分数，如 60）',100*6/(5+6-1),'%')]
    result['q-2011-43'] = [number('地址转换平均耗时（ns）',10+.1*100,'ns'),number('数据访问平均耗时（ns）',20+.05*100,'ns'),number('一次访问总平均耗时（ns）',10+.1*100+20+.05*100,'ns')]
    result['q-2012-43'] = [hexpart('x 的 8 位补码（十六进制）',(-53)%256),hexpart('y 的 8 位补码',76),hexpart('-y 的 8 位补码',(-76)%256),
                           number('x+y 的真值',-53+76),hexpart('x-y 截断为 8 位后的机器码',(-53-76)%256),exact('x-y 是否溢出','是','溢出','发生负溢出','负溢出')]
    frame = 1024*768*24//8
    result['q-2012-44'] = [number('VRAM 最小容量（B）',frame,'B'),number('刷新读出带宽（B/s）',frame*60),number('总线最低频率（Hz，填写整数）',frame*60//4)]
    result['q-2013-43'] = [number('x+y 的十进制真值',.5),number('规格化后的阶码真值',0),exact('单符号位存储的 8 位尾数（二进制）','01000000','01000000B')]
    result['q-2014-43'] = [number('最多指令条数',2**4),number('最多通用寄存器数',2**2),number('最多寻址方式数',2**2),
                           number('直接寻址范围（B）',2**8,'B'),number('一次间接寻址取操作数的访存次数（不含取指）',2),
                           number('目标相对本条指令首地址的最小位移（字节）',2-128),number('目标相对本条指令首地址的最大位移（字节）',2+127)]
    result['q-2014-44'] = [number('无冲突总周期数',5+10-1),number('吞吐率（条/周期，可填 5/7）',10/14,tolerance=.005),
                           number('无冲突加速比',50/14,tolerance=.005),number('包含两次转移损失的总周期数',14+2*3)]
    result['q-2015-43'] = [number('控存最多微指令条数',2**6),number('控存容量（bit）',2**6*26,'bit'),number('操作控制字段位数',26-2-6),number('最多微命令种数',6*(2**3-1))]
    result['q-2020-44'] = [number('不转发的总周期数（按原题 WB 后一拍读约定）',13),number('有转发的总周期数',8),number('加速比',13/8)]
    f32 = lambda x: struct.unpack('>I',struct.pack('>f',x))[0]
    result['q-2021-43'] = [hexpart('x 的 IEEE754 单精度机器码',f32(7.75)),hexpart('y 的机器码',f32(-2.375)),hexpart('x+y 的机器码',f32(7.75-2.375))]
    result['q-2022-43'] = [number('页内偏移位数',12),number('虚拟页号位数',32-12),number('TLB 组数',16//2),number('TLB Tag 位数',20-3),
                           number('Cache 组数',32768//64//4),number('Cache 块内偏移位数',6),number('Cache 组号位数',7),number('Cache Tag 位数',24-6-7),number('EAT（ns）',10+.02*100+20+.05*100,'ns')]
    result['q-2023-43'] = [number('直接寻址空间（B）',2**10,'B'),number('一次间接寻址空间（B）',2**16,'B'),number('取指阶段访存次数',1),
                           hexpart('第一个相对转移的目标地址',0x2000+2+0x2a),hexpart('第二个相对转移的目标地址',0x2008+2+0x3d6-1024),hexpart('变址有效地址',0xa00+6)]
    result['q-2023-44'] = [number('ROM 片内地址线根数',13),number('SRAM 片内地址线根数',12),number('扩展 4KB 需要的 4K×4 SRAM 片数',2)]
    result['q-2025-43'] = [hexpart('-105 的 8 位补码',(-105)%256),hexpart('+86 的 8 位补码',86),hexpart('-105+86 的 8 位结果',(-105+86)%256),
                           exact('异号加法结果的双符号位','11'),hexpart('-105-86 的 8 位结果',(-105-86)%256),exact('同负号加法结果的双符号位','10')]
    result['q-2026-43'] = [number('取指周期拍数',3),number('执行周期拍数',5),number('总时钟周期数',3+5),number('总执行时间（ns）',(3+5)*20,'ns')]
    result['q-2026-44'] = [number('无定向时 I2 停顿拍数',2),number('无定向时 I3 停顿拍数',2),number('无定向总周期数',5+3-1+2+2),
                           number('有定向时 I2 停顿拍数',1),number('有定向时 I3 停顿拍数',0),number('有定向总周期数',5+3-1+1)]
    result['mock05-43'] = [number('总线带宽（B/s，十进制）',4*10**9),number('主存带宽（B/s，十进制）',4*10**7),
                           number('读 1000 字总时间（ns）',1000*100,'ns'),number('有 Cache 的平均访存时间（ns）',5+.1*100,'ns'),number('有 Cache 的 1000 次总时间（ns）',1000*(5+.1*100),'ns')]
    micro = 2+20*4; control=18+math.ceil(math.log2(6+1)); address=math.ceil(math.log2(micro)); width=control+2+address
    result['mock05-44'] = [number('微指令总数',micro),number('下址字段位数',address),number('操作控制字段位数',control),number('微指令字长',width),number('按实际条数配置的控存容量（bit）',micro*width,'bit')]
    result['mock06-43'] = [number('理想总时间（ns）',(5+8-1)*2,'ns'),number('无转发的气泡数',2),number('无转发总时间（ns）',(5+8-1+2)*2,'ns'),
                           number('有转发的气泡数',1),number('有转发总时间（ns）',(5+8-1+1)*2,'ns')]
    result['mock08-43'] = [exact('-128 的 8 位补码（二进制）','10000000','10000000B'),hexpart('1.5 的 IEEE754 单精度机器码',f32(1.5)),
                           hexpart('-0 的 IEEE754 单精度机器码',0x80000000),number('存储器扩展所需芯片片数',512//128*32//8),number('50MHz 总线带宽（MB/s，十进制）',50*32//8)]
    result['mock10-43'] = [number('无异常总拍数',5+6-1),number('有异常的完成拍号',27+5+3-2),number('异常导致增加的拍数',33-10)]

    # Page algorithms are simulated from the question's sequence, not copied from its answer.
    page_cases = {'q-2009-45':[('FIFO',3),('LRU',3)], 'q-2010-45':[('FIFO',3),('FIFO',4)],
                  'q-2018-45':[('FIFO',3),('LRU',3),('FIFO',4)],'q-2020-45':[('LRU',3)],
                  'q-2023-46':[('FIFO',3),('OPT',3),('FIFO',4)],'q-2026-45':[('CLOCK',3)],'mock01-45':[('LRU',3)]}
    by_id={q['id']:q for q in questions}
    for id, algorithms in page_cases.items():
        stem=by_id[id]['question']
        seq_match=re.search(r'(?:序列[^：:]*[：:])\s*([\d\s,，、]+)',stem)
        if not seq_match: raise ValueError('Page sequence missing: '+id)
        sequence=list(map(int,re.findall(r'\d+',seq_match[1])))
        result[id]=[number(f'{mode} 在 {frames} 个页框下的缺页次数',faults(sequence,frames,mode)[0]) for mode,frames in algorithms]
        if id in ('mock01-45','q-2026-45'):
            mode,frames=algorithms[0]
            result[id].append(number('缺页率（填写百分数，如 75）',100*faults(sequence,frames,mode)[0]/len(sequence),'%',.05))
    result['q-2010-46'] = [number('每个间接块的地址项数',4096//4),number('最大文件长度（B，1KB=1024B）',(10+1024+1024**2)*4096,'B'),number('1MB 文件占用总块数（含索引块）',256+1)]
    result['q-2012-45'] = [number('页内偏移位数',12),number('最多页表项数',2**20),number('无快表且无缺页时访存次数',2),number('平均有效访问时间（ns）',10+100+.1*100,'ns')]
    result['q-2013-45'] = [number('每段最大长度（B）',2**24,'B'),number('最多段数',2**8),hexpart('物理地址（十六进制）',0x12*4096+0x1a5),number('无快表的访存次数',3),number('快表命中的访存次数',1)]
    result['mock03-46'] = [number('每个索引块的地址项数',8192//4),number('最大文件长度（B）',(10+2048+2048**2)*8192,'B'),number('访问偏移100MB处需读盘块数（不含inode）',3)]

    for id,keys,size,mod,mul in [('q-2010-41',[19,14,23,1,68,20,84,27,55,11,10,79],13,13,1),('q-2011-42',[7,8,30,11,18,9,14],7,7,3)]:
        table,success,failure=hash_table(keys,size,mod,mul)
        result[id]=[number('成功 ASL（可填分数，保留至少两位小数）',success,tolerance=.005),number('失败 ASL（满表时至多探测一圈）',failure,tolerance=.005)]
    result['q-2018-41']=[number('v1 到 v2 的最短距离',10),number('v1 到 v3 的最短距离',min(10+50,30+20)),number('v1 到 v4 的最短距离',30),number('v1 到 v5 的最短距离',min(100,10+10,30+60))]
    result['mock03-41']=[exact('按结点名的先序遍历（空格可省略）','A B C D E F G'),number('B 叶深度（根为0）',1),number('D 叶深度',2),number('F 叶深度',3),number('G 叶深度',3),number('WPL',6*1+3*2+8*3+5*3)]
    result['mock03-42']=[exact('序列(0,1,2,3,4)是否为拓扑序列','是','1','正确'),exact('序列(1,0,2,3,4)是否为拓扑序列','否','0','错误'),exact('矩阵成对检查的时间复杂度','O(n^2)','O(n²)'),exact('邻接表配合入度的时间复杂度','O(n+e)','O(n+m)')]
    expression='(A+B)/(C-D)*E-F'
    result['mock07-41']=[exact('通常优先级的后缀式（保留运算符）',postfix(expression,{'+':1,'-':1,'*':2,'/':2})),exact('加减优先级高于乘除时的后缀式',postfix(expression,{'+':2,'-':2,'*':1,'/':1}))]
    edges=[('A','B',1),('B','D',2),('A','C',3),('B','C',4),('C','E',5),('B','E',5),('C','D',6),('D','E',7),('C','F',8),('E','F',9)]
    weights=spanning_weights(list('ABCDEF'),edges)
    result['mock07-42']=[number('MST 的总权值',weights[0]),exact('MST 是否唯一','否','不唯一','不是唯一')]
    edges=[(1,2,1),(1,3,3),(2,3,2),(2,4,6),(3,4,4),(3,5,5),(4,5,7),(4,6,3),(5,6,8)]
    weights=spanning_weights(list(range(1,7)),edges)
    result['mock10-41']=[number('MST 的总权值',weights[0]),number('严格次小生成树的权值',next(w for w in weights if w>weights[0])),exact('MST 是否唯一','是','唯一')]
    result['q-2010-47']=[number('发送一帧的时延（ms）',1000*8/10**7*1000,'ms'),number('一帧至收到ACK的总时间（ms）',.8+2*10,'ms'),number('信道利用率（百分数）',100*.8/(.8+20),'%',.01)]
    result['mock06-47']=[number('非持久连接总时间（ms）',4*(2*50+20),'ms'),number('持久非流水线总时间（ms）',50+4*(50+20),'ms'),number('流水线总时间（ms）',50+(50+20)+(50+3*20),'ms')]
    import ipaddress
    network=ipaddress.ip_network('192.168.10.0/24');start=int(network.network_address);subnets=[]
    for hosts in (50,25,10,5):
        bits=math.ceil(math.log2(hosts+2));subnet=ipaddress.ip_network((start,32-bits));subnets.append(subnet);start+=subnet.num_addresses
    result['mock04-47']=[exact(f'部门 {label} 网络/前缀',str(subnet)) for label,subnet in zip('ABCD',subnets)]
    result['mock04-47'].append(exact('覆盖四个子网的最长聚合前缀','192.168.10.0/25'))

    if check_lock:
        lock=json.loads(Path(__file__).with_name('essay-calculation-lock.json').read_text(encoding='utf-8'))
        for id in result:
            if source_digest(by_id[id]) != lock[id]: raise ValueError('Changed calculation source: '+id)
    return result
