"""Pinned upstream -> local offline bank. Python 3.10+, standard library only.

Does not invent answers, execute upstream code, or deploy into the game.
All accepted questions are structurally checked, NOT academically verified.
"""
from __future__ import annotations

import argparse
from collections import Counter
from concurrent.futures import ThreadPoolExecutor, as_completed
import hashlib
import html
import json
from pathlib import Path, PurePosixPath
import re
import sys
import time
import unicodedata
from urllib.parse import quote, unquote, urlsplit
from urllib.request import Request, urlopen
from question_quality import load_policy, apply_quality, update_manifest

ROOT = Path(__file__).resolve().parents[1]
REPO = "lij768423-svg/408-"
COMMIT = "267d0d815b1eee770dad12e88bbd560d08ba3f66"
BASE = f"https://raw.githubusercontent.com/{REPO}/{COMMIT}/"
SUBJECTS = {"数据结构": "ds", "计算机组成原理": "co", "操作系统": "os", "计算机网络": "cn"}
MD_IMAGE = re.compile(r'!\[[^\]]*\]\(\s*(<[^>]+>|[^\s)]+)(?:\s+"[^"]*")?\s*\)')
HTML_IMAGE = re.compile(r'<img\b[^>]*\bsrc=[\"\']([^\"\']+)[\"\'][^>]*>', re.I)
EXAM = re.compile(r"统考|真题|考研试题|全国硕士|研究生入学考试")
PRACTICE = re.compile(r"模拟题|模拟试题|原创题|自编题")


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def save_json(path: Path, value) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    temporary.replace(path)


def clean(text: str) -> str:
    # Preserve code indentation, LaTeX and mathematical Unicode. Never guess OCR repairs.
    return unicodedata.normalize("NFC", text).replace("\r\n", "\n").replace("\r", "\n").replace("\ufeff", "").strip()


def classify(stem: str) -> dict:
    # Only inspect the question, not an explanation mentioning unrelated past exams.
    years = sorted(set(int(x) for x in re.findall(r"(?<!\d)(20\d{2})(?!\d)", stem[:160])))
    if EXAM.search(stem):
        return {"kind": "past_exam", "years": years, "evidence": "题干存在考试来源标记"}
    if PRACTICE.search(stem):
        return {"kind": "practice", "years": [], "evidence": "题干明确标为模拟或原创题"}
    # A year+exam label could be unmarked historical exam material.
    if re.search(r"[【\[][^】\]]*(?:19|20)\d{2}[^\u3011\]]*[\u3011\]]", stem[:160]):
        return {"kind": "suspected_exam", "years": years, "evidence": "题干带年份来源标签，尚未确认"}
    return {"kind": "unknown", "years": [], "evidence": "上游缺少可验证的非真题声明"}


def asset_path(ref: str) -> str:
    value = html.unescape(unquote(ref.strip("<>")))
    parts = urlsplit(value)
    if parts.scheme or parts.netloc or parts.query or parts.fragment or "\\" in value:
        raise ValueError(f"不支持外链或含参数的图片路径: {ref}")
    path = PurePosixPath(parts.path)
    if path.is_absolute() or ".." in path.parts or not path.parts or ":" in value:
        raise ValueError(f"图片路径越界: {ref}")
    if path.suffix.lower() not in {".png", ".jpg", ".jpeg", ".gif", ".webp"}:
        raise ValueError(f"不支持的图片格式: {ref}")
    return path.as_posix()


def image_refs(text: str) -> list[str]:
    return sorted(set(asset_path(m.group(1)) for pattern in (MD_IMAGE, HTML_IMAGE) for m in pattern.finditer(text)))


