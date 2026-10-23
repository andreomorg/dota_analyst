from .prompt_v5 import SYSTEM_V5

SYSTEM_V52 = SYSTEM_V5 + '\nO primeiro sinal é o foco; cite os números dele. Lista vazia significa informação insuficiente.\n'


def system_for(profile, legacy):
    if profile == 'tactical-review-v52':
        return SYSTEM_V52
    return SYSTEM_V5 if profile == 'tactical-review-v5' else legacy
