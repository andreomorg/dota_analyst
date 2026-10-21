"""A separate teacher calibration recipe; original comparisons and targets remain immutable."""
import argparse
import copy
from pathlib import Path

from ..core.json import canonical, digest, parse_json
from ..core.validation import require
from .pipeline import load_cases, load_selection, ollama_generator, run_cases, SYSTEM
from .audit import validate_request
from .contracts import validate_candidate
from .facts import audit_candidate_facts
from .structured import candidate_schema

TEACHER_SYSTEM = SYSTEM + '''
Esta receita é uma segunda calibração para coaching de revisão, não um agente que decide a melhor jogada.
AllyHeroId é SEMPRE aliado, nunca inimigo. Distância até aliado não descreve vulnerabilidade ou proximidade de inimigo.
Revise o foco indicado e as relações canônicas. Se um objetivo inimigo foi tomado, não invente uma torre aliada ameaçada.
Prefira comparações de coordenação verificáveis a perguntas genéricas de atacar/defender sem contexto.
Não infira intenção mental de dois quadros. Pode pedir intenção relatada pelo jogador, mantendo desconhecida se não houver relato.
O exercício exige anotação concreta: instante, condição observada de cada alternativa e lacunas. "Decidir a melhor opção" não é critério suficiente.
Insufficient é relativo ao foco de revisão indicado; explique qual dado impede essa comparação sem afirmar que toda a partida é irrecuperável.
Os exemplos mostram como ligar dados e limites a uma pergunta útil. Não copie seus IDs, números ou eventos para outra entrada.
Toda transição de suporte termina na âncora e começa antes dela. Nunca descreva essa mudança como ocorrida após a âncora.
Se as limitações reconhecem que não há evidência para preferir uma opção, preference deve ser undetermined e disposition conditional-review.
'''

ACTION_INSTRUCTIONS = '''
Quando recorded-actions existir, o roster técnico é a fonte de nomes e times; não adivinhe nomes por ID.
itemUses e abilityUses são usos registrados do jogador, apenas na janela anterior de 30s.
null significa fonte indisponível; [] significa nenhum uso registrado nesse recorte, não incapacidade de usar.
Um uso não comprova alvo, acerto, efeito, custo, inventário atual, recurso restante ou cooldown atual.
Não invente essas propriedades por conhecimento de patch. role.position=null significa função retrospectiva ocultada.
Quando houver uso, cite a chave técnica e o instante exato observado, sem recomendar uma opção como superior.
Nomes técnicos são identificadores opacos, não descrições de efeito. Não deduza finalidade do nome.
A entrada não fornece mecânica de ativação: pode haver uso próprio, em unidade, em ponto ou sem alvo.
Nunca pressuponha inimigo, vulnerabilidade, proteção de aliado, interrupção, dano ou tipo de alvo para todo uso.
Primeiro peça identificação da modalidade e efeito observado no replay, se recuperáveis; não apresente essas lacunas como fatos.
Se a modalidade não é conhecida, marque insufficient para comparar decisões desse uso: alternatives=[], preference=undetermined.
Nesse caso proponha exercício específico de recuperação, com chave/instante e anotação da modalidade/efeito ou ausência de evidência.
Não copie o padrão "usar agora versus esperar" sem condições realmente distintas e sustentadas.
Todo campo que menciona um uso deve citar recorded-actions nos evidenceIds correspondentes.
Conecte o uso ao contexto somente quando houver evidência; proximidade temporal não demonstra causa.
Sem uso registrado, não invente ação para cumprir esse foco: escolha outro fato concreto ou insuficiência específica.
'''


