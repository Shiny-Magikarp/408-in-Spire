"""Pinned CS408 essay import. Parse TypeScript literals without executing code."""
from __future__ import annotations
import argparse
from collections import Counter
from concurrent.futures import ThreadPoolExecutor
import hashlib
import json
from pathlib import Path
import re
from urllib.request import Request, urlopen

ROOT = Path(__file__).resolve().parents[1]
REPOSITORY = '43aquarius/cs408'
COMMIT = '5a689c40f7ec7e9298c76966dfe300b8f3c3b682'
CACHE = ROOT / '.cache' / 'essay-audit' / '43aquarius__cs408'
FILES = [f'src/data/questions/y{year}-a.ts' for year in range(2009, 2027)] + [
    f'src/data/mocks/mock{n:02}-a.ts' for n in range(1, 11)]


class LiteralParser:
    """A deliberately small data grammar; calls/imports/expressions fail closed."""
    def __init__(self, text): self.text, self.i = text, 0
    def skip(self):
        while self.i < len(self.text):
            if self.text[self.i].isspace(): self.i += 1
            elif self.text.startswith('//', self.i):
                n = self.text.find('\n', self.i); self.i = len(self.text) if n < 0 else n + 1
            elif self.text.startswith('/*', self.i):
                n = self.text.find('*/', self.i + 2)
                if n < 0: raise ValueError('Unclosed comment')
                self.i = n + 2
            else: break
    def take(self, token):
        self.skip()
        if not self.text.startswith(token, self.i): raise ValueError(f'Expected {token} at {self.i}')
        self.i += len(token)
    def string(self):
        quote = self.text[self.i]; self.i += 1; out = []
        while self.i < len(self.text):
            ch = self.text[self.i]; self.i += 1
            if ch == quote: return ''.join(out)
            if quote == '`' and ch == '$' and self.text[self.i:self.i+1] == '{':
                raise ValueError('Template interpolation is not data')
            if ch != '\\': out.append(ch); continue
            ch = self.text[self.i]; self.i += 1
            if ch in ('u', 'x'):
                length = 4 if ch == 'u' else 2
                out.append(chr(int(self.text[self.i:self.i+length], 16))); self.i += length
            elif ch == '\n': pass
            else: out.append({'n':'\n','r':'\r','t':'\t','b':'\b','f':'\f','v':'\v','0':'\0'}.get(ch, ch))
        raise ValueError('Unclosed string')
    def value(self):
        self.skip(); ch = self.text[self.i]
        if ch in "'\"`":
            value = self.string(); self.skip()
            while self.text[self.i:self.i+1] == '+':
                self.i += 1; self.skip()
                if self.text[self.i] not in "'\"`": raise ValueError('Only literal string concatenation allowed')
                value += self.string(); self.skip()
            return value
        if ch == '[':
            self.i += 1; result = []; self.skip()
            while self.text[self.i] != ']':
                result.append(self.value()); self.skip()
                if self.text[self.i] == ']': break
                self.take(','); self.skip()
            self.take(']'); return result
        if ch == '{':
            self.i += 1; result = {}; self.skip()
            while self.text[self.i] != '}':
                if self.text[self.i] in "'\"": key = self.string()
                else:
                    m = re.match(r'[A-Za-z_$][\w$]*', self.text[self.i:])
                    if not m: raise ValueError('Invalid object key')
                    key = m[0]; self.i += len(key)
                if key in result: raise ValueError('Duplicate object key ' + key)
                self.take(':'); result[key] = self.value(); self.skip()
                if self.text[self.i] == '}': break
                self.take(','); self.skip()
            self.take('}'); return result
        m = re.match(r'-?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?', self.text[self.i:])
        if m:
            self.i += len(m[0]); return float(m[0]) if any(c in m[0] for c in '.eE') else int(m[0])
        for token, value in [('true', True), ('false', False), ('null', None)]:
            if self.text.startswith(token, self.i): self.i += len(token); return value
        raise ValueError('Executable or unsupported expression at ' + self.text[self.i:self.i+50])


def parse_questions(text):
    start = re.search(r'export\s+const\s+\w+\s*:\s*Question\s*\[\]\s*=\s*', text)
    if not start: raise ValueError('Question array declaration missing')
    parser = LiteralParser(text[start.end():]); result = parser.value(); parser.skip()
    if parser.text[parser.i:].strip() not in ('', ';'): raise ValueError('Unexpected code after question data')
    if not isinstance(result, list): raise ValueError('Question array expected')
    return result


def fetch(path, offline):
    destination = CACHE / path
    if not destination.exists():
        if offline: raise FileNotFoundError('Offline essay cache missing: ' + path)
        request = Request(f'https://raw.githubusercontent.com/{REPOSITORY}/{COMMIT}/{path}', headers={'User-Agent':'Spire408-import/0.4'})
        with urlopen(request, timeout=30) as response: data = response.read(4*1024*1024+1)
        if len(data) > 4*1024*1024: raise ValueError('Essay file too large')
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(data)
    data = destination.read_bytes()
    lock = json.loads((ROOT/'tools/essay-source-lock.json').read_text(encoding='utf-8'))
    if lock['commit'] != COMMIT or hashlib.sha256(data).hexdigest() != lock['files'][path]:
        raise ValueError('Pinned essay source checksum mismatch: ' + path)
    return data


def save(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + '.tmp')
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    temporary.replace(path)


def import_bank(offline=False):
    def one(path):
        data = fetch(path, offline); records = parse_questions(data.decode('utf-8'))
        if len(records) != 7: raise ValueError('Expected seven essays: ' + path)
        for q in records:
            if q.get('type') != 'application' or q.get('subject') not in ('ds','co','os','cn'):
                raise ValueError('Invalid essay identity: ' + path)
            if not all(isinstance(q.get(k), str) and q[k].strip() for k in ('id','question','answerText','explanation')):
                raise ValueError('Missing essay text: ' + path)
            q['_source'] = dict(repository=REPOSITORY, commit=COMMIT, file=path, sha256=hashlib.sha256(data).hexdigest())
        return records
    with ThreadPoolExecutor(max_workers=8) as pool: questions = [q for rows in pool.map(one, FILES) for q in rows]
    if len({q['id'] for q in questions}) != 196: raise ValueError('Essay IDs overlap')
    save(ROOT/'data/essay-upstream.json', dict(repository=REPOSITORY, commit=COMMIT, questions=questions))
    (ROOT/'data/CS408-ESSAYS-LICENSE.txt').write_bytes(fetch('LICENSE', offline))
    (ROOT/'data/CS408-ESSAYS-README.md').write_bytes(fetch('README.md', offline))
    report = dict(repository=REPOSITORY, commit=COMMIT, imported=len(questions),
                  subjects=dict(Counter(q['subject'] for q in questions)),
                  sources=dict(Counter(q['source'] for q in questions)),
                  topics=dict(Counter(q['topic'] for q in questions)))
    save(ROOT/'reports/essay-import-report.json', report)
    print(json.dumps({k:v for k,v in report.items() if k!='topics'}, ensure_ascii=True))
    return questions


if __name__ == '__main__':
    parser = argparse.ArgumentParser(); parser.add_argument('--offline', action='store_true')
    import_bank(parser.parse_args().offline)
