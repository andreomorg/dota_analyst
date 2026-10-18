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
                            ("status", "Read experiment progress"),
                            ("build-curriculum", "Prepare a new provisional curriculum"),
                            ("evaluate", "Compare saved base/adapted predictions without ML"),
                            ("snapshot-evaluation", "Copy completed predictions and evaluate without changing their source"),
                            ("serve", "Run the authenticated internal review service"),
                            ("run-control", "Inspect or request cooperative pause for a managed run"),
                            ("promotion-check", "Validate and record explicit promotion without installing a model"),
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
    from .training import main as training_main
    return training_main(remaining)
