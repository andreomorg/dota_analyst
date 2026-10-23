"""Inspect the real checkpoint using metadata tensors; never load its 4B tensor payload into RAM/CUDA."""
import argparse
import json
from pathlib import Path


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--snapshot', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    import torch
    from coach_ml.runtime_qwen35.model import verify_snapshot, validate_text_layout
    assert not torch.cuda.is_initialized()
    verify_snapshot(args.snapshot)
    result = validate_text_layout(args.snapshot)
    assert not torch.cuda.is_initialized()
    result.update(cudaInitialized=False, tensorPayloadLoaded=False, gpuQualified=False)
    args.output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(result))


if __name__ == '__main__':
    main()
