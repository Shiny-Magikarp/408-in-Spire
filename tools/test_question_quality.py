import copy
import json
from pathlib import Path
import unittest
from import_questions import normalize
from question_quality import apply_quality, load_policy, update_manifest


class QualityTests(unittest.TestCase):
    def setUp(self):
        self.policy = load_policy()
        self.id, self.fix = next(iter(self.policy["corrections"].items()))
        source = Path(__file__).resolve().parents[1] / ".cache" / self.policy["source_commit"] / "data.json"
        raw = json.loads(source.read_text(encoding="utf-8"))["questions"]
        self.q = normalize(next(q for q in raw if q["id"] == self.fix["upstream_id"]))

    def test_reported_instruction_encoding_question_is_d(self):
        self.assertTrue(apply_quality(self.q, self.policy))
        self.assertEqual((2 ** (32 - 2 * 12) - 250) * 2 ** 12, 3 * 2 ** 13)
        self.assertEqual(self.q["answer"], "D")
        self.assertNotIn("缺页", self.q["explanation"])
        self.assertIn("24576", self.q["explanation"])
        self.assertNotEqual(self.q["revision"], self.fix["expected"]["revision"])
        self.assertEqual(self.q["quality"]["academic_review"], "locally_corrected")

    def test_changed_source_cannot_silently_reapply_erratum(self):
        for field in ("answer", "revision", "explanation"):
            q = copy.deepcopy(self.q)
            q[field] = "changed"
            with self.assertRaisesRegex(RuntimeError, "Stale"):
                apply_quality(q, self.policy)

    def test_unreviewed_ocr_batch_is_quarantined(self):
        q = copy.deepcopy(self.q)
        q["id"] = "not-reviewed"
        self.assertFalse(apply_quality(q, self.policy))
        q["source"]["upstream_id"] = "wiki-reviewed-separately"
        self.assertTrue(apply_quality(q, self.policy))

    def test_only_explicit_legacy_bank_is_compatible(self):
        apply_quality(self.q, self.policy)
        manifest = update_manifest(self.policy, [self.q])
        self.assertEqual(len(manifest["compatible_from"]), 2)
        self.assertNotIn(self.id, manifest["withdrawn_ids"])
        self.assertEqual(manifest["revisions"][0]["revision"], self.q["revision"])


if __name__ == "__main__":
    unittest.main()
