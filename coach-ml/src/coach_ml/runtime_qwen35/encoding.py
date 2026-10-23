"""Local, no-thinking chat encoding; all prompt/padding labels remain the shared runtime's -100."""
from ..core.validation import require


def encode_example(messages, tokenizer, cfg):
    require(type(messages) is list and len(messages) == 3
            and [m.get('role') for m in messages] == ['system', 'user', 'assistant'], 'Expected aligned SFT messages')
    require(all(type(m.get('content')) is str and m['content'] for m in messages), 'Empty SFT message')
    require(type(tokenizer.eos_token) is str and tokenizer.eos_token and type(tokenizer.eos_token_id) is int,
            'Tokenizer has no explicit message terminator')
    prompt = tokenizer.apply_chat_template(messages[:2], tokenize=False, add_generation_prompt=True,
                                           enable_thinking=False)
    prefix = tokenizer(prompt, add_special_tokens=False)['input_ids']
    target = tokenizer(messages[2]['content'] + tokenizer.eos_token, add_special_tokens=False)['input_ids']
    require(prefix and target and target[-1] == tokenizer.eos_token_id, 'Invalid encoded message boundary')
    require(len(prefix) + len(target) <= cfg['maxTrainingTokens'], 'Qwen3.5 example exceeds budget; truncation forbidden')
    return prefix, target
