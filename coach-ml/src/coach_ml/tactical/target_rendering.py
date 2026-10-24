"""Shared presentation for versioned tactical policies; no decision thresholds."""
from ..core.json import digest
from .contracts import validate_candidate
from .situated import FIELDS, V51_FIELDS


def render_target(request, rule_id, details, situation, get):
    opening = ('Comparar', 'Revisar', 'Examinar', 'Conferir')[int(digest(request)[:8], 16) % 4]
    refs = ['situation']
    candidate = {
        'schemaVersion': 1, 'disposition': 'conditional-review' if details else 'insufficient',
        'opportunity': '', 'evidenceIds': refs, 'alternatives': [], 'preference': 'undetermined',
        'exercise': {'steps': [], 'completionCriterion': '', 'evidenceIds': refs},
        'limitations': ['Comparação provisória; prontidão e contagens não demonstram uma opção superior.'],
    }
    if details:
        scene, action_a, action_b, condition, condition_b = details
        candidate['opportunity'] = f'{opening} {action_a.lower()} ou {action_b.lower()} {scene}?'
        for identifier, action, check in (
                ('A', action_a, condition),
                ('B', action_b, condition_b)):
            candidate['alternatives'].append({
                'id': identifier, 'action': action, 'conditions': ['Verificar se ' + check + '.'],
                'evidenceIds': refs, 'unknowns': [u for u in request['unknowns']
                                               if u in ('intent', 'safe-path', 'patch-mechanics')],
            })
        candidate['exercise']['steps'] = [
            f'{opening} na âncora: {scene}.',
            'Anotar para A e B quais verificações têm evidência e quais ainda precisam de informação.',
        ]
        candidate['exercise']['completionCriterion'] = (
            'Registrar uma condição verificável que separa A de B, sem escolher por resultado posterior.')
    else:
        # Only fields the scene has (a v5.1 field absent from an older context is not a gap of that scene).
        present = ({field for values in situation.values() for field in values} if situation
                   else set(FIELDS) - V51_FIELDS)
        missing_fields = [field for field in FIELDS if field in present and get(field) is None]
        gap = ', '.join(missing_fields[:5]) if missing_fields else 'condições que distinguem as ações'
        candidate['opportunity'] = f'{opening} quais dados faltam para comparar duas ações na âncora.'
        candidate['exercise']['steps'] = [
            f'Conferir lacunas da âncora: {gap}.',
            'Obter a informação que falta e verificar se sustenta duas ações e uma condição que as diferencie.',
        ]
        candidate['exercise']['completionCriterion'] = (
            'Registrar os campos ausentes ou insuficientes e a informação necessária para comparar duas ações específicas.')
        candidate['limitations'] = ['Nenhuma regra R1–R8 tem condições observadas suficientes nesta âncora.']
    return rule_id, validate_candidate(request, candidate)
