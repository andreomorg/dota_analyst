"""Paired factual/policy counts; examples are not independent matches."""

MEASURES = ('strictValid', 'normalizedValid', 'strictObservationCorrect',
            'normalizedObservationCorrect', 'strictFocusCorrect', 'exactPolicy')


def empty_group():
    return {'cases': 0, 'metrics': {name: dict.fromkeys(
        ['bothCorrect', 'gained', 'regressed', 'bothIncorrect'], 0) for name in MEASURES}}


class PairedComparison:
    def __init__(self):
        self.overall = empty_group()
        self.by_competency = {}
        self.by_match = {}

    def add(self, match_id, competency, base, adapted):
        groups = [self.overall, self.by_competency.setdefault(competency, empty_group()),
                  self.by_match.setdefault(match_id, empty_group())]
        for group in groups:
            group['cases'] += 1
            for metric in MEASURES:
                before, after = base[metric], adapted[metric]
                outcome = ('bothCorrect' if before and after else 'gained' if after else
                           'regressed' if before else 'bothIncorrect')
                group['metrics'][metric][outcome] += 1

    def report(self):
        return {'overall': self.overall, 'byCompetency': self.by_competency,
                'byMatch': self.by_match, 'independentGames': len(self.by_match),
                'caseCountsAreNotIndependentGames': True, 'tacticalImprovementValidated': False}


def failure_reasons(scores):
    if scores['truncated']:
        return ['truncated']
    reasons = []
    if not scores['strictValid']:
        reasons.append('non-strict-json' if scores['normalizedValid'] else 'invalid-json-or-contract')
    if scores['normalizedValid'] and not scores['normalizedObservationCorrect']:
        reasons.append('observation-mismatch')
    if scores['strictValid'] and not scores['strictFocusCorrect']:
        reasons.append('focus-mismatch')
    if scores['strictValid'] and scores['strictObservationCorrect'] and scores['strictFocusCorrect'] and not scores['exactPolicy']:
        reasons.append('policy-text-difference')
    return reasons
