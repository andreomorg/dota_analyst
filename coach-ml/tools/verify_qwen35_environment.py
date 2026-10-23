"""CPU-only qualification and package inventory for the isolated runtime image."""
import argparse
from importlib import metadata, util
import json
from pathlib import Path
import platform
import shutil
import subprocess
import sys


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--snapshot', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--constraints-output', type=Path, required=True)
    args = parser.parse_args()
    import torch
    assert not torch.cuda.is_initialized(), 'CPU qualification must not initialize CUDA'
    subprocess.run([sys.executable, '-m', 'pip', 'check'], check=True)
    import causal_conv1d_cuda
    from transformers import AutoConfig, AutoTokenizer, Qwen3_5ForCausalLM
    config = AutoConfig.from_pretrained(args.snapshot, local_files_only=True, trust_remote_code=False)
    assert config.model_type == 'qwen3_5' and config.text_config.model_type == 'qwen3_5_text'
    tokenizer = AutoTokenizer.from_pretrained(args.snapshot, local_files_only=True, trust_remote_code=False)
    text = tokenizer.apply_chat_template([{'role': 'user', 'content': 'Teste de JSON.'}],
                                        tokenize=False, add_generation_prompt=True, enable_thinking=False)
    assert text and tokenizer(text, add_special_tokens=False)['input_ids']
    packages = dict(sorted((d.metadata['Name'].lower().replace('_', '-'), d.version)
                           for d in metadata.distributions()))
    constraints = Path('/opt/qwen35/constraints-qwen35.txt')
    for line in constraints.read_text(encoding='utf-8-sig').splitlines():
        if not line or line.startswith('#'):
            continue
        name, version = line.split('==')
        installed = metadata.version(name)
        if name in ('torch', 'torchvision'):
            installed = installed.split('+')[0]
        assert installed == version, f'Frozen package mismatch: {name}'
    assert all(util.find_spec(name) is not None for name in ('unsloth', 'unsloth_zoo', 'fla', 'peft'))
    assert not torch.cuda.is_initialized(), 'CPU qualification initialized CUDA'
    report = {'python': platform.python_version(), 'packages': packages, 'torch': torch.__version__,
              'cudaBuild': torch.version.cuda, 'cxx11Abi': torch._C._GLIBCXX_USE_CXX11_ABI,
              'causalConvExtensionImported': bool(causal_conv1d_cuda), 'textClass': Qwen3_5ForCausalLM.__name__,
              'tokenizerLoadedOffline': True, 'cudaInitialized': False, 'gpuQualified': False}
    args.output.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    shutil.copyfile('/opt/qwen35/install-report.json', args.output.parent / 'pip-install-report.json')
    # Local CUDA suffixes on torch/vision are supplied by the base image; public pins accept those builds.
    pins = {name: version.split('+')[0] if name in ('torch', 'torchvision') else version
            for name, version in packages.items() if name != 'dotaanalyst-coach-ml'}
    args.constraints_output.write_text('# Resolved Linux/Python 3.11/CUDA 12.8 environment.\n' +
                                      ''.join(f'{name}=={version}\n' for name, version in pins.items()), encoding='utf-8')
    print(json.dumps({'cpuEnvironmentVerified': True, 'packages': len(packages), 'gpuQualified': False}))


if __name__ == '__main__':
    main()
