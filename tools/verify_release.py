"""Compare the production package with the installed mod and record release evidence."""
import hashlib
import json
import re
import zipfile
from datetime import date
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "dist/Spire408"
INSTALLED = ROOT.parent / "mods/Spire408"


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def main():
    manifest = json.loads((SOURCE / "mod_manifest.json").read_text(encoding="utf-8-sig"))
    version = manifest["version"]
    archive = ROOT / f"dist/Spire408-{version}.zip"
    files = sorted(p for p in SOURCE.rglob("*") if p.is_file())
    matched = {p.relative_to(SOURCE).as_posix():
               (INSTALLED / p.relative_to(SOURCE)).is_file()
               and digest(p) == digest(INSTALLED / p.relative_to(SOURCE)) for p in files}
    types = (ROOT / "reports/production-types.txt").read_text(encoding="utf-8-sig")
    has_smoke = "Class Spire408.Tests.GameSmoke" in types
    core = (ROOT / "reports/core-tests.log").read_text(encoding="utf-8-sig")
    game = (ROOT / "reports/integration-summary.txt").read_text(encoding="utf-8-sig")
    imports = (ROOT / "reports/import-tests.log").read_text(encoding="utf-8-sig")
    assert re.search(r"^OK$", imports, re.MULTILINE), "Import/academic checks did not pass"
    bank = json.loads((ROOT / "game/Spire408/Data/questions.json").read_text(encoding="utf-8"))
    essays = json.loads((ROOT / "reports/essay-bank-report.json").read_text(encoding="utf-8"))
    quality = json.loads((ROOT / "reports/question-quality-audit.json").read_text(encoding="utf-8"))
    comparison = json.loads((ROOT / "reports/cross-source-comparison.json").read_text(encoding="utf-8"))
    model = json.loads((SOURCE / "Models/wakuu/model.json").read_text(encoding="utf-8"))
    assert quality["shipped_count"] == len(bank["questions"]), "Question audit is stale"
    assert essays["automatic"] + essays["review_only"] == essays["total"], "Essay coverage is inconsistent"
    assert digest(SOURCE / "Models/wakuu/wakuu.onnx") == model["sha256"], "Model checksum differs"
    with zipfile.ZipFile(archive) as packed:
        crc_valid = packed.testzip() is None
        zip_matches = all(hashlib.sha256(packed.read("Spire408/" + p.relative_to(SOURCE).as_posix())).hexdigest()
                          == digest(p) for p in files)
        assert {n for n in packed.namelist() if not n.endswith("/")} == {
            "Spire408/" + p.relative_to(SOURCE).as_posix() for p in files}, "Archive has stale or missing files"
    size = sum(p.stat().st_size for p in INSTALLED.rglob("*") if p.is_file())
    assert all(matched.values()), "Installed files differ from the release package"
    assert size < 500_000_000, "Installed mod exceeds 500 MB"
    assert crc_valid and zip_matches, "Archive integrity or content mismatch"
    assert not has_smoke, "Production assembly contains the smoke-test entry point"
    report_path = ROOT / "reports/release-report.json"
    report = {
        "version": version, "date": date.today().isoformat(),
        "questions": len(bank["questions"]), "essays": essays,
        "core_checks": int(re.search(r"(\d+) checks passed", core)[1]),
        "import_tests": int(re.search(r"Ran (\d+) tests", imports)[1]),
        "game_checks": int(re.search(r"COMPLETE (\d+) checks", game)[1]),
        "production_has_smoke": has_smoke, "installed_hash_matches": all(matched.values()),
        "installed_bytes": size, "package_files": len(files), "zip_bytes": archive.stat().st_size,
        "zip_sha256": digest(archive), "zip_crc_valid": crc_valid, "zip_contents_match": zip_matches,
        "installed_files_match": matched,
        "model": {"model_version": model["model_version"], "precision": model["precision"],
                  "bytes": model["bytes"], "sha256": model["sha256"],
                  "parent_model_version": model.get("parent_model_version"),
                  "evaluation": "docs/WAKUU-082.md", "scoring_policy": "passages-081",
                  "weights_changed_since_081": model["sha256"] != "cd1a655635fec6f9bb33e5b95271212fc550c816489271939ea422bdc73356c3",
                  "not_independently_human_validated": True},
        "battle_rules": {"gold_per_correct": 10, "caps": [30, 60, 90], "successful_gains": [3, 6, 9],
                         "caps_apply_to_study_gold_only": True, "intrinsic_card_relic_gold": True,
                         "normal_combat_gold": False, "initial_gold": 0,
                         "damage_multiplier": 2, "block_multiplier": 2, "wrong_consumes_gain": False,
                         "activation": "manual_next_selected_card", "queued_older_cards_consume_activation": False,
                         "automatic_plays_consume_activation": False, "once_per_turn_restriction": False},
        "badge_ui": {"scene": "game/Spire408/Scenes/StudyCombatHud.tscn", "native_button": "NButton",
                     "idle_seconds": 2.4, "active_seconds": 1.2, "hover_scale": 1.07,
                     "status_text": False, "preview": "reports/screenshots/badge-interaction-061.gif"},
        "art": {"resource_master": "art/knowledge-resource-transparent.png", "generator": "built-in imagegen",
                "mode": "generate", "transparent_background": True},
        "question_quality": {"shipped": quality["shipped_count"], "quarantined": quality["quarantined_count"],
                             "locally_corrected": len(quality["corrected_ids"]),
                             "cross_source_candidate_pairs": len(comparison["matched"]),
                             "cross_source_answer_conflicts": len(comparison["conflicts"]),
                             "independently_calculated": quality["independently_calculated_count"],
                             "cross_checked_in_shipped_bank": quality["cross_checked_in_shipped_bank_count"],
                             "all_remaining_academically_reviewed": False,
                             "audit": "reports/question-quality-audit.json",
                             "scope_document": "docs/ACADEMIC-AUDIT.md",
                             "screenshot": "reports/screenshots/game-corrected-instruction-question.png",
                             "legacy_run_migration": "explicit_bank_hash_only",
                             "historical_gold_adjusted": False},
    }
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({k: report[k] for k in ("version", "core_checks", "game_checks", "installed_bytes",
                                           "zip_bytes", "installed_hash_matches", "production_has_smoke")}, ensure_ascii=False))


if __name__ == "__main__":
    main()
