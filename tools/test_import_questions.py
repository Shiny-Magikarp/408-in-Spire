import copy
import unittest
from import_questions import asset_path, classify, fingerprint, image_refs, normalize


class ImportTests(unittest.TestCase):
    def setUp(self):
        self.q = {"id": "test-01", "book": "数据结构", "type": "single_choice",
                  "question": "【2011 统考真题】复杂度 $O(n^2)$？\n```c\n    x *= 2;\n```",
                  "options": {"A": "$O(n)$", "B": "$O(1)$", "C": "$O(n^2)$", "D": "$O(2^n)$"},
                  "answer": ["C"], "explanation": "参考解析。"}

    def test_preserves_formulas_and_code(self):
        result = normalize(self.q)
        self.assertIn("$O(n^2)$", result["stem"])
        self.assertIn("    x *= 2;", result["stem"])
        self.assertEqual(result["exam"]["kind"], "past_exam")

    def test_hidden_and_multichoice_not_imported(self):
        for change in ({"hidden": True}, {"type": "multiple_choice"}, {"answer": ["A", "C"]}):
            with self.assertRaises(ValueError):
                normalize(self.q | change)

    def test_missing_option_and_bad_answer(self):
        for change in ({"options": {"A": "only"}}, {"answer": ["E"]}, {"answer": "C"}):
            with self.assertRaises(ValueError):
                normalize(self.q | change)

    def test_unknown_is_not_declared_non_exam(self):
        self.assertEqual(classify("下列哪项正确？")["kind"], "unknown")
        self.assertEqual(classify("【2018 某大学】下列哪项正确？")["kind"], "suspected_exam")
        self.assertEqual(classify("【原创题】下列哪项正确？")["kind"], "practice")

    def test_image_refs_and_path_containment(self):
        self.assertEqual(image_refs('![图](images/a.png) <img src="images/b.jpg">'), ["images/a.png", "images/b.jpg"])
        for path in ("../private.png", "%2e%2e/private.png", "https://example.com/a.png", "//server/a.png", "C:/a.png", "images/a.svg", "images/a.png?x=1"):
            with self.assertRaises(ValueError, msg=path):
                asset_path(path)

    def test_identity_stable_answer_revision_changes(self):
        a, b = normalize(self.q), normalize(self.q | {"answer": ["B"]})
        self.assertEqual(a["id"], b["id"])
        self.assertNotEqual(a["revision"], b["revision"])
        self.assertEqual(fingerprint(a), fingerprint(b))

    def test_exact_exam_duplicate_unified(self):
        other = copy.deepcopy(self.q)
        other["question"] = other["question"].replace("【2011 统考真题】", "")
        other["id"] = "test-02"
        self.assertEqual(fingerprint(normalize(self.q)), fingerprint(normalize(other)))


if __name__ == "__main__":
    unittest.main()