def normalize(q: dict) -> dict:
    if q.get("hidden") is True:
        raise ValueError("upstream_hidden")
    if q.get("type") != "single_choice":
        raise ValueError("not_single_choice")
    if q.get("book") not in SUBJECTS:
        raise ValueError("unknown_subject")
    for field in ("id", "question", "explanation"):
        if not isinstance(q.get(field), str) or not q[field].strip():
            raise ValueError(f"missing_{field}")
    options = q.get("options")
    if not isinstance(options, dict) or set(options) != set("ABCD"):
        raise ValueError("invalid_options")
    if any(not isinstance(v, str) or not v.strip() for v in options.values()):
        raise ValueError("empty_option")
    answers = q.get("answer")
    if not isinstance(answers, list) or len(answers) != 1 or answers[0] not in options:
        raise ValueError("invalid_answer")
    subject = SUBJECTS[q["book"]]
    stem = clean(q["question"])
    options = {k: clean(options[k]) for k in "ABCD"}
    explanation = clean(q["explanation"])
    refs = image_refs("\n".join([stem, *options.values(), explanation]))
    identity = digest((REPO + ":" + q["id"]).encode())[:24]
    content = json.dumps([subject, stem, options, answers[0]], ensure_ascii=False, sort_keys=True)
    return {
        "id": f"408-{identity}", "revision": digest(content.encode())[:16],
        "subject": subject, "subject_name": q["book"], "type": "single_choice",
        "chapter": str(q.get("chapter", "")), "chapter_title": q.get("chapter_title", ""),
        "section": str(q.get("section", "")), "section_title": q.get("section_title", ""),
        "stem": stem, "options": options, "answer": answers[0], "explanation": explanation,
        "exam": classify(stem), "assets": refs,
        "quality": {"structural_validation": "passed", "academic_review": "unreviewed"},
        "source": {"repository": REPO, "commit": COMMIT, "upstream_id": q["id"],
                   "source_file": q.get("source_file"), "code_license": "ISC",
                   "question_redistribution": "unverified"},
    }


def fingerprint(q: dict) -> str:
    # Exact content-equivalent duplicates only; do not merge semantically similar exercises.
    stem = re.sub(r"[【\[][^\u3011\]]*(?:统考|真题)[^\u3011\]]*[\u3011\]]", "", q["stem"])
    parts = [q["subject"], stem, *[q["options"][k] for k in "ABCD"]]
    return digest(json.dumps(parts, ensure_ascii=False).encode())


def fetch(relative: str, offline: bool) -> bytes:
    cache = ROOT / ".cache" / COMMIT / relative
    if cache.is_file():
        return cache.read_bytes()
    if offline:
        raise FileNotFoundError(f"离线缓存缺失: {relative}")
    last_error = None
    for attempt in range(3):
        try:
            with urlopen(Request(BASE + quote(relative, safe="/"), headers={"User-Agent": "408InSpire-import/1"}), timeout=30) as response:
                data = response.read(25 * 1024 * 1024 + 1)
            if len(data) > 25 * 1024 * 1024:
                raise ValueError("单文件超出 25 MB 限制")
            cache.parent.mkdir(parents=True, exist_ok=True)
            temp = cache.with_suffix(cache.suffix + ".download")
            temp.write_bytes(data)
            temp.replace(cache)
            return data
        except Exception as exc:
            last_error = exc
            if attempt < 2:
                time.sleep(attempt + 1)
    raise RuntimeError(f"{relative}: {last_error}")


def valid_image(data: bytes) -> bool:
    return (data.startswith(b"\x89PNG\r\n\x1a\n") or data.startswith(b"\xff\xd8\xff")
            or data[:6] in (b"GIF87a", b"GIF89a")
            or (data[:4] == b"RIFF" and data[8:12] == b"WEBP"))


