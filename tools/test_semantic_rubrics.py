import sys
from pathlib import Path
import unittest
sys.path.insert(0,str(Path(__file__).resolve().parent/'ai'))
from full_essay_rubrics import parts,checkpoints
from marking_families_081 import rows as marking_rows

class SemanticRubricTests(unittest.TestCase):
    def test_all_sections_keep_weights_and_text(self):
        source='**(1)** 用两个指针保持固定间隔，扫描到尾部。\n**(2)** 时间复杂度为O(n)，额外空间为O(1)。'
        result=parts(source)
        self.assertEqual(sum(p['points'] for p in result),100)
        self.assertEqual(len(result),2)
        self.assertTrue(all(p['kind']=='rubric' and p['checks'] for p in result))

    def test_long_reference_is_not_silently_truncated(self):
        source='这是第一项需要核对的具体结论。'*50+'这是最后一项需要核对的独特结论。'
        result=checkpoints(source)
        self.assertTrue(any('最后一项' in c for c in result))
        self.assertTrue(all(0<len(c)<=80 for c in result))

    def test_many_steps_fit_editor_bound_rows(self):
        source='\n'.join(f'**({i})** 第{i}项要求保留具体的判断条件与结论。' for i in range(1,21))
        result=parts(source)
        self.assertLessEqual(len(result),8)
        self.assertEqual(sum(p['points'] for p in result),100)
        self.assertTrue(any('第20项' in c for p in result for c in p['checks']))

    def test_no_reference_does_not_become_an_automatic_full_score(self):
        with self.assertRaises(ValueError):parts('见代码。')

    def test_marking_family_splits_do_not_overlap(self):
        splits={name:marking_rows(name) for name in ('train','dev','test')}
        family_sets={name:{r['family_id'] for r in data} for name,data in splits.items()}
        self.assertTrue(family_sets['train'].isdisjoint(family_sets['dev']|family_sets['test']))
        self.assertTrue(family_sets['dev'].isdisjoint(family_sets['test']))
        text_sets={name:{(r['premise'],r['hypothesis']) for r in data} for name,data in splits.items()}
        self.assertTrue(text_sets['train'].isdisjoint(text_sets['dev']|text_sets['test']))

    def test_marking_holdout_is_balanced_across_books_and_award_labels(self):
        data=marking_rows('test')
        self.assertEqual(len(data),96)
        for subject in ('ds','co','os','cn'):
            book=[r for r in data if r['subject']==subject]
            self.assertEqual(len(book),24)
            self.assertEqual(sum(r['label']==2 for r in book),12)

if __name__=='__main__':unittest.main()
