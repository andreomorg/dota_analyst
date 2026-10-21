"""Validate and project precomputed C# support transitions, without outcomes or path inference."""
import math

from ..core.validation import require


def add_support(request, transition):
    anchor = request['anchorSeconds']
    require(transition['heroId'] == request['heroId'] and transition['anchorSeconds'] == anchor, 'Support identity mismatch')
    value = {'allyHeroId': transition['allyHeroId'], 'anchorSeconds': anchor,
             'unavailableReason': transition['unavailableReason'],
             'expectedPairedSeconds': transition['expectedPairedSeconds'],
             'observedPairedSeconds': transition['observedPairedSeconds'],
             'unit': 'dota-world-distance', 'distanceDecimalPlaces': 2}
    expected = 11 if anchor >= 10 else 0
    require(type(value['expectedPairedSeconds']) is int and value['expectedPairedSeconds'] == expected,
            'Invalid support expected coverage')
    observed = value['observedPairedSeconds']
    require(type(observed) is int and 0 <= observed <= expected, 'Invalid support observed coverage')
    for field, target in (('earlier', anchor-10), ('atAnchor', anchor)):
        sample = transition[field]
        if sample is None:
            value[field] = None
            continue
        require(target >= 0 and sample['targetSeconds'] == target, 'Invalid support target time')
        require(all(type(sample[k]) is int and max(0,target-2) <= sample[k] <= target
                    for k in ('viewerSampleSeconds', 'allySampleSeconds')), 'Future or stale support endpoint')
        distance = sample['distance']
        require(type(distance) in (int, float) and math.isfinite(distance) and distance >= 0, 'Invalid support distance')
        value[field] = {key: sample[key] for key in ('targetSeconds', 'viewerSampleSeconds', 'allySampleSeconds')}
        value[field]['distance'] = round(distance, 2)
    change = transition['change']
    require(type(change) is int and change in (0,1,2,3), 'Unknown support change')
    require((change == 0) == (transition['earlier'] is None or transition['atAnchor'] is None), 'Support availability contradiction')
    if change != 0:
        before, after = transition['earlier']['distance'], transition['atAnchor']['distance']
        require(change == (1 if after > before else 2 if after < before else 3), 'Support change contradicts distances')
    value['change'] = ('unavailable','recorded-separation-increased','recorded-separation-decreased','recorded-separation-unchanged')[change]
    request['facts'].append({'id':'support-transition', 'kind':'recorded-support-transition', 'sampleSeconds':anchor,
                             'value':value, 'source':'csharp:CoachingSupportTransitionBuilder', 'perspective':'omniscient',
                             'limitations':['Endpoints are not a continuous path; paired seconds are coverage, not movement duration.',
                                            'Nearest observed ally at anchor is not proof of available intervention.']})
    return request
