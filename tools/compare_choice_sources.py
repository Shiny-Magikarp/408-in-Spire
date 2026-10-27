"""Cross-source candidates are evidence, not an automatic academic approval."""
import collections
import difflib
import html
import json
from pathlib import Path
import re
import unicodedata
from import_questions import normalize, COMMIT

ROOT = Path(__file__).resolve().parents[1]


def norm(s):
    s = html.unescape(unicodedata.normalize('NFKC', s))
    s = re.sub(r'【[^】]*】', '', s)
    s = re.sub(r'\\(?:mathrm|mathbf|mathit|operatorname|text|frac|left|right|big|qquad|quad|times|cdot|ldots|leq|geq|le|ge|lceil|rceil|lfloor|rfloor|log|mathcal|sqrt|overline)', '', s)
    return re.sub(r'[^\u4e00-\u9fffA-Za-z0-9]', '', s).lower()


def main():
    raw = json.loads((ROOT / '.cache' / COMMIT / 'data.json').read_text(encoding='utf-8'))['questions']
    qs = []
    for q in raw:
        try:
            qs.append(normalize(q))
        except ValueError:
            pass
    ref = json.loads((ROOT / '.cache/academic-audit/cross-source-questions.json').read_text(encoding='utf-8'))
    for r in ref:
        r['norm'] = norm(r['stem_text'])
    index = collections.defaultdict(set)
    for i, r in enumerate(ref):
        for j in range(len(r['norm']) - 2):
            index[(r['subject'], r['norm'][j:j+3])].add(i)
    matched, conflicts, suspects = [], [], []
    for q in qs:
        ns = norm(q['stem'])
        votes = collections.Counter()
        for gram in set(ns[j:j+3] for j in range(len(ns) - 2)):
            votes.update(index[(q['subject_name'], gram)])
        best = []
        for i, _ in votes.most_common(5):
            r = ref[i]
            if len(r['options']) != 4:
                continue
            if q['exam']['years'] and r['year'] not in q['exam']['years']:
                continue
            ratio = difflib.SequenceMatcher(None, ns, r['norm'], autojunk=False).ratio()
            scores = {k: max(difflib.SequenceMatcher(None, norm(v), norm(x['text']), autojunk=False).ratio()
                             for x in r['options']) for k, v in q['options'].items()}
            best.append((ratio, r, scores))
        if not best:
            continue
        ratio, r, scores = max(best, key=lambda x: x[0])
        if ratio < .86:
            continue
        mapping = {k: max(r['options'], key=lambda x: difflib.SequenceMatcher(None, norm(v), norm(x['text']), autojunk=False).ratio())['label']
                   for k, v in q['options'].items()}
        mapped = [k for k, label in mapping.items() if label == r['answer']]
        entry = {'id': q['id'], 'upstream_id': q['source']['upstream_id'], 'year': r['year'], 'number': r['num'],
                 'stem_similarity': ratio, 'minimum_option_similarity': min(scores.values()), 'mapping': mapping,
                 'answer': q['answer'], 'reference_answer': mapped[0] if len(mapped) == 1 else None, 'reference_url': r['url']}
        critical = r'最多|最少|最大|最小|不正确|不可能|错误|有符号|无符号|先序|前序|中序|后序|升序|降序'
        same_qualifiers = collections.Counter(re.findall(critical, q['stem'])) == collections.Counter(re.findall(critical, r['stem_text']))
        if ratio >= .94 and min(scores.values()) >= .85 and len(set(mapping.values())) == 4 and same_qualifiers:
            matched.append(entry)
            if q['answer'] != entry['reference_answer']:
                conflicts.append(entry | {'stem': q['stem'], 'options': q['options'], 'explanation': q['explanation'],
                                          'reference_stem': r['stem_text'], 'reference_options': r['options'],
                                          'reference_explanation': r['explanation_text']})
        else:
            suspects.append(entry)
    result = {'matched': matched, 'conflicts': conflicts, 'candidates': suspects}
    (ROOT / 'reports/cross-source-comparison.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    print('STRONG MATCHES', len(matched), 'CONFLICTS', len(conflicts), 'CANDIDATES', len(suspects))
    for q in conflicts:
        print(json.dumps(q, ensure_ascii=False))


if __name__ == '__main__':
    main()
