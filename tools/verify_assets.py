"""Validate the exact question and presentation resources shipped in the PCK."""
import json
import hashlib
from pathlib import Path
import re
from question_quality import load_policy

root = Path(__file__).resolve().parents[1]
resources = root / "game"
bank = json.loads((resources / "Spire408/Data/questions.json").read_text(encoding="utf-8"))
presentation = json.loads((resources / "Spire408/Rendered/presentation.json").read_text(encoding="utf-8"))
report = json.loads((root / "reports/render-report.json").read_text(encoding="utf-8"))
assert not report["errors"], report["errors"]
assert report["bank_sha256"] == hashlib.sha256((resources / "Spire408/Data/questions.json").read_bytes()).hexdigest()
assert report["presentation_sha256"] == hashlib.sha256((resources / "Spire408/Rendered/presentation.json").read_bytes()).hexdigest()
assert set(presentation) == {q["id"] for q in bank["questions"]}
policy = load_policy()
assert bank["source_sha256"] == policy["source_sha256"]
assert not (set(policy["withdrawn_ids"]) & {q["id"] for q in bank["questions"]})
images = set()
for q in bank["questions"]:
    if any(q["source"]["upstream_id"].startswith(p) for p in policy["quarantine_upstream_prefixes"]):
        assert q["id"] in policy["corrections"], f"Quarantined question leaked: {q['id']}"
    if q["id"] in policy["corrections"]:
        correction = policy["corrections"][q["id"]]
        assert q["answer"] == correction["answer"] and q["explanation"] == correction["explanation"]
    entry = presentation[q["id"]]
    assert set(entry) == {"stem", "A", "B", "C", "D", "explanation"}, q["id"]
    for text in entry.values():
        assert "```mermaid" not in text, q["id"]
        for ref, width, height in re.findall(r"@@img\|([^|]+)\|(\d+)\|(\d+)@@", text):
            path = (resources / ref).resolve()
            assert path.is_relative_to(resources.resolve())
            assert int(width) > 0 and int(height) > 0
            assert path.read_bytes().startswith(b"\x89PNG\r\n\x1a\n"), ref
            images.add(ref)
print(f"Asset check passed: {len(bank['questions'])} questions, {len(images)} referenced images, no missing resources.")
essays = json.loads((resources / 'Spire408/Data/essays.json').read_text(encoding='utf-8'))['questions']
essay_view = json.loads((resources / 'Spire408/Rendered/essay-presentation.json').read_text(encoding='utf-8'))
assert {q['id'] for q in essays} == set(essay_view)
for q in essays:
    assert essay_view[q['id']]['stem'] and sum(p['points'] for p in q['parts']) == 100
    if q['id'].startswith('cs408-'):
        assert essay_view[q['id']]['reference_answer'] and essay_view[q['id']]['explanation']
        assert q['source']['commit'] == '5a689c40f7ec7e9298c76966dfe300b8f3c3b682'
print(f"Essay assets passed: {len(essays)} complete questions and references.")
