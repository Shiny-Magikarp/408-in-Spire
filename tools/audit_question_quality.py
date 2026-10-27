"""Check all shipped records against the pinned source and explicit local corrections.

This verifies import correspondence, not the academic correctness of every retained question.
"""
import hashlib
import json
from pathlib import Path
from import_questions import normalize
from question_quality import apply_quality, load_policy
from choice_calculations import verify as verify_calculations

ROOT = Path(__file__).resolve().parents[1]


def main():
    policy = load_policy()
    raw = (ROOT / ".cache" / policy["source_commit"] / "data.json").read_bytes()
    assert hashlib.sha256(raw).hexdigest() == policy["source_sha256"]
    questions = json.loads(raw)["questions"]
    normalized = {}
    quarantined = []
    for item in questions:
        try:
            q = normalize(item)
        except ValueError:
            continue
        if apply_quality(q, policy):
            normalized[q["id"]] = q
        else:
            quarantined.append(q["id"])
    bank = json.loads((ROOT / "game/Spire408/Data/questions.json").read_text(encoding="utf-8"))
    fields = ["stem", "options", "answer", "explanation", "revision"]
    assert {q["id"] for q in bank["questions"]} == set(normalized)
    for q in bank["questions"]:
        assert all(q[k] == normalized[q["id"]][k] for k in fields), q["id"]
    independent=verify_calculations(bank['questions'])
    source_matches=policy.get('cross_checks',{})
    cross_checked=[q['id'] for q in bank['questions'] if q['id'] in source_matches]
    examples = []
    for item in questions:
        upstream = item["id"]
        if upstream == next(iter(policy["corrections"].values()))["upstream_id"] or (
                upstream.startswith("2027_") and ("_7.2.4_01_" in upstream or "_4.2.3_01_" in upstream)):
            examples.append({"upstream_id": upstream, "question": item["question"],
                             "upstream_answer": item["answer"], "upstream_explanation": item["explanation"],
                             "source_file": item.get("source_file")})
    report = {"source_commit": policy["source_commit"], "source_sha256": policy["source_sha256"],
              "shipped_count": len(bank["questions"]), "fields_compared_for_every_record": fields,
              "source_correspondence": "passed", "quarantined_count": len(quarantined),
              "quarantined_ids": sorted(quarantined), "corrected_ids": list(policy["corrections"]),
              "confirmed_mismatch_examples": examples, "academic_accuracy_of_remaining_bank": "not_fully_reviewed",
              "independently_calculated":independent,"independently_calculated_count":len(independent),
              "cross_checked_in_shipped_bank":cross_checked,"cross_checked_in_shipped_bank_count":len(cross_checked),
              "cross_source_reference":policy.get('cross_check_source'),
              "cross_source_agreement_is_not_academic_approval":True,
              "explicit_content_exclusions":policy.get('exclusions',{}),
              "quarantine_scope": "Unreviewed OCR supplement plus confirmed incomplete or ambiguous inputs; reviewed exceptions are explicit."}
    (ROOT / "reports/question-quality-audit.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Question correspondence passed: {len(bank['questions'])}; explicit corrections: {len(policy['corrections'])}; quarantined: {len(quarantined)}. Academic review remains incomplete.")


if __name__ == "__main__":
    main()
