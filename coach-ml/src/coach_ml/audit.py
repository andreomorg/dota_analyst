"""Read-only audits for local coaching experiments. No ML/runtime dependencies."""
from __future__ import annotations

import argparse
from collections import Counter
from itertools import zip_longest
import json
from pathlib import Path
import re
import sys


# Compatibility exports retain existing public imports. New modules use core directly.
from .core.validation import AuditError, require
from .core.json import DECODER, canonical, digest, finite_float, parse_json, reject_constant, unique_object
from .core.streams import iter_json_array, iter_jsonl


PARTITIONS = {
    "train": {"train", "train-known"},
    "validation": {"validation-new", "validation-known", "reserved-validation"},
    "test": {"test-new", "reserved-test"},
}


def load_manifest(file):
    value = parse_json(Path(file).read_text(encoding="utf-8-sig"))
    require(isinstance(value, dict) and isinstance(value.get("matches"), list),
            "Expected a match manifest")
    matches = {}
    for row in value["matches"]:
        require(isinstance(row, dict), "Invalid manifest match")
        match_id, source_hash = row.get("matchId"), row.get("sourceHash")
        require(isinstance(match_id, str) and match_id.isascii() and match_id.isdigit(),
                "Invalid manifest matchId")
        require(match_id not in matches, f"Duplicate manifest match: {match_id}")
        require(isinstance(source_hash, str) and re.fullmatch(r"[0-9a-f]{64}", source_hash),
                f"Invalid source hash for match {match_id}")
        require(row.get("split") in set.union(*PARTITIONS.values()),
                f"Unknown split for match {match_id}")
        matches[match_id] = row
    require(bool(matches), "Empty match manifest")
    return matches


def check_factual_target(request, target, index):
    evidence = request["evidence"]
    event, support, economy = evidence.get("event"), evidence.get("support"), evidence.get("economy")
    require(isinstance(event, dict) and isinstance(support, dict), f"Missing event/support at row {index}")
    require(event.get("kind") in ["death", "detected-fight"], f"Unknown event kind at row {index}")
    anchor, seconds = event.get("anchorSeconds"), event.get("eventSeconds")
    require(type(anchor) is int and type(seconds) is int and 0 <= anchor <= seconds,
            f"Invalid event chronology at row {index}")
    require(support.get("change") in ["unavailable", "recorded-separation-increased",
                                      "recorded-separation-decreased", "recorded-separation-unchanged"],
            f"Unknown support observation at row {index}")
    if economy is not None:
        require(isinstance(economy, dict) and type(economy.get("sampleSeconds")) is int
                and 0 <= economy["sampleSeconds"] <= anchor,
                f"Future or invalid economy snapshot at row {index}")
        for field in ["lastHits", "netWorth"]:
            require(economy.get(field) is None or type(economy[field]) is int,
                    f"Invalid economy field {field} at row {index}")
    expected = {"eventKind": event["kind"], "lastHits": economy.get("lastHits") if economy else None,
                "netWorth": economy.get("netWorth") if economy else None,
                "economySampleSeconds": economy["sampleSeconds"] if economy else None,
                "supportChange": support["change"]}
    require(canonical(target.get("observation")) == canonical(expected),
            f"Target contradicts input observations at row {index}")
    focus = "survival-review" if event["kind"] == "death" else "fight-participation-review"
    require(target.get("reviewFocus") == focus, f"Review focus contradicts event kind at row {index}")
    require(all(isinstance(target.get(field), str) and bool(target[field].strip()) and len(target[field]) <= 700
                for field in ["exercise", "limitations"]), f"Invalid review text at row {index}")
    required_refs = {"event", "support"} | ({"economy"} if economy is not None else set())
    require(set(target["evidenceIds"]) == required_refs, f"Missing or unexpected evidence at row {index}")


