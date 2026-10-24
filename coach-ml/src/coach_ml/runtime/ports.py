"""Tensor-free runtime interfaces and generation composition inputs."""
from collections.abc import Callable, Sequence
from dataclasses import dataclass
from pathlib import Path
from types import ModuleType
from typing import Protocol, runtime_checkable

EncodedExample = tuple[list[int], list[int]]


class ChatTokenizer(Protocol):
    eos_token: str
    eos_token_id: int

    def apply_chat_template(self, messages: list[dict[str, str]], *, tokenize: bool,
                            add_generation_prompt: bool, enable_thinking: bool) -> str: ...

    def __call__(self, text: str, *, add_special_tokens: bool) -> dict[str, list[int]]: ...

    def decode(self, tokens: list[int], *, skip_special_tokens: bool) -> str: ...


@runtime_checkable
class ModelRuntime(Protocol):
    def tokenizer(self, snapshot: Path, cfg: dict[str, object]) -> ChatTokenizer: ...

    def encode(self, messages: list[dict[str, str]], tokenizer: ChatTokenizer,
               cfg: dict[str, object]) -> EncodedExample: ...

    def identity(self, snapshot: Path, cfg: dict[str, object]) -> dict[str, object]: ...

    def load_base(self, snapshot: Path, cfg: dict[str, object]) -> object: ...

    def attach(self, model: object, cfg: dict[str, object]) -> object: ...


class AdapterLoader(Protocol):
    def from_pretrained(self, model: object, path: Path, *, is_trainable: bool) -> object: ...


@dataclass(frozen=True)
class GenerationJob:
    root: Path
    output: Path
    cfg: dict[str, object]
    validation: Sequence[EncodedExample]
    tokenizer: ChatTokenizer


@dataclass(frozen=True)
class GenerationResources:
    torch: ModuleType
    json: ModuleType
    time: ModuleType
    gc: ModuleType
    load_base: Callable[[], object]
    adapter_loader: AdapterLoader
    prediction_prefix: Callable[[Path, list[dict[str, object]], str], int]