def build(offline: bool = False) -> dict:
    raw = fetch("data.json", offline)
    policy = load_policy()
    if policy["source_commit"] != COMMIT or policy["source_sha256"] != digest(raw):
        raise RuntimeError("Question quality policy does not match the pinned source; review before importing.")
    source = json.loads(raw)
    rejected, candidates, seen_ids = [], [], set()
    groups = {}
    for item in source["questions"]:
        try:
            q = normalize(item)
            if not apply_quality(q, policy):
                rejected.append({"upstream_id": item["id"], "id": q["id"], "reason": "quality_quarantine",
                                 "detail": policy.get('exclusions',{}).get(q['id'],{}).get('reason',policy["reason"])})
                continue
            if q["id"] in seen_ids:
                raise ValueError("duplicate_id")
            seen_ids.add(q["id"])
            groups.setdefault(fingerprint(q), []).append(q)
        except ValueError as exc:
            rejected.append({"upstream_id": item.get("id"), "reason": str(exc)})
    for group in groups.values():
        if len({q["answer"] for q in group}) > 1:
            rejected.extend({"upstream_id": q["source"]["upstream_id"], "reason": "duplicate_answer_conflict"} for q in group)
            continue
        # Prefer the copy labelled as an exam, preventing an untagged duplicate leaking past filtering.
        group.sort(key=lambda q: ({"past_exam": 0, "suspected_exam": 1, "unknown": 2, "practice": 3}[q["exam"]["kind"]], q["id"]))
        canonical = group[0]
        canonical["aliases"] = [q["id"] for q in group[1:]]
        candidates.append(canonical)
        rejected.extend({"upstream_id": q["source"]["upstream_id"], "reason": "duplicate_content", "canonical_id": canonical["id"]} for q in group[1:])
    refs = sorted({ref for q in candidates for ref in q["assets"]})
    assets, failures = {}, {}
    print(f"检查 {len(candidates)} 道候选题，获取 {len(refs)} 张本地图片…", flush=True)
    with ThreadPoolExecutor(max_workers=6) as pool:
        jobs = {pool.submit(fetch, ref, offline): ref for ref in refs}
        for future in as_completed(jobs):
            ref = jobs[future]
            try:
                data = future.result()
                if not valid_image(data):
                    raise ValueError("图片文件签名无效")
                target = ROOT / "data" / ref
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(data)
                assets[ref] = {"sha256": digest(data), "bytes": len(data)}
            except Exception as exc:
                failures[ref] = str(exc)
    questions = []
    for q in candidates:
        missing = sorted(set(q["assets"]) & failures.keys())
        if missing:
            rejected.append({"upstream_id": q["source"]["upstream_id"], "reason": "missing_asset", "assets": missing})
        else:
            questions.append(q)
    questions.sort(key=lambda q: q["id"])
    license_text = fetch("LICENSE", offline).decode("utf-8")
    notice = ROOT / "data" / "UPSTREAM-LICENSE.txt"
    notice.parent.mkdir(parents=True, exist_ok=True)
    notice.write_text(license_text, encoding="utf-8")
    bank = {"schema_version": 1, "source_commit": COMMIT, "source_sha256": digest(raw),
            "asset_root": ".", "quality_update": update_manifest(policy, questions), "questions": questions}
    report = {"source_repository": REPO, "source_commit": COMMIT, "source_sha256": digest(raw),
              "input_count": len(source["questions"]), "accepted_count": len(questions),
              "excluded_count": len(rejected), "excluded_reasons": dict(Counter(q["reason"] for q in rejected)),
              "subjects": dict(Counter(q["subject_name"] for q in questions)),
              "exam_classification": dict(Counter(q["exam"]["kind"] for q in questions)),
              "images_downloaded": len(assets), "image_bytes": sum(v["bytes"] for v in assets.values()),
              "asset_failures": failures, "academic_accuracy": "not_verified",
              "quality_quarantined_count": sum(q["reason"] == "quality_quarantine" for q in rejected),
              "locally_corrected_ids": [q["id"] for q in questions if "erratum" in q["quality"]],
              "strict_no_exam_count": sum(q["exam"]["kind"] == "practice" for q in questions),
              "exclusions": rejected}
    save_json(ROOT / "data" / "questions.json", bank)
    save_json(ROOT / "data" / "assets-manifest.json", dict(sorted(assets.items())))
    save_json(ROOT / "reports" / "import-report.json", report)
    print(json.dumps({k: v for k, v in report.items() if k != "exclusions"}, ensure_ascii=False, indent=2), flush=True)
    return report


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--offline", action="store_true", help="只使用缓存；缺图片题会被隔离")
    args = parser.parse_args()
    result = build(args.offline)
    if not result["accepted_count"] or result["asset_failures"]:
        raise SystemExit(2)
