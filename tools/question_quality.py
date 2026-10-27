"""Explicit, pinned errata and quarantine. Never infer replacement answers from text similarity."""
import hashlib
import json
from pathlib import Path

POLICY_PATH = Path(__file__).with_name("question-quality-policy.json")


def load_policy():
    return json.loads(POLICY_PATH.read_text(encoding="utf-8"))


def apply_quality(q, policy):
    exclusion=policy.get('exclusions',{}).get(q['id'])
    if exclusion:
        if q['revision']!=exclusion['expected_revision']: raise RuntimeError('Stale question exclusion: '+q['id'])
        return False
    correction = policy["corrections"].get(q["id"])
    if correction:
        if q["source"]["upstream_id"] != correction["upstream_id"] or any(
                q.get(key) != value for key, value in correction["expected"].items()):
            raise RuntimeError(f"Stale question erratum: {q['id']}")
        previous = q["revision"]
        q["answer"] = correction["answer"]
        q["explanation"] = correction["explanation"]
        if 'options' in correction: q['options']=correction['options'].copy()
        # An explanation correction must also invalidate the reviewed revision.
        content = [q["subject"], q["stem"], q["options"], q["answer"], q["explanation"]]
        q["revision"] = hashlib.sha256(json.dumps(content, ensure_ascii=False, sort_keys=True).encode()).hexdigest()[:16]
        q["quality"]["academic_review"] = "locally_corrected"
        q["quality"]["erratum"] = {"previous_revision": previous, "verification": correction["verification"],
                                   "reviewed_on": correction["reviewed_on"]}
        return True
    allowed=not any(q["source"]["upstream_id"].startswith(prefix) for prefix in policy["quarantine_upstream_prefixes"])
    cross=policy.get('cross_checks',{}).get(q['id'])
    if allowed and cross:
        if q['revision']!=cross['expected_revision'] or q['answer']!=cross['answer']: raise RuntimeError('Stale cross-source check: '+q['id'])
        q['quality']['answer_cross_check']=cross
    review=policy.get('independent_reviews',{}).get(q['id'])
    if allowed and review:
        if q['revision']!=review['expected_revision'] or q['answer']!=review['answer']: raise RuntimeError('Stale independent review: '+q['id'])
        q['quality']['academic_review']='independently_calculated'
        q['quality']['academic_evidence']=review
    return allowed


def update_manifest(policy, questions):
    return {"compatible_from": policy["compatible_bank_hashes"], "withdrawn_ids": policy["withdrawn_ids"],
            "revisions": [{"id": q["id"], "previous_revision": q["quality"]["erratum"]["previous_revision"],
                           "revision": q["revision"]} for q in questions if "erratum" in q["quality"]]}
