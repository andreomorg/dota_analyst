"""Local, no-thinking chat encoding; all prompt/padding labels remain the shared runtime's -100."""
from ..core.validation import require


def encode_messages(messages, tokenizer):
    require(type(messages) is list and len(messages) == 3
            and all(type(m) is dict for m in messages)
            and [m.get('role') for m in messages] == ['system', 'user', 'assistant'], 'Expected aligned SFT messages')
    require(all(type(m.get('content')) is str and m['content'].strip() for m in messages), 'Empty SFT message')
    require(type(tokenizer.eos_token) is str and tokenizer.eos_token and type(tokenizer.eos_token_id) is int,
            'Tokenizer has no explicit message terminator')
    # A replay string must not introduce a chat boundary or hide part of the supervised answer.
    # This tokenizer registers some chat markers as added tokens without marking them special.
    controls = (set(getattr(tokenizer, 'all_special_tokens', ()))
                | set(getattr(tokenizer, 'get_added_vocab', lambda: {})()) | {tokenizer.eos_token})
    require(not any(token and token in m['content'] for m in messages for token in controls),
            'SFT content contains a tokenizer control token')
    prompt = tokenizer.apply_chat_template(messages[:2], tokenize=False, add_generation_prompt=True,
                                           enable_thinking=False)
    prefix = tokenizer(prompt, add_special_tokens=False)['input_ids']
    target = tokenizer(messages[2]['content'] + tokenizer.eos_token, add_special_tokens=False)['input_ids']
    require(prefix and target and target[-1] == tokenizer.eos_token_id, 'Invalid encoded message boundary')
    require(target.count(tokenizer.eos_token_id) == 1, 'Expected one answer terminator')
    return prefix, target


def encode_example(messages, tokenizer, cfg):
    prefix, target = encode_messages(messages, tokenizer)
    require(len(target) <= cfg['maxNewTokens'],
            'Qwen3.5 answer exceeds generation budget; truncation forbidden')
    require(len(prefix) + len(target) <= cfg['maxTrainingTokens'], 'Qwen3.5 example exceeds budget; truncation forbidden')
    return prefix, target
