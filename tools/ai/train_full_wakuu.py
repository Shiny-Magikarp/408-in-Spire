"""Train a local candidate. Promotion is a separate, measured operation."""
import os
os.environ['USE_TF']='0'
os.environ['USE_FLAX']='0'
import argparse
import hashlib
import json
from pathlib import Path
import random
import sys
import time
import numpy as np
import torch
from transformers import BertConfig, BertForSequenceClassification, BertTokenizer

ROOT=Path(__file__).resolve().parents[2]
sys.path.append(str(ROOT/'.tools/ai-python'))
parser=argparse.ArgumentParser()
parser.add_argument('--epochs',type=int,default=3)
parser.add_argument('--batch-size',type=int,default=8)
parser.add_argument('--learning-rate',type=float,default=2e-5)
parser.add_argument('--checkpoint',default='.tools/ai/train/checkpoint')
parser.add_argument('--corpus',default='.tools/ai/full-corpus')
parser.add_argument('--workdir',default='.tools/ai/full-train')
parser.add_argument('--report',default='reports/wakuu-full-training.json')
parser.add_argument('--model-version',default='wakuu-nli-408-dev2')
parser.add_argument('--seed',type=int,default=408080)
args=parser.parse_args()
random.seed(args.seed);np.random.seed(args.seed);torch.manual_seed(args.seed)
torch.set_num_threads(4)
device=torch.device('cuda' if torch.cuda.is_available() else 'cpu')
work=ROOT/args.workdir;work.mkdir(parents=True,exist_ok=True)
corpus=ROOT/args.corpus
train=json.loads((corpus/'train.json').read_text(encoding='utf-8'))
dev=json.loads((corpus/'dev.json').read_text(encoding='utf-8'))
tokenizer=BertTokenizer(vocab_file=str(ROOT/'.tools/ai/base/vocab.txt'),do_lower_case=True)
config=BertConfig.from_pretrained(ROOT/args.checkpoint,local_files_only=True)
config._attn_implementation='eager'
model=BertForSequenceClassification.from_pretrained(ROOT/args.checkpoint,config=config,local_files_only=True).to(device)
for name,p in model.named_parameters():
    p.requires_grad=any(s in name for s in ('encoder.layer.10.','encoder.layer.11.','pooler','classifier'))

def encode(rows):
    return tokenizer([r['premise'] for r in rows],[r['hypothesis'] for r in rows],padding=True,truncation=True,max_length=192,return_tensors='pt')

def evaluate(rows):
    model.eval(); probabilities=[]
    with torch.inference_mode():
        for start in range(0,len(rows),16):
            batch={k:v.to(device) for k,v in encode(rows[start:start+16]).items()}
            with torch.autocast(device_type=device.type,dtype=torch.float16,enabled=device.type=='cuda'):
                probabilities.extend(model(**batch).logits.float().softmax(-1).cpu().tolist())
    awards=[p[2]>=.85 and p[0]<.1 for p in probabilities]
    return dict(correct=sum(int(np.argmax(p))==r['label'] for p,r in zip(probabilities,rows)),total=len(rows),
        false_awards=sum(a and r['label']!=2 for a,r in zip(awards,rows)),
        accepted_correct=sum(a and r['label']==2 for a,r in zip(awards,rows)),
        positive_count=sum(r['label']==2 for r in rows),
        negative_count=sum(r['label']!=2 for r in rows))

started=time.perf_counter();baseline=evaluate(dev)
print('BASELINE',json.dumps(baseline),flush=True)
optimizer=torch.optim.AdamW([p for p in model.parameters() if p.requires_grad],lr=args.learning_rate,weight_decay=.01)
scaler=torch.amp.GradScaler('cuda',enabled=device.type=='cuda')
history=[];best=None
for epoch in range(args.epochs):
    model.train();order=list(range(len(train)));random.shuffle(order);losses=[]
    for start in range(0,len(order),args.batch_size):
        rows=[train[i] for i in order[start:start+args.batch_size]]
        batch={k:v.to(device) for k,v in encode(rows).items()}
        labels=torch.tensor([r['label'] for r in rows],device=device)
        optimizer.zero_grad(set_to_none=True)
        with torch.autocast(device_type=device.type,dtype=torch.float16,enabled=device.type=='cuda'):
            loss=model(**batch,labels=labels).loss
        scaler.scale(loss).backward();scaler.unscale_(optimizer)
        torch.nn.utils.clip_grad_norm_(model.parameters(),1.)
        scaler.step(optimizer);scaler.update();losses.append(loss.item())
        if start % (args.batch_size*100)==0:
            print(f'PROGRESS epoch={epoch+1} rows={start}/{len(train)} loss={np.mean(losses):.4f} seconds={time.perf_counter()-started:.1f}',flush=True)
    metrics=evaluate(dev)
    record=dict(epoch=epoch+1,loss=float(np.mean(losses)),**metrics)
    history.append(record);print('EPOCH',json.dumps(record),flush=True)
    # Penalize false awards heavily without choosing an all-abstain model.
    score=metrics['accepted_correct']-8*metrics['false_awards']
    if best is None or score>best[0]:
        best=(score,record)
        model.save_pretrained(work/'checkpoint',safe_serialization=True)
model=BertForSequenceClassification.from_pretrained(work/'checkpoint',local_files_only=True,attn_implementation='eager').cpu().eval()
dummy=encode(train[:2])
fp32=work/'wakuu.fp32.onnx'
torch.onnx.export(model,(dummy['input_ids'],dummy['attention_mask'],dummy['token_type_ids']),str(fp32),
    input_names=['input_ids','attention_mask','token_type_ids'],output_names=['logits'],opset_version=17,
    dynamic_axes={name:{0:'batch',1:'sequence'} for name in ('input_ids','attention_mask','token_type_ids')},do_constant_folding=True)
import onnx
from onnxruntime.transformers.float16 import convert_float_to_float16
graph=convert_float_to_float16(onnx.load(str(fp32)),keep_io_types=True,disable_shape_infer=True)
onnx.save(graph,str(work/'wakuu.fp16.onnx'))
report=dict(model_version=args.model_version,seed=args.seed,device=str(device),epochs=args.epochs,learning_rate=args.learning_rate,checkpoint=args.checkpoint,
    train_pairs=len(train),development_pairs=len(dev),baseline=baseline,history=history,selected=best[1],
    elapsed_seconds=time.perf_counter()-started,corpus=json.loads((corpus/'manifest.json').read_text(encoding='utf-8')),
    limitation='Question-disjoint synthetic reference-derived development; independent authored challenge and test split are evaluated after selection.')
(ROOT/args.report).write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('EXPORTED',work/'wakuu.fp16.onnx',flush=True)
