"""Executable experimental output contract. Structural validity is not tactical approval."""
from ..core.validation import require


def _object(value, keys):
    require(type(value) is dict and set(value) == set(keys), 'Unexpected tactical fields')


def _text(value, limit):
    require(type(value) is str and 0 < len(value.strip()) <= limit, 'Invalid tactical text')


def _texts(value, maximum, limit, minimum=0):
    require(type(value) is list and minimum <= len(value) <= maximum, 'Invalid tactical list')
    for item in value:
        _text(item, limit)


def validate_candidate(request, value):
    _object(value, ('schemaVersion', 'disposition', 'opportunity', 'evidenceIds', 'alternatives',
                    'preference', 'exercise', 'limitations'))
    require(type(value['schemaVersion']) is int and value['schemaVersion'] == 1, 'Invalid tactical version')
    disposition = value['disposition']
    require(disposition in ('supported-review', 'conditional-review', 'insufficient'), 'Invalid disposition')
    _text(value['opportunity'], 400)
    available = {fact['id'] for fact in request['facts']}

    def refs(items):
        _texts(items, 8, 120, 1)
        require(len(set(items)) == len(items) and set(items) <= available, 'Unknown tactical evidence')

    refs(value['evidenceIds'])
    _texts(value['limitations'], 8, 700, 1)
    alternatives = value['alternatives']
    require(type(alternatives) is list, 'Invalid alternatives')
    expected = 0 if disposition == 'insufficient' else 2
    require(len(alternatives) == expected, 'Expected zero or two alternatives')
    for index, item in enumerate(alternatives):
        _object(item, ('id', 'action', 'conditions', 'evidenceIds', 'unknowns'))
        require(item['id'] == ('A', 'B')[index], 'Expected ordered alternatives A and B')
        _text(item['action'], 400)
        _texts(item['conditions'], 4, 400, 1)
        refs(item['evidenceIds'])
        _texts(item['unknowns'], 12, 120)
        require(set(item['unknowns']) <= set(request['unknowns']), 'Invented unknown category')
    require(value['preference'] in (('A', 'B') if disposition == 'supported-review' else ('undetermined',)),
            'Preference contradicts disposition')
    exercise = value['exercise']
    _object(exercise, ('steps', 'completionCriterion', 'evidenceIds'))
    _texts(exercise['steps'], 3, 300, 1)
    _text(exercise['completionCriterion'], 400)
    refs(exercise['evidenceIds'])
    return value
