import json
from pathlib import Path
import unittest
from import_essays import LiteralParser, parse_questions
from build_essays import rubrics
from essay_scope import POLICY as scope_policy, validate_exclusions

class EssayImportTests(unittest.TestCase):
    def test_literal_data_and_escapes(self):
        text="import type { Question } from '../types';\nexport const q: Question[] = [{id:'x',question:'line\\nnext',n:-2,ok:true,arr:[1,2,],},]"
        self.assertEqual(parse_questions(text),[dict(id='x',question='line\nnext',n=-2,ok=True,arr=[1,2])])
    def test_no_upstream_execution(self):
        for value in ['[process.exit()]','[...other]','[`${process.env.SECRET}`]','[{a: someCall()}]','[{a: 1,a: 2}]']:
            with self.subTest(value=value),self.assertRaises(ValueError): LiteralParser(value).value()
    def test_no_trailing_code(self):
        with self.assertRaises(ValueError): parse_questions('export const q: Question[] = []; run()')
    def test_preserved_batch(self):
        root=Path(__file__).resolve().parents[1]
        source=json.loads((root/'data/essay-upstream.json').read_text(encoding='utf-8'))['questions']
        bank=json.loads((root/'game/Spire408/Data/essays.json').read_text(encoding='utf-8'))['questions']
        imported={q['id']:q for q in bank if q['id'].startswith('cs408-')}
        errata=json.loads((root/'tools/essay-quality-policy.json').read_text(encoding='utf-8'))['corrections']
        self.assertEqual(len(imported),187)
        self.assertEqual(set(imported),{'cs408-'+q['id'] for q in source if q['id'] not in scope_policy['exclusions']})
        for q in source:
            if q['id'] in scope_policy['exclusions']:continue
            item=imported['cs408-'+q['id']]
            patches=errata.get(q['id'],{}).get('fields',{})
            self.assertEqual(item['reference_answer'],patches.get('answerText',{}).get('replacement',q['answerText']))
            self.assertEqual(item['explanation'],patches.get('explanation',{}).get('replacement',q['explanation']))
            self.assertIn(q['question'],item['stem'])
            self.assertEqual(item['original_points'],q['score'])
    def test_drawing_exclusions_reject_changed_source(self):
        root=Path(__file__).resolve().parents[1]
        source=json.loads((root/'data/essay-upstream.json').read_text(encoding='utf-8'))['questions']
        built=[dict(id='cs408-'+id,revision=rule['expected_revision']) for id,rule in scope_policy['exclusions'].items()]
        self.assertEqual(len(validate_exclusions(source,built)),9)
        next(q for q in source if q['id']=='mock05-41')['question']+='changed'
        with self.assertRaisesRegex(ValueError,'Changed excluded drawing source'):validate_exclusions(source,built)
    def test_graph_algorithms_and_merge_calculations_are_retained(self):
        root=Path(__file__).resolve().parents[1]
        bank={q['id'] for q in json.loads((root/'game/Spire408/Data/essays.json').read_text(encoding='utf-8'))['questions']}
        self.assertTrue({'cs408-q-2025-41','cs408-q-2020-41','cs408-q-2016-41','cs408-mock06-42','cs408-mock10-41'}<=bank)
    def test_independent_calculation_checks(self):
        # Independent small solvers check the hand-authored final-result keys.
        import heapq
        pool=[7,9,12,22,30,46];heapq.heapify(pool);wpl=0
        while len(pool)>1:
            merged=heapq.heappop(pool)+heapq.heappop(pool);wpl+=merged;heapq.heappush(pool,merged)
        self.assertEqual(rubrics()['mock06-42'][0]['expected'],wpl)
        sequence=[1,2,3,1,4,2,5,1,2,3,4,5,1];frames=[];misses=0
        for i,page in enumerate(sequence):
            if page in frames: continue
            misses+=1
            if len(frames)==3:
                def distance(p):
                    try:return sequence.index(p,i+1)
                    except ValueError:return float('inf')
                frames.remove(max(frames,key=distance))
            frames.append(page)
        self.assertEqual(rubrics()['mock04-46'][1]['expected'],misses)

if __name__=='__main__': unittest.main()
