"""Locally adapt Chinese NLI, export ONNX candidates, and record measured results.

Training uses a small authored development corpus. It is not a claim of general
408 exam-marking reliability. No remote inference or remote Python code is used.
"""
from pathlib import Path
import os
os.environ['USE_TF'] = '0'
os.environ['USE_FLAX'] = '0'
import copy
import hashlib
import json
import random
import shutil
import sys
import time
import numpy as np
import torch
from transformers import BertConfig, BertForSequenceClassification, BertTokenizer

ROOT = Path(__file__).resolve().parents[2]
sys.path.append(str(ROOT / '.tools/ai-python'))
import onnxruntime as ort
from onnxruntime.quantization import quantize_dynamic, QuantType

base = ROOT / '.tools/ai/base'
work = ROOT / '.tools/ai/train'
output = work / 'candidate'
work.mkdir(parents=True, exist_ok=True)
output.mkdir(parents=True, exist_ok=True)
random.seed(408); np.random.seed(408); torch.manual_seed(408)
torch.set_num_threads(4)
device = torch.device('cuda' if torch.cuda.is_available() else 'cpu')
config = BertConfig.from_json_file(str(base/'config.json'))
config._attn_implementation = 'eager'
tokenizer = BertTokenizer(vocab_file=str(base/'vocab.txt'), do_lower_case=True)
model = BertForSequenceClassification(config)
state = torch.load(base/'pytorch_model.bin', map_location='cpu', weights_only=True)
state.pop('bert.embeddings.position_ids', None)  # Non-persistent buffer in current transformers.
model.load_state_dict(state)
del state
model.to(device)
training = json.loads((ROOT/'tools/ai/domain-train.json').read_text(encoding='utf-8'))
validation = json.loads((ROOT/'tools/ai/domain-eval.json').read_text(encoding='utf-8'))

def inputs(rows):
    return tokenizer([r['premise'] for r in rows], [r['hypothesis'] for r in rows], padding=True,
                     truncation=True, max_length=192, return_tensors='pt')

def evaluate():
    model.eval(); probabilities=[]
    with torch.inference_mode():
        for start in range(0, len(validation), 8):
            batch = {k:v.to(device) for k,v in inputs(validation[start:start+8]).items()}
            probabilities.extend(model(**batch).logits.softmax(-1).cpu().numpy().tolist())
    predicted=[int(np.argmax(p)) for p in probabilities]
    correct=sum(p==r['label'] for p,r in zip(predicted,validation))
    false_awards=sum(p[2]>=0.85 and p[0]<0.1 and r['label']!=2 for p,r in zip(probabilities,validation))
    accepted=sum(p[2]>=0.85 and p[0]<0.1 and r['label']==2 for p,r in zip(probabilities,validation))
    return dict(correct=correct,total=len(validation),false_awards=false_awards,accepted_correct=accepted,
                details=[dict(**r,probabilities=p,predicted=y) for r,p,y in zip(validation,probabilities,predicted)])

baseline=evaluate()
print('BASELINE', {k:v for k,v in baseline.items() if k!='details'}, flush=True)
# Keep most of the multilingual/general knowledge stable, adapting the final
# two transformer blocks, pooler and the existing three-class NLI head.
for name,parameter in model.named_parameters():
    parameter.requires_grad = any(s in name for s in ('encoder.layer.10.','encoder.layer.11.','pooler','classifier'))
optimizer=torch.optim.AdamW([p for p in model.parameters() if p.requires_grad],lr=2e-5,weight_decay=0.01)
best=None; best_score=(-999,-999,-999); best_weights=None
history=[]; started=time.perf_counter()
for epoch in range(5):
    model.train(); order=list(range(len(training))); random.shuffle(order); losses=[]
    for start in range(0,len(order),12):
        rows=[training[i] for i in order[start:start+12]]
        batch={k:v.to(device) for k,v in inputs(rows).items()}
        labels=torch.tensor([r['label'] for r in rows],device=device)
        optimizer.zero_grad(set_to_none=True)
        loss=model(**batch,labels=labels).loss
        loss.backward(); torch.nn.utils.clip_grad_norm_(model.parameters(),1.0); optimizer.step()
        losses.append(float(loss.detach().cpu()))
    result=evaluate()
    record=dict(epoch=epoch+1,loss=float(np.mean(losses)),**{k:v for k,v in result.items() if k!='details'})
    history.append(record); print('EPOCH',record,flush=True)
    score=(-result['false_awards'],result['correct'],result['accepted_correct'])
    if score>best_score:
        best=result; best_score=score
        best_weights={k:v.detach().cpu().clone() for k,v in model.state_dict().items()}
model.load_state_dict(best_weights)
model.cpu().eval()
model.save_pretrained(work/'checkpoint',safe_serialization=True)
tokenizer.save_pretrained(work/'checkpoint')
dummy=inputs([training[0],training[1]])
full=work/'wakuu.fp32.onnx'
torch.onnx.export(model, (dummy['input_ids'],dummy['attention_mask'],dummy['token_type_ids']), str(full),
 input_names=['input_ids','attention_mask','token_type_ids'],output_names=['logits'],opset_version=17,
 dynamic_axes={name:{0:'batch',1:'sequence'} for name in ('input_ids','attention_mask','token_type_ids')},
 do_constant_folding=True)
quant=output/'wakuu.int8.onnx'
quantize_dynamic(str(full),str(quant),weight_type=QuantType.QInt8,per_channel=True,
                 op_types_to_quantize=['MatMul','Gemm'])
options=ort.SessionOptions(); options.intra_op_num_threads=4; options.inter_op_num_threads=1
session=ort.InferenceSession(str(quant),sess_options=options,providers=['CPUExecutionProvider'])
rows=validation
encoded=inputs(rows)
tic=time.perf_counter()
logits=session.run(None,{k:v.numpy() for k,v in encoded.items()})[0]
latency=time.perf_counter()-tic
probs=np.exp(logits-logits.max(axis=1,keepdims=True)); probs/=probs.sum(axis=1,keepdims=True)
quant_correct=sum(int(np.argmax(p))==r['label'] for p,r in zip(probs,rows))
false_awards=sum(p[2]>=0.85 and p[0]<0.1 and r['label']!=2 for p,r in zip(probs,rows))
report=dict(model='IDEA-CCNL/Erlangshen-Roberta-110M-NLI',revision='864d25be3ce5e90d9193cb49d9fbd52722cdc6b0',
 seed=408,device=str(device),train_pairs=len(training),validation_pairs=len(validation),baseline=baseline,
 history=history,selected=best,quantized_correct=int(quant_correct),quantized_false_awards=int(false_awards),
 validation_cpu_batch_seconds=latency,elapsed_seconds=time.perf_counter()-started,
 bytes=quant.stat().st_size,sha256=hashlib.sha256(quant.read_bytes()).hexdigest(),
 limitation='Small authored development validation; not an independently human-labelled exam scoring benchmark.')
(ROOT/'reports/wakuu-training.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
shutil.copy2(base/'vocab.txt',output/'vocab.txt')
manifest={k:report[k] for k in ('model','revision','bytes','sha256','limitation')}
manifest.update(model_version='wakuu-nli-408-dev1',entailment_threshold=0.85,contradiction_ceiling=0.1,
                classes=['contradiction','neutral','entailment'],max_tokens=192,training_pairs=len(training))
(output/'model.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
print('EXPORTED',json.dumps({k:v for k,v in report.items() if k not in ('baseline','history','selected')},ensure_ascii=False),flush=True)
