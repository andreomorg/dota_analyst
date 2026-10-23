"""Signal-driven targets. All decisions and cited values come from the Domain signals."""
import copy
import math
from ..core.validation import require
from .policy_v2 import render_target
from .signal_wording import details_for, CITED_FIELDS
from .situated import FIELDS

NAMES = ('outnumbered', 'low-health', 'escape-ready-under-damage', 'isolated-enemy-side',
         'ahead-without-observer', 'teleport-defense', 'ultimate-window', 'roshan-window',
         'advance-poor-return-vision', 'tower-route-exposed', 'distant-ally-pings')


def validate_signals(signals):
    require(type(signals) is list, 'Expected an ordered signals list')
    previous = 0
    for signal in signals:
        require(type(signal) is dict and set(signal) == {'id', 'name', 'numbers'}, 'Invalid signal fields')
        require(signal['id'] in [f'R{i}' for i in range(1, len(NAMES) + 1)], 'Unknown signal rule')
        index = int(signal['id'][1:])
        require(index > previous and signal['name'] == NAMES[index - 1], 'Invalid signal priority or name')
        previous = index
        require(type(signal['numbers']) is dict and all(k in FIELDS for k in signal['numbers']), 'Invalid cited fields')
        require(set(signal['numbers']) == set(CITED_FIELDS[index - 1]), 'Missing or extra cited fields')
        require(all(v is None or type(v) in (int, float) and math.isfinite(v)
                    for v in signal['numbers'].values()), 'Invalid cited number')
    return signals


def legacy_request(request):
    result = copy.deepcopy(request)
    result['task'] = 'tactical-review-v5'
    result['facts'] = [f for f in result['facts'] if f['id'] != 'signals']
    return result


def target_for(request):
    require(request.get('task') == 'tactical-review-v52' and request.get('schemaVersion') == 5,
            'Policy v3 requires situated v5.2')
    signals = validate_signals(next(f['value'] for f in request['facts'] if f['id'] == 'signals'))
    legacy = legacy_request(request)
    situation = next(f['value'] for f in legacy['facts'] if f['id'] == 'situation') or {}
    def get(field):
        return situation.get(FIELDS[field][0], {}).get(field)
    numbers = signals[0]['numbers'] if signals else {}
    def fmt(field, percent=False):
        require(field in numbers, 'Signal omitted a cited field: ' + field)
        value = numbers[field]
        return 'desconhecido' if value is None else (f'{value * 100:.0f}%' if percent else f'{value:g}')
    # Only the selected wording is formatted: other rules must never read values from the scene.
    def optional_fmt(field, percent=False):
        value = numbers.get(field)
        return 'desconhecido' if value is None else (f'{value * 100:.0f}%' if percent else f'{value:g}')
    rule = signals[0]['id'] if signals else 'R0'
    details = details_for(optional_fmt)[int(rule[1:]) - 1] if signals else None
    # Require every field referenced by the selected wording, including explicit unknowns.
    if signals:
        from .signal_wording import CITED_FIELDS
        for field in CITED_FIELDS[int(rule[1:]) - 1]:
            fmt(field)
    return render_target(legacy, rule, details, situation, get)


def audit_agreement(request):
    from .policy_v2 import target_for as old_target
    current, historical = target_for(request), old_target(legacy_request(request))
    require(current == historical, 'Domain signals disagree with policy v2 rule, actions or numbers')
    return current
