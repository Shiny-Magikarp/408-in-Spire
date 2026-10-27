from pathlib import Path
import os
os.environ['USE_TF']='0'
import sys,json,time,hashlib
import numpy as np
from transformers import BertTokenizer
ROOT=Path(__file__).resolve().parents[2]
sys.path.append(str(ROOT/'.tools/ai-python'))
import onnx
import onnxruntime as ort
from onnxruntime.transformers.float16 import convert_float_to_float16

fp32=ROOT/'.tools/ai/train/wakuu.fp32.onnx'
fp16=ROOT/'.tools/ai/train/wakuu.fp16.onnx'
candidate=ROOT/'.tools/ai/train/candidate'
model=onnx.load(str(fp32))
model=convert_float_to_float16(model,keep_io_types=True,disable_shape_infer=True)
onnx.save(model,str(fp16))
del model
rows=json.loads((ROOT/'tools/ai/domain-eval.json').read_text(encoding='utf-8'))
tokenizer=BertTokenizer(vocab_file=str(ROOT/'.tools/ai/base/vocab.txt'),do_lower_case=True)
reports=[]
for precision,path in [('fp16',fp16),('fp32',fp32),('int8',candidate/'wakuu.int8.onnx')]:
    options=ort.SessionOptions();options.intra_op_num_threads=4;options.inter_op_num_threads=1
    session=ort.InferenceSession(str(path),sess_options=options,providers=['CPUExecutionProvider'])
    details=[];tic=time.perf_counter()
    for row in rows:
        tokens=tokenizer(row['premise'],row['hypothesis'],return_tensors='np',truncation=True,max_length=192)
        scores=session.run(None,dict(tokens))[0][0];p=np.exp(scores-scores.max());p/=p.sum()
        award=bool(p[2]>=.85 and p[0]<.1)
        details.append(dict(**row,predicted=int(np.argmax(p)),probabilities=p.tolist(),award=award))
    elapsed=time.perf_counter()-tic
    report=dict(precision=precision,bytes=path.stat().st_size,correct=sum(x['predicted']==x['label'] for x in details),
     false_awards=sum(x['award'] and x['label']!=2 for x in details),accepted_correct=sum(x['award'] and x['label']==2 for x in details),
     cpu_seconds=elapsed,details=details)
    reports.append(report);print(json.dumps({k:v for k,v in report.items() if k!='details'}),flush=True)
    del session
baseline=next(r for r in reports if r['precision']=='fp32')
if baseline['false_awards']:
    raise RuntimeError('FP32 candidate awards an incorrect development answer; release preserved.')
selected=next((r for r in reports if r['false_awards']==0 and r['precision']=='fp16'
               and r['correct']>=baseline['correct'] and r['accepted_correct']>=baseline['accepted_correct']),baseline)
import shutil
chosen=fp16 if selected['precision']=='fp16' else fp32
output=ROOT/'models/wakuu';output.mkdir(parents=True,exist_ok=True)
destination=output/'wakuu.onnx'
staged=output/'wakuu.onnx.next';shutil.copy2(chosen,staged)
manifest=json.loads((candidate/'model.json').read_text(encoding='utf-8'))
manifest.update(precision=selected['precision'],bytes=staged.stat().st_size,sha256=hashlib.sha256(staged.read_bytes()).hexdigest(),file='wakuu.onnx')
staged_manifest=output/'model.json.next'
staged_manifest.write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
shutil.copy2(candidate/'vocab.txt',output/'vocab.txt')
os.replace(staged,destination)
os.replace(staged_manifest,output/'model.json')
(ROOT/'reports/wakuu-precision.json').write_text(json.dumps(dict(selected=selected['precision'],comparisons=reports),ensure_ascii=False,indent=2),encoding='utf-8')
print('SELECTED',selected['precision'],flush=True)
