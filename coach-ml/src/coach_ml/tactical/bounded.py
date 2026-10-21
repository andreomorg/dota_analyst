"""Optional canonical factual narration; never a claim of learned tactical preference."""
from ..core.validation import require
from .structured import candidate_schema

V1_POLICY='canonical-position-review-v1'
POLICY='canonical-position-review-v2'
POLICIES=(V1_POLICY,POLICY)
INSTRUCTIONS='''
Neste modo limitado, copie a opportunity canônica e selecione apenas limitações canônicas.
Não reescreva fatos nesses campos. Proponha alternativas condicionadas e exercício verificável;
não afirme posse de itens, intenção ou prontidão nas ações, condições ou exercício.
Preference é undetermined; não há capacidade de escolher a melhor jogada neste experimento.
Quando canonicalFacts contém recoveryExercise, copie esse exercício: ele identifica o dado realmente ausente.
'''


def _recovery(request,viewer,support):
    hero,anchor=request['heroId'],request['anchorSeconds']
    evidence=['positions']+(['support-transition'] if support else [])
    if viewer is None:
        targets=[{'heroId':hero,'targetSeconds':anchor}]
    elif not support or support['change']!='unavailable':
        return None
    else:
        reason=support['unavailableReason']
        if reason=='insufficient-earlier-window':
            return {'targets':[],'exercise':{'steps':[f'Registre o quadro disponível até {anchor}s e indique que não existe janela anterior válida neste recorte; não invente uma comparação de movimento.'],
                'completionCriterion':'Quadro atual e motivo da insuficiência anotados, sem inventar um extremo anterior.',
                'evidenceIds':evidence}}
        earlier=reason in ('earlier-viewer-unobserved','earlier-ally-unobserved','earlier-both-unobserved')
        target=anchor-10 if earlier else anchor
        require(target>=0,'Cannot recover a support endpoint before the valid window')
        targets=[]
        if reason in ('earlier-viewer-unobserved','earlier-both-unobserved','anchor-viewer-unobserved'):
            targets.append({'heroId':hero,'targetSeconds':target})
        if reason in ('earlier-ally-unobserved','earlier-both-unobserved','anchor-ally-unobserved'):
            targets.append({'heroId':support['allyHeroId'],'targetSeconds':target})
        require(targets,'Unknown support recovery reason')
    descriptions=[(f'herói {t["heroId"]}' if t['heroId'] is not None else 'aliados ainda não identificados')+
                  f' entre {max(0,t["targetSeconds"]-2)}s e {t["targetSeconds"]}s' for t in targets]
    step='Busque, se o replay permitir, posição de '+' e de '.join(descriptions)+'. Não use amostras posteriores; preserve a lacuna se ausente.'
    require(len(step)<=300,'Recovery exercise exceeds contract budget')
    return {'targets':targets,'exercise':{'steps':[step],
        'completionCriterion':'Anote instante e fonte da amostra recuperada; se não houver amostra válida, mantenha a lacuna e não infira mudança.',
        'evidenceIds':evidence}}


def fact_card(request,policy=POLICY):
    require(policy in POLICIES,'Unknown bounded facts policy')
    facts={fact['id']:fact['value'] for fact in request['facts']}
    hero,anchor=request['heroId'],request['anchorSeconds']
    viewer=next((p for p in facts['positions']['samples'] if p['heroId']==hero),None)
    support=facts.get('support-transition')
    directions={'recorded-separation-increased':'aumentou','recorded-separation-decreased':'diminuiu',
                'recorded-separation-unchanged':'não mudou'}
    if viewer is None:
        opportunity=f'Para revisar posicionamento do herói {hero} até {anchor}s, falta uma posição válida na âncora. Recupere esse dado antes de comparar apoio e tarefa independente.'
    elif support and support['change'] in directions:
        opportunity=f'Na janela anterior que termina em {anchor}s, a separação registrada entre o herói {hero} e o aliado {support["allyHeroId"]} {directions[support["change"]]}. Compare coordenação e tarefa independente sem presumir prontidão.'
    else:
        opportunity=f'Há posição registrada do herói {hero} até {anchor}s, mas não há comparação válida de separação nesta projeção. Revise o dado ausente antes de comparar apoio e tarefa independente.'
    events=facts['objectives']['events']
    if events:
        opportunity+=(' Há 1 evento de objetivo registrado na janela.' if len(events)==1 else
                      f' Há {len(events)} eventos de objetivo registrados na janela.')
    require(len(opportunity)<=400,'Canonical opportunity exceeds contract budget')
    limitations=[
        'Posições são oniscientes e não comprovam visão do jogador ou caminho seguro.',
        'Compras não comprovam entrega, inventário ou prontidão; upgrades não comprovam mana ou cooldown.',
        'Distância entre amostras não comprova intenção, trajeto contínuo ou intervenção disponível.',
        'Eventos de objetivo não comprovam pressão atual nem ameaça a estruturas.',
        'Os dados deste recorte não sustentam preferência entre alternativas; a comparação permanece condicional.']
    if viewer is None:
        limitations.append('A insuficiência se refere a esta pergunta de posicionamento; outros aspectos da partida podem ser revisáveis.')
    elif not support or support['change']=='unavailable':
        limitations.append('Sem comparação válida entre extremos, não afirme aumento ou redução da separação.')
    card={'opportunity':opportunity,'limitations':limitations,
          'dispositions':['insufficient'] if viewer is None else ['conditional-review','insufficient']}
    if policy==POLICY:
        recovery=_recovery(request,viewer,support)
        if recovery:
            card['recoveryTargets']=recovery['targets']
            card['recoveryExercise']=recovery['exercise']
    return card


def bounded_schema(request):
    schema=candidate_schema(request)
    card=fact_card(request)
    schema['properties']['opportunity']={'type':'string','enum':[card['opportunity']]}
    schema['properties']['limitations']={'type':'array','items':{'type':'string','enum':card['limitations']},
                                         'minItems':2,'maxItems':len(card['limitations']),'uniqueItems':True}
    schema['properties']['disposition']={'type':'string','enum':card['dispositions']}
    schema['properties']['preference']={'type':'string','enum':['undetermined']}
    if 'recoveryExercise' in card:
        exercise=card['recoveryExercise']
        props={'steps':{'type':'array','items':{'type':'string','enum':exercise['steps']},'minItems':1,'maxItems':1},
               'completionCriterion':{'type':'string','enum':[exercise['completionCriterion']]},
               'evidenceIds':{'type':'array','items':{'type':'string','enum':exercise['evidenceIds']},
                              'minItems':len(exercise['evidenceIds']),'maxItems':len(exercise['evidenceIds']),'uniqueItems':True}}
        schema['properties']['exercise']={'type':'object','properties':props,'required':list(props),'additionalProperties':False}
    return schema


def validate_bounded(request,candidate,policy=POLICY):
    card=fact_card(request,policy)
    require(candidate['opportunity']==card['opportunity'],'Noncanonical factual opportunity')
    limits=candidate['limitations']
    require(len(limits)>=2 and len(set(limits))==len(limits) and all(v in card['limitations'] for v in limits),
            'Noncanonical factual limitations')
    require(candidate['disposition'] in card['dispositions'] and candidate['preference']=='undetermined',
            'Bounded experiment cannot claim a supported tactical preference')
    if 'recoveryExercise' in card:
        actual,expected=candidate['exercise'],card['recoveryExercise']
        require(actual['steps']==expected['steps'] and actual['completionCriterion']==expected['completionCriterion']
                and set(actual['evidenceIds'])==set(expected['evidenceIds']),
                'Recovery exercise targets the wrong missing data')