def audit_sft(dataset, metadata, manifest, partition, profile="factual-review"):
    if profile in ('tactical-review-v1', 'tactical-review-v5'):
        from .tactical.audit import audit_sft as audit_tactical
        return audit_tactical(dataset, metadata, manifest, partition, profile)
    require(partition in PARTITIONS, "Unknown audit partition")
    require(profile in ["factual-review", "competency-review-v1"], "Unknown audit profile")
    matches = load_manifest(manifest)
    scenes, used_matches, input_counts, target_counts = set(), set(), Counter(), Counter()
    exercises, focuses, reviews, role_counts = Counter(), Counter(), Counter(), Counter()
    competency_exercises = {}
    rows = 0
    missing = object()
    for index, (record, meta) in enumerate(zip_longest(iter_jsonl(dataset), iter_json_array(metadata),
                                                     fillvalue=missing)):
        require(record is not missing and meta is not missing, "Dataset and metadata lengths differ")
        require(isinstance(record, dict) and isinstance(meta, dict), f"Invalid row {index}")
        require(type(meta.get("row")) is int and meta["row"] == index, f"Metadata row mismatch at {index}")
        match_id = meta.get("matchId")
        require(isinstance(match_id, str) and match_id in matches, f"Unknown source match at row {index}")
        match = matches[match_id]
        require(match["split"] in PARTITIONS[partition],
                f"Partition leakage at row {index}: {match_id} belongs to {match['split']}")
        require(meta.get("sourceHash") == match["sourceHash"], f"Source hash mismatch at row {index}")
        hero_id, episode = meta.get("heroId"), meta.get("episodeId")
        require(type(hero_id) is int and hero_id > 0 and isinstance(episode, str) and bool(episode),
                f"Invalid source identity at row {index}")
        scene_id = f"{match_id}/{hero_id}/{episode}"
        require(meta.get("sourceSceneId") == scene_id, f"Source scene mismatch at row {index}")
        identity = scene_id
        if profile == "competency-review-v1":
            skill = meta.get("competencyId")
            require(isinstance(skill, str) and bool(skill), f"Missing competency at row {index}")
            identity += "/" + skill
            require(meta.get("exampleId") == identity, f"Competency example identity mismatch at row {index}")
        require(identity not in scenes, f"Duplicate source scene: {identity}")
        scenes.add(identity)
        used_matches.add(match_id)
        messages = record.get("messages")
        require(isinstance(messages, list) and len(messages) == 3
                and all(isinstance(m, dict) for m in messages)
                and [m.get("role") for m in messages] == ["system", "user", "assistant"]
                and all(isinstance(m.get("content"), str) and m["content"].strip() for m in messages),
                f"Expected system/user/assistant messages at row {index}")
        request, target = parse_json(messages[1]["content"]), parse_json(messages[2]["content"])
        require(isinstance(request, dict) and isinstance(target, dict), f"Expected JSON objects at row {index}")
        require(canonical(request) == canonical(meta.get("request")), f"Request alignment failed at row {index}")
        require(canonical(target) == canonical(meta.get("target")), f"Target alignment failed at row {index}")
        require(request.get("heroId") == hero_id and type(request.get("heroId")) is int,
                f"Request hero mismatch at row {index}")
        evidence, refs = request.get("evidence"), target.get("evidenceIds")
        require(isinstance(evidence, dict) and isinstance(refs, list) and bool(refs)
                and all(isinstance(ref, str) for ref in refs) and len(set(refs)) == len(refs)
                and all(ref in evidence for ref in refs), f"Invalid evidence references at row {index}")
        require(isinstance(target.get("exercise"), str) and target["exercise"].strip(),
                f"Missing review exercise at row {index}")
        if profile == "factual-review":
            check_factual_target(request, target, index)
        else:
            from .competencies import expected_response
            require(request.get("competencyId") == meta["competencyId"], f"Competency mismatch at row {index}")
            try:
                expected = expected_response(request)
            except (ValueError, KeyError, TypeError) as exc:
                raise AuditError(f"Invalid competency input at row {index}: {exc}") from exc
            require(canonical(target) == canonical(expected), f"Competency policy target mismatch at row {index}")
            competency_exercises.setdefault(meta["competencyId"], Counter())[digest(target["exercise"])] += 1
        input_counts[digest(request)] += 1
        target_counts[digest(target)] += 1
        exercises[digest(target["exercise"])] += 1
        focuses[target["reviewFocus"]] += 1
        role = request.get("role")
        require(isinstance(role, dict) and type(role.get("position")) is int and 1 <= role["position"] <= 5,
                f"Invalid role at row {index}")
        role_counts[str(role["position"])] += 1
        reviews["human-reviewed" if meta.get("humanReviewed") is True
                else "assistant-provisional" if meta.get("provisionalPolicyTarget") is True
                else "unspecified"] += 1
        rows += 1
    require(rows > 0, "Empty SFT dataset")
    warnings = []
    if len(exercises) <= 2 and rows >= 20:
        warnings.append("LOW_EXERCISE_DIVERSITY: many records use at most two exercise templates")
    if max(exercises.values()) / rows >= 0.5 and rows >= 20:
        warnings.append("DOMINANT_EXERCISE: one exercise accounts for at least half of records")
    if reviews["human-reviewed"] == 0:
        warnings.append("NO_HUMAN_REVIEW: targets have no declared human tactical review")
    if reviews["unspecified"]:
        warnings.append("UNSPECIFIED_REVIEW_ORIGIN: some target authorship/review is not declared")
    return {"schemaVersion": 1, "kind": "sft-integrity-and-diversity", "partition": partition, "profile": profile,
            "rows": rows, "matches": len(used_matches), "roles": dict(role_counts),
            "uniqueInputs": len(input_counts), "duplicateInputOccurrences": rows - len(input_counts),
            "uniqueTargets": len(target_counts), "uniqueExercises": len(exercises),
            "dominantExerciseShare": max(exercises.values()) / rows,
            "reviewFocusCounts": dict(focuses), "reviewOriginCounts": dict(reviews),
            "competencyDiversity": {key: {"examples": sum(v.values()), "uniqueExercises": len(v),
                                            "dominantExerciseShare": max(v.values()) / sum(v.values())}
                                     for key, v in competency_exercises.items()},
            "structuralIntegrityPassed": True, "warnings": warnings,
            "tacticalQualityApproved": False, "trainingEligibilityGranted": False,
            "inputTargetFactsRechecked": True, "sourceFactsRecomputed": False}


def training_status(experiment):
    from .progress import training_status as read_status
    return read_status(experiment)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    audit = commands.add_parser("audit-sft", help="Check factual-review SFT alignment and partition boundaries")
    for flag in ["dataset", "metadata", "manifest"]:
        audit.add_argument("--" + flag, required=True, type=Path)
    audit.add_argument("--partition", choices=sorted(PARTITIONS), required=True)
    audit.add_argument("--profile", choices=["factual-review", "competency-review-v1", "tactical-review-v1", "tactical-review-v5"], default="factual-review")
    status = commands.add_parser("status", help="Read training files; does not inspect/start/stop processes")
    status.add_argument("--experiment", required=True, type=Path)
    args = parser.parse_args(argv)
    try:
        if args.command == "audit-sft":
            report = audit_sft(args.dataset, args.metadata, args.manifest, args.partition, args.profile)
        else:
            report = training_status(args.experiment)
        print(json.dumps(report, ensure_ascii=False, allow_nan=False, indent=2))
        return 2 if args.command == 'status' and not report['statusIntegrityPassed'] else 0
    except (AuditError, OSError, UnicodeError, RecursionError) as exc:
        print(json.dumps({"error": str(exc), "structuralIntegrityPassed": False}), file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
