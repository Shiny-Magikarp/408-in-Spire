"""Build local, experimental semantic checkpoints; never execute submitted code.

Question-derived text is generated locally and is excluded from the public repo.
These are reference-answer checkpoints, not human-certified exam marking schemes.
"""
import hashlib
import re

POLICY_VERSION = 'wakuu-rubrics-080-v1'

def clean(text):
    text = re.sub(r'```[^\n]*\n(.*?)```', r'\1', text, flags=re.S)
    text = re.sub(r'\*\*|`|^#{1,6}\s*', '', text, flags=re.M)
    return re.sub(r'[ \t]+', ' ', text).strip()

def checkpoints(text):
    text = clean(text)
    text = re.sub(r'^[（(]\d+[)）]\s*', '', text)
    statements = re.split(r'(?<=[。；！？\n])', text)
    result = []
    for statement in statements:
        statement = statement.strip()
        if len(statement) < 8 or re.fullmatch(r'.{0,20}(?:见代码|代码见下|见上|如下)[。：:]*', statement):
            continue
        # Keep all content. Split long clauses explicitly, rather than silently
        # truncating either side of a model input pair.
        while len(statement) > 80:
            cut = max(statement.rfind(mark, 30, 80) for mark in ('，', ',', '；', '：', ' '))
            cut = cut + 1 if cut >= 30 else 80
            result.append(statement[:cut].strip())
            statement = statement[cut:].strip()
        if statement:
            result.append(statement)
    return list(dict.fromkeys(result))

def parts(reference):
    sections = re.split(r'(?=\*{0,2}[（(]\d+[)）])', reference)
    sections = [clean(s) for s in sections if clean(s)]
    sections = [s for s in sections if checkpoints(s)]
    if not sections:
        raise ValueError('No usable reference checkpoints')
    # A few sources include many numbered substeps; retain every checkpoint in
    # at most eight editor-bound answer rows.
    while len(sections) > 8:
        sections[-2:] = [sections[-2] + '\n' + sections[-1]]
    result = []
    for i, section in enumerate(sections):
        checks = checkpoints(section)
        result.append(dict(id=f'p{i+1}', kind='rubric',
            prompt=f'第{i+1}组：写出对应小问的思路、过程、伪代码或结论（实验性语义核分）',
            reference=section, accepted=[], checks=checks,
            points=100//len(sections)+(i<100%len(sections))))
    return result

def revision(item):
    import json
    return hashlib.sha256(json.dumps(item, ensure_ascii=False, sort_keys=True).encode()).hexdigest()[:16]
