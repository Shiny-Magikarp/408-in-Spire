"""Explicit, source-locked exclusions; graph terminology alone is not drawing."""
import hashlib
import json
from pathlib import Path

POLICY = json.loads((Path(__file__).resolve().parent / 'essay-scope-policy.json').read_text(encoding='utf-8'))


def validate_exclusions(source, built):
    source_by_id = {q['id']: q for q in source}
    built_by_id = {q['id']: q for q in built}
    for upstream_id, rule in POLICY['exclusions'].items():
        if hashlib.sha256(source_by_id[upstream_id]['question'].encode()).hexdigest() != rule['expected_question_sha256']:
            raise ValueError('Changed excluded drawing source: ' + upstream_id)
        if built_by_id['cs408-' + upstream_id]['revision'] != rule['expected_revision']:
            raise ValueError('Changed excluded drawing rubric: ' + upstream_id)
    return {'cs408-' + id for id in POLICY['exclusions']}
