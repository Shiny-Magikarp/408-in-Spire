"""Independent, reproducible calculations for specific pinned choice questions."""
import heapq
import json
import math
from pathlib import Path
import re


def calculate():
    # Inputs are transcribed from the question, never its stored answer/explanation.
    result={
      '408-0757d429c8f8d9dd1c68c3e8':('hex',0x1000+5-3,'5次Push、3次Pop，栈向高地址增长，净增2个单元。'),
      '408-0de86c990d6d53e99a73cd8b':('number',8,'49%11=5，5、6、7已占用，首个空位为8。'),
      '408-0700c57ff439d707ba903755':('number',50000/4*1000/10**9*100,'中断频率=50000B/s÷4B；乘每次1000周期再除1GHz，换算百分数。'),
      '408-07c9f93d04a85204f785c4b1':('range',(3*2**28,2**30-1),'30位字节空间，RAM占低3/4，ROM为最高1/4。'),
      '408-08e3bcf2bf842219db8e2cb7':('pair',(2,20-9-3),'按字编址，512字偏移9位，8组索引3位，Tag为8位，2路并行比较。'),
      '408-09b6f9747ee681f1642676e2':('hex',0x1188+(0x2222&255),'寻址特征(2222H>>8)&3=2，使用X2=1188H，形式地址22H。'),
      '408-00739878c5a20d6fe0829764':('number',2,'两类设备各需一种驱动，数量不随相同设备台数增加。'),
      '408-06ce6d99ea981e89ce2417d1':('sequence',(1,3,2),'同到达且非抢占，短作业优先2h、3h、5h使完成时间总和最小。'),
      '408-0afc21f7c1db0f8a6de61487':('number',(11-1)//(3-1),'确保无死锁需m>=n(k-1)+1，因此n<=5。'),
      '408-0c3f5200c888304add02b184':('number',60/3000/10*1000,'3000转/分为50转/秒，每圈20ms，10个等长扇区每个传输2ms。'),
      '408-0ea31f1e01368beb72337125':('number',(2+1)/1,'响应比=(等待2h+服务1h)/服务1h=3。'),
      '408-0f649e45091621de3fad84f3':('number',1,'缓冲区互斥信号量初值1，与生产者/消费者数量和容量无关。'),
      '408-062de8c98734f724bb4d985b':('number',400/(400+5*20)*100,'OSI七层中除应用层与物理层外共5层，各引入20B。'),
      '408-07f177f056026d170959013f':('pair',(3*3,16-(1+2+4)),'慢启动窗口1、2、4、8KB需要3RTT；此前收到1+2+4=7KB，接收缓冲余9KB。'),
      '408-0a2ab26c76878a1c0007ba07':('time_us',2*(100+100)/(200000*1000)*10**6,'最远站间经Hub单程200m，往返400m，传播速率2e8m/s，结果2us。'),
      '408-0fd942d8b4d9e74ae8e9276f':('number',100000/10**8*20*10**6/1000,'传播时延1ms等于发送时延，20Mbit/s×1ms=20kbit。'),
      '408-c6d6516fd8d184f92cebb8ae':('number',2+(30-2)*3+1,'三对角矩阵首行2项，前28个完整中间行各3项，第30行对角线为第2项，零起始下标87。'),
      '408-781f84b06ed124d38520705f':('number',sum(range(8,13)),'上三角按行，m6,6前5行分别有12、11、10、9、8项，零起始下标50。'),
      '408-2810210e0b0e005abbfcd76d':('number',sum(range(1,7))+2-1,'上三角按列，m2,7前6列有21项，再加行内偏移1，零起始下标22。'),
      '408-de5504c9b6a3313307ad4c6c':('number',1+20*(4-1)+10*(3-1)+1*(2-1),'树边数n-1等于总度数，叶数=1+Σ(d-1)nd=82。'),
      '408-2e81a67910a77a91f602d7a2':('number',2**7-1-2*8,'问最多：树高7且前6层满，第6层8个叶子使第7层缺16个结点。'),
      '408-ce094053e3f7f2b1caf564e7':('number',2011-116+1,'孩子兄弟表示中，每个原树分支结点的孩子链末尾无右孩子，加根结点，共1896。'),
      '408-391bd9477ba12ff5c674ee71':('number',math.comb(7-1,2)+1,'最多非连通图是K6加一个孤立点，有15边；16边保证任意简单无向图连通。'),
      '408-469d7a28e51779b66fdcf016':('number',3+4+math.ceil((2*16-4*3-3*4)/2),'度数总和32；已用24，其他顶点度至多2，至少另需4个顶点。'),
      '408-e832b47f3d0924f6dbb9b0e0':('number',3*3+2*3*1,'按空树高0：高1为1形，高2为3形；高3子树高度(2,2)/(2,1)/(1,2)，共15。'),
      '408-51657da08fa983164a521c97':('number',90/1.5+10,'CPU速度提高50%使CPU时间除以1.5，加不变10s的I/O时间，得到70s。'),
    }
    weights=[0,2,3,4,5,6,7];heapq.heapify(weights);wpl=0
    while len(weights)>1:
        combined=sum(heapq.heappop(weights) for _ in range(3));wpl+=combined;heapq.heappush(weights,combined)
    result['408-67f5c2e670ba96785910611d']=('number',wpl,'三叉哈夫曼树补一个0权虚叶，依次合并0,2,3→5；4,5,5→14；6,7,14→27，WPL=46。')
    result['408-04e40feb1614fae0c16bc8c8']=('cdma',(1,-1,1,-1),'要求a、b、c三个码片向量两两正交，不仅分别与a正交。')
    return result


def plain(text):
    return re.sub(r'\s+','',text).replace('$','')


def matches(kind,value,text):
    text=plain(text)
    if kind=='time_us':
        text=text.replace('\\','').replace('mu','u').replace('μ','u')
        match=re.fullmatch(r'(\d+(?:\.\d+)?)(ns|us|ms|s)',text)
        return bool(match) and abs(float(match[1])*{'ns':.001,'us':1,'ms':1000,'s':1000000}[match[2]]-value)<1e-7
    if kind=='hex':
        match=re.fullmatch(r'([0-9A-Fa-f]+)H',text)
        return bool(match) and int(match[1],16)==value
    if kind=='range':
        endpoints=re.findall(r'([0-9A-Fa-f]+)H',text)
        return len(endpoints)==2 and tuple(int(x,16) for x in endpoints)==value
    if kind in ('pair','sequence'):
        return tuple(map(int,re.findall(r'\d+',text)))==value
    if kind=='cdma':
        nums=list(map(int,re.findall(r'-?\d+',text)))
        if len(nums)!=8:return False
        vectors=[value,nums[:4],nums[4:]]
        return all(sum(x*y for x,y in zip(a,b))==0 for i,a in enumerate(vectors) for b in vectors[i+1:])
    nums=re.findall(r'-?\d+(?:\.\d+)?',text)
    return len(nums)==1 and abs(float(nums[0])-value)<1e-7


def verify(questions):
    by_id={q['id']:q for q in questions}
    lock=json.loads(Path(__file__).with_name('choice-calculation-lock.json').read_text(encoding='utf-8'))
    records=[]
    for id,(kind,value,proof) in calculate().items():
        q=by_id[id]
        if q['revision']!=lock[id]:raise ValueError('Changed independent calculation input: '+id)
        choices=[letter for letter,text in q['options'].items() if matches(kind,value,text)]
        if len(choices)!=1 or choices[0]!=q['answer']:
            raise ValueError(f'Independent calculation mismatch: {id}; computed={value}; choices={choices}; stored={q["answer"]}')
        records.append(dict(id=id,method='independent_calculation',answer=choices[0],computed=value,verification=proof))
    return records