def grounding_notes(request):
    facts = {f['id']:f['value'] for f in request['facts']}
    coverage = facts['positions']
    viewer = next((p for p in coverage['samples'] if p['heroId'] == request['heroId']),None)
    notes = {'viewerHeroId':request['heroId'],'viewerObserved':viewer is not None,
             'knownAllies':[], 'knownEnemies':[]}
    if viewer:
        notes['knownAllies'] = [p['heroId'] for p in coverage['samples'] if p['team']==viewer['team'] and p['heroId']!=request['heroId']]
        notes['knownEnemies'] = [p['heroId'] for p in coverage['samples'] if p['team']!=viewer['team']]
    actions = facts.get('recorded-actions')
    if actions:
        roster = actions['heroes']
        team = next(h['team'] for h in roster if h['heroId'] == request['heroId'])
        notes['knownAllies'] = [h['heroId'] for h in roster if h['team']==team and h['heroId']!=request['heroId']]
        notes['knownEnemies'] = [h['heroId'] for h in roster if h['team']!=team]
        notes['identityScope'] = 'roster técnico, não observação de posição ou visão'
        notes['recordedUseScope'] = 'uso passado, sem alvo/resultado/recursos atuais conhecidos'
    support = facts.get('support-transition')
    if support:
        notes['supportTarget'] = {'heroId':support['allyHeroId'],
                                  'relationship':'ally' if support['allyHeroId'] is not None else 'unavailable'}
        notes['supportTemporalScope'] = 'janela anterior que termina na âncora; nenhuma observação posterior'
    notes['recordedObjectives'] = []
    kinds = ('Tower','Barracks','Ancient','Roshan','Tormentor')
    for event in facts['objectives']['events'] or []:
        owner, taken = event['owner'], event['takenBy']
        notes['recordedObjectives'].append({'kind':kinds[event['kind']] if type(event['kind']) is int and 0 <= event['kind'] < len(kinds) else 'unknown',
            'seconds':event['seconds'],
            'ownerRelation':'unknown' if viewer is None or owner not in (0,1) else 'viewer-team' if owner == viewer['team'] else 'enemy',
            'takenByRelation':'unknown' if viewer is None or taken not in (0,1) else 'viewer-team' if taken == viewer['team'] else 'enemy'})
    notes['focus'] = ('recuperar localização antes de avaliar coordenação' if not viewer else
                       'coordenação após objetivo registrado, sem inventar ameaça a torre' if facts['objectives']['events'] else
                       'coordenação ao mesmo aliado e condições para agir conjuntamente' if support and support['change']!='unavailable' else
                       'verificar quais fontes permitem uma comparação, ou marcar insuficiência')
    if actions and (actions['itemUses'] or actions['abilityUses']):
        notes['focus'] = 'recuperar modalidade/efeito de um uso registrado antes de comparar decisões; não deduzir mecânica da chave'
    return notes


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('source','examples','reference-index','output'):
        parser.add_argument('--'+name,type=Path,required=True)
    selection = parser.add_mutually_exclusive_group(required=True)
    selection.add_argument('--index',type=Path)
    selection.add_argument('--bundle',type=Path)
    parser.add_argument('--include-support',action='store_true')
    parser.add_argument('--partition',choices=['development','validation-diagnostic'],default='development')
    parser.add_argument('--ollama-url',default='http://dotaanalyst-tactical-ollama:11434/')
    parser.add_argument('--model',default='qwen3:8b')
    parser.add_argument('--think',action='store_true')
    parser.add_argument('--max-tokens',type=int,default=1024)
    parser.add_argument('--bounded-facts',action='store_true')
    parser.add_argument('--actions',type=Path,help='Opt-in C# action receipt directory (development only)')
    parser.add_argument('--patches',type=Path,help='Opt-in audited development patch metadata directory')
    args = parser.parse_args(argv)
    require(128 <= args.max_tokens <= 4096, 'Teacher token budget must be between 128 and 4096')
    require(not (args.actions and args.bounded_facts), 'Position-only bounded narration is not an action-review policy')
    from .bounded import POLICY,INSTRUCTIONS,fact_card,bounded_schema,validate_bounded
    system=TEACHER_SYSTEM+INSTRUCTIONS if args.bounded_facts else TEACHER_SYSTEM
    if args.actions:
        system += ACTION_INSTRUCTIONS
    def envelope(request):
        notes=grounding_notes(request)
        if args.bounded_facts:
            notes['canonicalFacts']=fact_card(request)
        return {'input':request,'grounding':notes}
    references = {r['caseId']:q for r,q in load_cases(args.source,args.reference_index,True)}
    seeds = parse_json(args.examples.read_text())
    require(type(seeds) is list and 1 <= len(seeds) <= 2, 'One or two bounded calibration seeds required')
    messages = []
    for seed in seeds:
        require(seed['authorType']=='assistant-provisional' and seed['humanReviewed'] is False,
                'Calibration origin must be explicit; never declare human review')
        request = references.get(seed['sourceCaseId'])
        require(request is not None and canonical(request)==canonical(seed['request']) and digest(request)==seed['inputSha256'],
                'Calibration example differs from archived development source')
        validate_request(request)
        validate_candidate(request,seed['candidate'])
        require(not audit_candidate_facts(request,seed['candidate'])['flags'], 'Calibration example has unresolved factual flags')
        displayed=copy.deepcopy(seed['candidate'])
        if args.bounded_facts:
            card=fact_card(request)
            displayed['opportunity']=card['opportunity']
            displayed['limitations']=card['limitations'][:3]
            if 'recoveryExercise' in card:
                displayed['exercise']=card['recoveryExercise']
            validate_candidate(request,displayed)
            validate_bounded(request,displayed)
        messages.extend([{'role':'user','content':canonical(envelope(request))},
                         {'role':'assistant','content':canonical(displayed)}])
    cases = load_selection(args.source,args.index,args.bundle,args.include_support,args.partition,args.actions,args.patches)
    schema=bounded_schema if args.bounded_facts else candidate_schema
    if args.actions:
        from .actions import action_review_schema
        schema = action_review_schema
    identity, generate = ollama_generator(args.ollama_url,args.model,args.max_tokens,system=system,examples=messages,
                                          schema_factory=lambda value:schema(value['input']),context=12288,thinking=args.think)
    identity.update({'recipe':'tactical-grounding-v4','studentSystemSha256':digest(SYSTEM),
                     'dataPartition':args.partition,'humanReviewed':False,'teacherCalibrationNotBlindEvaluation':True})
    if args.actions:
        identity.update({'recipe':'tactical-grounding-actions-v2','actionReview':'recover-unverified-activation-mode-first'})
    if args.bounded_facts:
        identity.update({'boundedFactsPolicy':POLICY,'capability':'conditional-replay-review-only',
                         'factualNarration':'deterministic-schema-constraint',
                         'recoveryExercise':'deterministic-when-source-gap-known',
                         'seedPresentation':'canonical-factual-fields-and-recovery-exercise'})
    def generate_case(request):
        reply=generate(envelope(request))
        if args.bounded_facts:
            reply['boundedFactsPolicy']=POLICY
        return reply
    print(canonical(run_cases(cases,args.output,identity,generate_case,system=system)))


if __name__ == '__main__':
    main()
