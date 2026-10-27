"""One post-selection comparison of old and candidate FP32/FP16 models."""
import os
os.environ['USE_TF']='0'
from pathlib import Path
import sys,json,time,hashlib
import argparse
import numpy as np
from transformers import BertTokenizer
from marking_challenge import rows as challenge_rows

ROOT=Path(__file__).resolve().parents[2]
sys.path.append(str(ROOT/'.tools/ai-python'))
import onnxruntime as ort
tokenizer=BertTokenizer(vocab_file=str(ROOT/'.tools/ai/base/vocab.txt'),do_lower_case=True)

def evaluate(path,rows):
    options=ort.SessionOptions();options.intra_op_num_threads=4;options.inter_op_num_threads=1
    session=ort.InferenceSession(str(path),sess_options=options,providers=['CPUExecutionProvider'])
    details=[];start=time.perf_counter()
    for row in rows:
        inputs=tokenizer(row['premise'],row['hypothesis'],return_tensors='np',truncation=True,max_length=192)
        logits=session.run(None,dict(inputs))[0][0];p=np.exp(logits-logits.max());p/=p.sum()
        details.append(dict(id=row.get('id',''),label=row['label'],probabilities=p.tolist(),predicted=int(p.argmax()),
            award=bool(p[2]>=.85 and p[0]<.1)))
    return dict(total=len(rows),correct=sum(x['label']==x['predicted'] for x in details),
        positive_count=sum(x['label']==2 for x in details),negative_count=sum(x['label']!=2 for x in details),
        accepted_correct=sum(x['label']==2 and x['award'] for x in details),
        false_awards=sum(x['label']!=2 and x['award'] for x in details),seconds=time.perf_counter()-start,details=details)

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--challenge-only',action='store_true');args=parser.parse_args()
    work=ROOT/'.tools/ai/full-train'
    challenge=challenge_rows()
    report={'challenge_scope':'68 authored short assertions, including 4 prompt injections; not student essays or executable code validation.', 'comparisons':{}}
    old=ROOT/'.tools/ai/model-backups/dev1-before-080/wakuu.onnx'
    if not old.exists():old=ROOT/'models/wakuu/wakuu.onnx'
    for name,path in [('dev1',old),('candidate_fp32',work/'wakuu.fp32.onnx'),('candidate_fp16',work/'wakuu.fp16.onnx')]:
        result=evaluate(path,challenge);report['comparisons'][name]=result
        print(name,json.dumps({k:v for k,v in result.items() if k!='details'}),flush=True)
    if args.challenge_only:
        report['synthetic_test']=json.loads((ROOT/'reports/wakuu-full-evaluation.json').read_text(encoding='utf-8'))['synthetic_test']
    else:
        test=json.loads((ROOT/'.tools/ai/full-corpus/test.json').read_text(encoding='utf-8'))
        report['synthetic_test']=evaluate(work/'wakuu.fp16.onnx',test)
    print('SYNTHETIC_TEST',json.dumps({k:v for k,v in report['synthetic_test'].items() if k!='details'}),flush=True)
    report['candidate_sha256']=hashlib.sha256((work/'wakuu.fp16.onnx').read_bytes()).hexdigest()
    report['candidate_bytes']=(work/'wakuu.fp16.onnx').stat().st_size
    report['limitations']=['Synthetic test uses the same generation templates as training but disjoint question IDs.',
        'Authored challenge is separate from gradient data and checkpoint selection; not independently human-marked exam answers.',
        'No guarantee of arbitrary code, proofs, diagrams or all correct alternative solutions.']
    (ROOT/'reports/wakuu-full-evaluation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')

if __name__=='__main__':main()
