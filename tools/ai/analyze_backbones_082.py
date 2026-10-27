"""Compare base/dev checkpoints on already-consumed development examples."""
import os
os.environ['USE_TF']='0';os.environ['USE_FLAX']='0'
from pathlib import Path
import json,sys
import numpy as np
import torch
from transformers import BertConfig,BertForSequenceClassification,BertTokenizer
from marking_families_081 import rows
from marking_challenge import rows as regression

ROOT=Path(__file__).resolve().parents[2]
torch.set_num_threads(4)
device='cuda' if torch.cuda.is_available() else 'cpu'
tokenizer=BertTokenizer(vocab_file=str(ROOT/'.tools/ai/base/vocab.txt'),do_lower_case=True)
groups={'known081':rows('test'),'development081':rows('dev'),'regression':regression()}
report={'scope':'All datasets here were inspected in previous rounds; development/diagnostic evidence only.','models':{}}
for name,path in [('base','.tools/ai/base'),('dev1','.tools/ai/train/checkpoint'),('dev2','.tools/ai/full-train/checkpoint')]:
    path=ROOT/path
    config=BertConfig.from_pretrained(path,local_files_only=True);config._attn_implementation='eager'
    if name=='base':
        model=BertForSequenceClassification(config)
        state=torch.load(path/'pytorch_model.bin',map_location='cpu',weights_only=True);state.pop('bert.embeddings.position_ids',None)
        model.load_state_dict(state);del state
    else:model=BertForSequenceClassification.from_pretrained(path,config=config,local_files_only=True)
    model.to(device).eval();measured={}
    for group,items in groups.items():
        probabilities=[]
        with torch.inference_mode():
            for start in range(0,len(items),16):
                batch=items[start:start+16]
                inputs=tokenizer([r['premise'] for r in batch],[r['hypothesis'] for r in batch],padding=True,truncation=True,max_length=192,return_tensors='pt')
                logits=model(**{k:v.to(device) for k,v in inputs.items()}).logits
                probabilities.extend(logits.softmax(-1).cpu().tolist())
        p=sum(r['label']==2 for r in items);n=len(items)-p
        tp=sum(r['label']==2 and v[2]>=.85 and v[0]<.1 for r,v in zip(items,probabilities))
        fp=sum(r['label']!=2 and v[2]>=.85 and v[0]<.1 for r,v in zip(items,probabilities))
        measured[group]=dict(total=len(items),positive=p,negative=n,true_awards=tp,false_awards=fp,
            details=[dict(id=r['id'],label=r['label'],probabilities=v) for r,v in zip(items,probabilities)])
        print(name,group,dict(tp=tp,p=p,fp=fp,n=n),flush=True)
    report['models'][name]=measured
    del model;torch.cuda.empty_cache()
(ROOT/'reports/wakuu-backbone-analysis-082.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
