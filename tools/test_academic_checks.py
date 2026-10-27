import copy
import json
from pathlib import Path
import struct
import unittest
from choice_calculations import verify, matches
from essay_calculations import faults, hash_table, spanning_weights, postfix, rubrics

ROOT=Path(__file__).resolve().parents[1]


class AcademicChecks(unittest.TestCase):
    def test_independent_choice_calculations(self):
        bank=json.loads((ROOT/'data/questions.json').read_text(encoding='utf-8'))['questions']
        self.assertEqual(len(verify(bank)),28)

    def test_units_are_not_ignored_in_choice_validation(self):
        self.assertTrue(matches('time_us',2,r'$2\\mu s$'))
        self.assertFalse(matches('time_us',2,'2ms'))

    def test_wrong_choice_key_is_detected(self):
        bank=json.loads((ROOT/'data/questions.json').read_text(encoding='utf-8'))['questions']
        target=next(q for q in bank if q['id']=='408-0757d429c8f8d9dd1c68c3e8')
        target['answer']='B'
        with self.assertRaisesRegex(ValueError,'mismatch'):verify(bank)

    def test_fifo_belady_and_lru_counterexample(self):
        seq=[1,2,3,4,1,2,5,1,2,3,4,5]
        self.assertEqual(faults(seq,3,'FIFO')[0],9)
        self.assertEqual(faults(seq,4,'FIFO')[0],10)
        self.assertEqual(faults(seq,3,'LRU')[0],10)
        self.assertEqual(faults(seq,3,'OPT')[0],7)
        self.assertEqual(faults(seq,3,'CLOCK')[0],9)
        self.assertTrue(all(len(row['resident'])<=3 for row in faults(seq,3,'LRU')[1]))

    def test_failed_hash_search_uses_hash_range(self):
        table,success,failure=hash_table([87,40,30,6,11,22,98,20],11,7)
        self.assertEqual(table[:8],[98,22,30,87,11,40,6,20])
        self.assertEqual(failure,6)

    def test_spanning_trees_include_equal_weight_alternatives(self):
        edges=[('a','b',1),('b','c',1),('a','c',1)]
        self.assertEqual(spanning_weights(list('abc'),edges),[2,2,2])

    def test_postfix_keeps_operators_and_precedence(self):
        expression='(A+B)/(C-D)*E-F'
        self.assertEqual(postfix(expression,{'+':1,'-':1,'*':2,'/':2}),'AB+CD-/E*F-')
        self.assertEqual(postfix(expression,{'+':2,'-':2,'*':1,'/':1}),'AB+CD-/EF-*')

    def test_new_rubrics_lock_their_question_parameters(self):
        source=json.loads((ROOT/'data/essay-upstream.json').read_text(encoding='utf-8'))['questions']
        self.assertEqual(len(rubrics(source)),45)
        next(q for q in source if q['id']=='q-2010-44')['question']+='modified'
        with self.assertRaisesRegex(ValueError,'Changed calculation source'):rubrics(source)

    def test_binary32_counterexample_to_old_reference(self):
        round32=lambda x:struct.unpack('f',struct.pack('f',x))[0]
        self.assertEqual(round32(round32(.1)+round32(.2)),round32(.3))
        q=next(q for q in json.loads((ROOT/'game/Spire408/Data/essays.json').read_text(encoding='utf-8'))['questions'] if q['id']=='cs408-q-2024-43')
        self.assertEqual(q['parts'][0]['kind'],'rubric')
        self.assertTrue(q['experimental'] and q['requires_process_review'])
        self.assertIn('未定义',q['reference_answer'])
        self.assertIn('YES',q['reference_answer'])

    def test_explanations_follow_explicit_errata(self):
        bank={q['id']:q for q in json.loads((ROOT/'game/Spire408/Data/essays.json').read_text(encoding='utf-8'))['questions']}
        self.assertIn('200MB/s',bank['cs408-mock08-43']['reference_answer'])
        self.assertIn('O(m+n)',bank['cs408-q-2015-42']['reference_answer'])
        self.assertIn('驻留集',bank['cs408-q-2018-45']['reference_answer'])

    def test_all_169_old_self_check_items_are_accounted_for(self):
        from audit_essay_grading import main
        main()
        report=json.loads((ROOT/'reports/essay-grading-audit.json').read_text(encoding='utf-8'))
        self.assertEqual(len(report['previous_review_only_items']),169)
        self.assertEqual(report['new_result_scopes']+report['new_semantic_scopes']+report['remaining_review_only'],169)


if __name__=='__main__':unittest.main()
