"""Conservative lexical checks; no claim to certify arbitrary tactical prose."""
import re


def iter_text_values(value):
    if isinstance(value, str):
        yield value
    elif isinstance(value, list):
        for item in value:
            yield from iter_text_values(item)
    elif isinstance(value, dict):
        for item in value.values():
            yield from iter_text_values(item)


def iter_candidate_prose(candidate):
    if not isinstance(candidate,dict):
        return
    for key in ('opportunity','limitations','unstructuredResponse'):
        yield from iter_text_values(candidate.get(key))
    for alternative in candidate.get('alternatives',[]):
        if isinstance(alternative,dict):
            for key in ('action','conditions'):
                yield from iter_text_values(alternative.get(key))
    exercise = candidate.get('exercise',{})
    if isinstance(exercise,dict):
        for key in ('steps','completionCriterion'):
            yield from iter_text_values(exercise.get(key))


def audit_candidate_facts(request, candidate):
    facts = {fact['id']: fact['value'] for fact in request['facts']}
    coverage = facts['positions']
    samples = coverage['samples']
    viewer = next((p for p in samples if p['heroId'] == request['heroId']), None)
    allies, enemies = set(), set()
    if viewer is not None:
        allies = {p['heroId'] for p in samples if p['team'] == viewer['team'] and p['heroId'] != request['heroId']}
        enemies = {p['heroId'] for p in samples if p['team'] != viewer['team']}
    actions = facts.get('recorded-actions')
    if actions:
        team = next(h['team'] for h in actions['heroes'] if h['heroId'] == request['heroId'])
        allies = {h['heroId'] for h in actions['heroes'] if h['team']==team and h['heroId']!=request['heroId']}
        enemies = {h['heroId'] for h in actions['heroes'] if h['team']!=team}
    support = facts.get('support-transition')
    if support and support['allyHeroId'] is not None:
        allies.add(support['allyHeroId'])
    flags = []
    prose = list(iter_candidate_prose(candidate))
    if actions:
        names = {e['name'] for field in ('itemUses','abilityUses') for e in actions[field] or []}
        for part in [candidate,*candidate.get('alternatives',[]),candidate.get('exercise',{})]:
            texts = list(iter_candidate_prose(part)) if part is candidate else list(iter_text_values(
                {k:v for k,v in part.items() if k in ('action','conditions','steps','completionCriterion')}))
            quote = next((t for t in texts if any(name in t for name in names)),None)
            if quote and 'recorded-actions' not in part.get('evidenceIds',[]):
                flags.append({'kind':'use-claim-without-action-reference','quote':quote,
                              'evidenceIds':['recorded-actions'],'action':'requires-content-review'})
        alternatives = candidate.get('alternatives',[])
        if len(alternatives)==2:
            normalize = lambda a:{re.sub(r'\s+',' ',t.strip().casefold()) for t in a.get('conditions',[])}
            if normalize(alternatives[0]) == normalize(alternatives[1]):
                flags.append({'kind':'identical-alternative-conditions','quote':alternatives[0]['conditions'][0],
                              'evidenceIds':['recorded-actions'],'action':'requires-content-review'})
    opportunity=candidate.get('opportunity','')
    opening=opportunity.split('. ')[0]
    conditional_opening=re.search(r'\b(?:verifi(?:car|que)|reavalie se|revise se|reveja se|confi(?:ra|rmar) se)\b',opening,re.I)
    if support and support.get('change')=='unavailable' and not conditional_opening:
        if re.search(r'\b(?:separação|distância)\b.*\b(?:aumentou|diminuiu)\b',opening,re.I):
            flags.append({'kind':'support-change-without-endpoints','quote':opening,
                          'evidenceIds':['support-transition'],'action':'requires-content-review'})
    objectives=facts['objectives']['events'] or []
    objective_claim=re.search(r'\b(?:torres?|barracões|barracão|objetivos?|Roshan|Tormentor)\b.*\b(?:tomad[oa]s?|registrad[oa]s?)\b|\b(?:tomada|perda)\b.*\b(?:torres?|barracões|objetivos?|Roshan|Tormentor)\b',opening,re.I)
    if objective_claim and not conditional_opening:
        if not objectives:
            flags.append({'kind':'objective-claim-without-record','quote':opening,
                          'evidenceIds':['objectives'],'action':'requires-content-review'})
        else:
            selected=objectives
            kind=3 if re.search(r'\bRoshan\b',opening,re.I) else 4 if re.search(r'\bTormentor\b',opening,re.I) else None
            if kind is None and re.search(r'\btorres?\b',opening,re.I) and not re.search(r'\bbarrac',opening,re.I):
                kind=0
            if kind is not None:
                selected=[event for event in selected if event['kind']==kind]
            time=re.search(r'\bem\s+(\d+)s\b',opening,re.I)
            if time:
                selected=[event for event in selected if event['seconds']==int(time.group(1))]
            ours=re.search(r'\b(?:pelo seu time|pelo time d[oa] (?:jogador|observador)|pela equipe do jogador)\b',opening,re.I)
            theirs=re.search(r'\b(?:pelo inimigo|pelo time (?:inimigo|adversário))\b',opening,re.I)
            if not selected or ((ours or theirs) and (viewer is None or not any(
                    event['takenBy'] is not None and ((event['takenBy']==viewer['team']) if ours else (event['takenBy']!=viewer['team']))
                    for event in selected))):
                flags.append({'kind':'objective-kind-time-or-team-contradiction','quote':opening,
                              'evidenceIds':['positions','objectives'],'action':'requires-content-review'})
            enemy_owned=re.search(r'\b(?:torres?|barracões|barracão|objetivos?)\s+inimig[oa]s?\b',opening,re.I)
            if enemy_owned and (viewer is None or not any(event['owner'] is not None and event['owner']!=viewer['team'] for event in selected)):
                flags.append({'kind':'objective-owner-contradiction','quote':opening,
                              'evidenceIds':['positions','objectives'],'action':'requires-content-review'})
    for text in [opportunity,*candidate.get('limitations',[])]:
        conditional=re.search(r'\b(?:verifi(?:car|que)|reavalie se|revise se|reveja se|confi(?:ra|rmar) se)\b',text,re.I)
        absence=re.search(r'\b(?:falta sua própria posição|posição do (?:observador|viewer) não (?:está registrada|é confirmada))\b',text,re.I)
        false_reason=re.search(r'\bausência de observação do (?:observador|viewer) em\s+(\d+)s\b',text,re.I)
        if viewer is not None and not conditional and (absence or (false_reason and int(false_reason.group(1))==request['anchorSeconds'])):
            flags.append({'kind':'observed-viewer-marked-absent','quote':text,
                          'evidenceIds':['positions'],'action':'requires-content-review'})
        if not conditional and re.search(r'\b(?:herói|jogador|viewer|observador)\s+\d+\s+(?:possui|tem|está com)\b',text,re.I):
            flags.append({'kind':'inventory-possession-without-source','quote':text,
                          'evidenceIds':[], 'action':'requires-content-review'})
    if candidate.get('preference') in ('A','B'):
        for text in candidate.get('limitations',[]):
            if re.search(r'\b(?:não há|sem) preferência (?:sustentada|justificada)\b',text,re.I):
                flags.append({'kind':'preference-contradicts-limitation','quote':text,
                              'evidenceIds':[], 'action':'requires-content-review'})
    if support and support.get('change') in ('recorded-separation-increased','recorded-separation-decreased'):
        opportunity = candidate.get('opportunity','')
        if re.search(r'\bseparação\b.*\b(?:diminuiu|aumentou)\b.*\bapós (?:o registro d[ae] |[ao] )?âncora\b',opportunity,re.I):
            flags.append({'kind':'support-window-after-anchor-claim','quote':opportunity,
                          'evidenceIds':['support-transition'], 'action':'requires-content-review'})
    for text in prose:
        for match in re.finditer(r'\b(?:âncora|ancora)\s+(?:(?:de|em|aos?)\s+)?(-?\d+)\s*s\b',text,re.I):
            if int(match.group(1)) != request['anchorSeconds']:
                flags.append({'kind':'anchor-time-contradiction','quote':match.group(0),
                              'expected':request['anchorSeconds'],'evidenceIds':[], 'action':'requires-content-review'})
        for match in re.finditer(r'\b(aliad[oa]s?|inimig[oa]s?)\s+(?:(?:her[oó]i|hero|jogador|id)\s+)?(\d+)\b', text, re.I):
            identifier = int(match.group(2))
            called_ally = match.group(1).lower().startswith('aliad')
            contradiction = (called_ally and identifier in enemies) or (not called_ally and identifier in allies)
            if contradiction:
                flags.append({'kind':'potential-team-contradiction', 'quote':match.group(0),
                              'heroId':identifier, 'evidenceIds':['positions'], 'action':'requires-content-review'})
            if called_ally and identifier==request['heroId']:
                flags.append({'kind':'viewer-referenced-as-own-ally','quote':match.group(0),
                              'heroId':identifier,'evidenceIds':['positions'],'action':'requires-content-review'})
        for match in re.finditer(r'\b(\d+)\s+aliados?\s+observados?\b', text, re.I):
            expected = coverage['observedAllies']
            if expected is not None and int(match.group(1)) != expected:
                flags.append({'kind':'potential-ally-count-contradiction', 'quote':match.group(0),
                              'expected':expected, 'evidenceIds':['positions'], 'action':'requires-content-review'})
    return {'checks':['explicit-Portuguese-team-reference','explicit-digit-ally-count',
                      'explicit-anchor-time','explicit-support-window-after-anchor',
                      'explicit-preference-limitation-contradiction','explicit-support-availability',
                      'explicit-objective-kind-time-team','explicit-viewer-absence',
                      'explicit-inventory-possession',
                      *(['explicit-use-evidence-reference','identical-alternative-conditions'] if actions else [])], 'flags':flags,
            'tacticalTruthCertified':False,
            'limitations':['Lexical matches may require negation/context review.',
                           'Implicit statements, arbitrary mechanics, safety and causal claims are not certified.']}
