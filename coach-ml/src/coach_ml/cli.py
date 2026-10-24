"""CLI dispatch keeps PyTorch and GPU initialization out of data-only commands."""
import argparse
import sys


def main(argv=None):
    argv = list(sys.argv[1:] if argv is None else argv)
    parser = argparse.ArgumentParser(prog="coach-ml", description="Local coaching datasets and experiments")
    commands = parser.add_subparsers(dest="command", required=True)
    for name, help_text in [("audit-sft", "Audit factual or competency SFT"),
                            ("audit-experiment", "Check train/validation integrity and shared inputs"),
                            ("coverage", "Measure hero/role/phase/task coverage and validation gaps"),
                            ("deduplicate", "Create an audited dataset without repeated model inputs"),
                            ("token-budget", "Measure local tokenizer budgets without loading weights"),
                            ("qwen35-audit", "Audit Qwen3.5 encoding and budgets offline before GPU use"),
                            ("status", "Read experiment progress"),
                            ("build-curriculum", "Prepare a new provisional curriculum"),
                            ("evaluate", "Compare saved base/adapted predictions without ML"),
                            ("snapshot-evaluation", "Copy completed predictions and evaluate without changing their source"),
                            ("serve", "Run the authenticated internal review service"),
                            ("run-control", "Inspect or request cooperative pause for a managed run"),
                            ("promotion-check", "Validate and record explicit promotion without installing a model"),
                            ("tactical-content-evaluate", "Compare saved tactical rules, actions and numbers with policy targets"),
                            ("tactical-content-diagnostics", "Locate paired gains and regressions in an existing F3 report"),
                            ("tactical-error-analysis", "Explain saved F3 errors without changing their scores"),
                            ("tactical-supervision-audit", "Diagnose visible-input conflicts and deterministic label agreement"),
                            ("tactical-infer", "Run separate development-only tactical inference without approving labels"),
                            ("tactical-review", "Critique tactical outputs with explicit provisional provenance"),
                            ("tactical-curate", "Export aligned provisional tactical labels without starting training"),
                            ("tactical-assemble", "Assemble a separate tactical experiment from curated train and diagnostic validation"),
                            ("tactical-refine", "Run a separate calibrated teacher recipe without updating weights"),
                            ("tactical-reference-audit", "Recompute published item-property history without approving training"),
                            ("risk-export", "Export death-risk samples from the local API into gzip CSV"),
                            ("risk-train", "Train the death-risk models on the development partitions (CPU)"),
                            ("risk-evaluate", "Evaluate saved death-risk models on validation or, once, the final test"),
                            ("risk-explain", "Explain the visible death-risk model overall and per death"),
                            ("region-train", "Train the next-region models (where a hero goes in 30 s, CPU)"),
                            ("region-evaluate", "Evaluate saved next-region models on validation or, once, the final test"),
                            ("region-hints", "List candidate tips from the next-region model for review by hand"),
                            ("position-measure", "Measure the C# position estimator against role-queue positions"),
                            ("train", "Inspect, prepare or run a separate LoRA experiment")]:
        commands.add_parser(name, help=help_text, add_help=False)
    args, remaining = parser.parse_known_args(argv)
    if args.command in ["audit-sft", "status"]:
        from .audit import main as audit_main
        return audit_main([args.command, *remaining])
    if args.command == "build-curriculum":
        from .curriculum import main as curriculum_main
        return curriculum_main(remaining)
    if args.command == "audit-experiment":
        from .datasets import main as datasets_main
        return datasets_main(remaining)
    if args.command == "coverage":
        from .coverage import main as coverage_main
        return coverage_main(remaining)
    if args.command == "deduplicate":
        from .curation import main as curation_main
        return curation_main(remaining)
    if args.command == "qwen35-audit":
        from .runtime_qwen35.audit import main as qwen35_audit_main
        return qwen35_audit_main(remaining)
    if args.command == "token-budget":
        from .tokenization import main as tokenization_main
        return tokenization_main(remaining)
    if args.command == "serve":
        from .service import main as service_main
        return service_main(remaining)
    if args.command == "run-control":
        from .experiments.control import main as control_main
        return control_main(remaining)
    if args.command == "promotion-check":
        from .experiments.capabilities import main as promotion_main
        return promotion_main(remaining)
    if args.command == "evaluate":
        from .evaluation import main as evaluation_main
        return evaluation_main(remaining)
    if args.command == "snapshot-evaluation":
        from .snapshot import main as snapshot_main
        return snapshot_main(remaining)
    if args.command == "tactical-supervision-audit":
        from .tactical.supervision_audit import main as supervision_main
        return supervision_main(remaining)
    if args.command == "tactical-error-analysis":
        from .tactical.error_analysis import main as error_main
        return error_main(remaining)
    if args.command == "tactical-content-diagnostics":
        from .tactical.content_diagnostics import main as diagnostics_main
        return diagnostics_main(remaining)
    if args.command == "tactical-content-evaluate":
        from .tactical.content_evaluation import main as content_main
        return content_main(remaining)
    if args.command == "tactical-infer":
        from .tactical.pipeline import main as tactical_main
        return tactical_main(remaining)
    if args.command == "tactical-review":
        from .tactical.review import main as tactical_review_main
        return tactical_review_main(remaining)
    if args.command == "tactical-curate":
        from .tactical.dataset import main as tactical_dataset_main
        return tactical_dataset_main(remaining)
    if args.command == "tactical-assemble":
        from .tactical.assembly import main as tactical_assembly_main
        return tactical_assembly_main(remaining)
    if args.command == "tactical-refine":
        from .tactical.refinement import main as refinement_main
        return refinement_main(remaining)
    if args.command == "tactical-reference-audit":
        from .tactical.source_history import main as reference_main
        return reference_main(remaining)
    if args.command == "risk-export":
        from .risk.export import main as risk_export_main
        return risk_export_main(remaining)
    if args.command == "risk-train":
        from .risk.train import main as risk_train_main
        return risk_train_main(remaining)
    if args.command == "risk-evaluate":
        from .risk.evaluate import main as risk_evaluate_main
        return risk_evaluate_main(remaining)
    if args.command == "region-train":
        from .risk.next_region import train_main as region_train_main
        return region_train_main(remaining)
    if args.command == "region-evaluate":
        from .risk.next_region import evaluate_main as region_evaluate_main
        return region_evaluate_main(remaining)
    if args.command == "region-hints":
        from .risk.hints import main as region_hints_main
        return region_hints_main(remaining)
    if args.command == "position-measure":
        from .risk.positions import main as position_measure_main
        return position_measure_main(remaining)
    if args.command == "risk-explain":
        from .risk.explain import main as risk_explain_main
        return risk_explain_main(remaining)
    from .training import main as training_main
    return training_main(remaining)
