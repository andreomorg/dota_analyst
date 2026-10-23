"""Deterministic provisional targets for situated v5 requests."""
from ..core.json import digest
from ..core.validation import require
from .contracts import validate_candidate
from .situated import FIELDS, V51_FIELDS


def target_for(request):
    require(request.get('task') == 'tactical-review-v5' and request.get('schemaVersion') == 5,
            'Policy v2 requires situated v5')
    situation = next(f['value'] for f in request['facts'] if f['id'] == 'situation') or {}

    def get(field):
        return situation.get(FIELDS[field][0], {}).get(field)

    def num(field):
        value = get(field)
        return value if type(value) in (int, float) else None

    def fmt(field, percent=False):
        value = get(field)
        if value is None:
            return 'desconhecido'
        return f'{value * 100:.0f}%' if percent else f'{value:g}'

    e, a = num('visibleEnemiesWithinFightRange'), num('alliesWithinFightRange')
    hp, damage = num('healthShare'), num('damageFromHeroesLast6Seconds')
    distance, missing = num('nearestAllyDistance'), num('missingEnemies')
    wave, buildings, lead = num('aheadOfOwnWave'), num('ownBuildingsUnderAttack'), num('teamNetWorthLead')
    rules = [
        (e is not None and a is not None and e > a + 1,
         f"com {fmt('alliesWithinFightRange')} aliados contra {fmt('visibleEnemiesWithinFightRange')} inimigos vistos em alcance de luta",
         'Lutar', 'Sair do alcance', 'há apoio e recursos para sustentar a aproximação',
         'existe caminho para sair do alcance sem deixar um aliado sozinho'),
        (hp is not None and hp <= .30 and e is not None and e >= 1,
         f"com {fmt('healthShare', True)} de vida e {fmt('visibleEnemiesWithinFightRange')} inimigos vistos; distância ao mais próximo: {fmt('nearestVisibleEnemyDistance')}",
         'Recuar para recuperar vida', 'Permanecer no local', 'há recuperação e cobertura para permanecer',
         'o recuo mantém a rota ou o objetivo cobertos por outro aliado'),
        ((get('escapeItemReady') is True or get('myEscapeReady') is True) and damage is not None and damage > 0,
         f"com fuga pronta e {fmt('damageFromHeroesLast6Seconds')} de dano recebido de heróis nos últimos 6 s",
         'Usar a fuga agora', 'Guardar a fuga', 'a fuga disponível permite alcançar uma saída verificável',
         'o dano recebido ainda deixa margem para usar a fuga depois'),
        (get('onEnemySide') is True and distance is not None and distance > 1500 and missing is not None and missing >= 3,
         f"no lado inimigo, aliado a {fmt('nearestAllyDistance')} unidades e {fmt('missingEnemies')} inimigos sumidos; última visão há {fmt('missingEnemyLastSeenSecondsAgo')} s",
         'Continuar o avanço', 'Aproximar-se do apoio', 'há apoio alcançável e informação para continuar',
         'o aliado mais próximo pode ser alcançado sem cruzar área sem visão'),
        ((get('aheadOfOwnWave') is True or wave is not None and wave > 0) and get('inOwnObserverVision') is False and missing is not None and missing >= 2,
         f"à frente da onda, sem visão de observadora própria e com {fmt('missingEnemies')} inimigos sumidos",
         'Manter o avanço sobre a onda', 'Retornar para junto da onda', 'há cobertura do time além da observadora própria',
         'voltar para junto da onda ainda permite pegar os creeps disponíveis'),
        (buildings is not None and buildings >= 1 and get('teleportReady') is True,
         f"com {fmt('ownBuildingsUnderAttack')} construções próprias sob ataque e teleporte pronto",
         'Usar TP para defender', 'Seguir o plano atual', 'há destino de TP disponível e condições para defender',
         'o plano atual rende mais do que a construção sob ataque custa'),
        (get('ultimateReady') is True and e is not None and e >= 2,
         f"com ultimate pronta e {fmt('visibleEnemiesWithinFightRange')} inimigos vistos em alcance de luta",
         'Usar a ultimate agora', 'Guardar a ultimate', 'alcance, recursos e apoio permitem usar a ultimate',
         'há uma ameaça maior para a qual guardar a ultimate'),
        (get('roshanAlive') is True and lead is not None and lead >= 3000 and get('hasAegis') is False,
         f"com Roshan vivo, vantagem de {fmt('teamNetWorthLead')} de patrimônio do time e sem Aegis próprio",
         'Preparar Roshan', 'Aplicar pressão em outro local', 'há recursos, cobertura e acesso para preparar Roshan',
         'outro objetivo está mais exposto do que o Roshan'),
        # v5.1 rules: they read fields older contexts lack, so they never fire there (older targets reproduce).
        (get('intentDestinationOnEnemySide') is True and missing is not None and missing >= 3
         and num('towerSeenShare') is not None and num('towerSeenShare') < .5,
         f"indo para um destino {fmt('intentDestinationAdvance')} unidades adiante no lado inimigo, com {fmt('missingEnemies')} inimigos sumidos e {fmt('towerSeenShare', True)} da rota de volta vista pelo time",
         'Seguir para o destino', 'Esperar informação antes de avançar', 'o destino compensa voltar por uma rota pouco vista',
         'esperar alguns segundos traz visão dos inimigos sumidos'),
        (num('towerNearestSeenEnemy') is not None and num('towerNearestSeenEnemy') <= 1200 and hp is not None and hp <= .5,
         f"com {fmt('healthShare', True)} de vida e a rota até a torre ({fmt('towerPathLength')} unidades) passando a {fmt('towerNearestSeenEnemy')} de um inimigo visto",
         'Voltar pela rota da torre', 'Ir até o aliado mais próximo', 'a rota da torre pode ser percorrida antes do inimigo visto chegar',
         f"a rota até o aliado ({fmt('allyPathLength')} unidades) passa mais longe dos inimigos vistos"),
        (num('allyPingsNearLast10') is not None and num('allyPingsNearLast10') >= 2 and distance is not None and distance > 1500,
         f"com {fmt('allyPingsNearLast10')} pings de aliados perto de você nos últimos 10 s e o aliado mais próximo a {fmt('nearestAllyDistance')} unidades",
         'Responder aos pings', 'Manter a tarefa atual', 'os pings pedem algo alcançável com os recursos prontos',
         'a tarefa atual ainda rende mais do que o pedido do aliado'),
    ]
    rule_id, details = 'R0', None
    for index, (applies, *content) in enumerate(rules, 1):
        if applies:
            rule_id, details = f'R{index}', content
            break
    return render_target(request, rule_id, details, situation, get)


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
