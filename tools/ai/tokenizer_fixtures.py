from pathlib import Path
import os,json
os.environ['USE_TF']='0';os.environ['USE_FLAX']='0'
from transformers import BertTokenizer
root=Path(__file__).resolve().parents[2]
tokenizer=BertTokenizer(vocab_file=str(root/'.tools/ai/base/vocab.txt'),do_lower_case=True)
rows=json.loads((root/'tools/ai/domain-eval.json').read_text(encoding='utf-8'))
rows += [dict(premise='Cache 16 KiB，块长 64 B。',hypothesis='标记字段占18位。'),
         dict(premise='P(empty) → P(mutex)；UTF-8。',hypothesis='先申请空槽再申请锁。'),
         dict(premise='  中文\n\tＡＢＣ１２３ café naïve. ',hypothesis='这是测试。')]
fixtures=[]
for row in rows:
    encoded=tokenizer(row['premise'],row['hypothesis'],truncation=True,max_length=192)
    fixtures.append(dict(premise=row['premise'],hypothesis=row['hypothesis'],**encoded))
(root/'tools/ai/tokenizer-fixtures.json').write_text(json.dumps(fixtures,ensure_ascii=False,indent=2),encoding='utf-8')
