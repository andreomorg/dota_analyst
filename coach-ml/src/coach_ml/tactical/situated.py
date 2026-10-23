"""Opt-in v5 projection of team-visible anchor snapshots."""
import copy

from ..core.validation import require
from .projection import project_scene

# Contract order: field -> (group, unit, short Portuguese meaning).
FIELDS = {
    'level': ('você', 'nível', 'nível do herói'),
    'netWorth': ('você', 'ouro', 'patrimônio total'),
    'gold': ('você', 'ouro', 'ouro disponível'),
    'healthShare': ('você', '0..1', 'vida, 0 a 1 do máximo'),
    'manaShare': ('você', '0..1', 'mana, 0 a 1 do máximo'),
    'stunned': ('você', 'sim/não', 'atordoado agora'),
    'silenced': ('você', 'sim/não', 'silenciado agora'),
    'activeDisables': ('você', 'contagem', 'controles ativos recebidos'),
    'activeSlows': ('você', 'contagem', 'lentidões ativas recebidas'),
    'teleportReady': ('prontidão', 'sim/não', 'teleporte pronto'),
    'hasEscapeItem': ('prontidão', 'sim/não', 'possui item de fuga'),
    'escapeItemReady': ('prontidão', 'sim/não', 'item de fuga pronto'),
    'ultimateReady': ('prontidão', 'sim/não', 'ultimate pronta'),
    'myDisableReady': ('prontidão', 'sim/não', 'habilidade de controle pronta'),
    'myEscapeReady': ('prontidão', 'sim/não', 'habilidade de fuga pronta'),
    'buybackReady': ('prontidão', 'sim/não', 'recompra pronta'),
    'enemiesAlive': ('inimigos vistos', 'contagem', 'inimigos vivos'),
    'visibleEnemies': ('inimigos vistos', 'contagem', 'inimigos vistos pelo time'),
    'visibleEnemiesWithinFightRange': ('inimigos vistos', 'contagem', 'inimigos vistos em alcance de luta'),
    'nearestVisibleEnemyDistance': ('inimigos vistos', 'unidades do mapa', 'distância ao inimigo visto mais próximo, unidades do mapa'),
    'missingEnemies': ('inimigos vistos', 'contagem', 'inimigos fora da visão do time'),
    'missingEnemyLastSeenSecondsAgo': ('inimigos vistos', 's', 'tempo desde a última visão de inimigo ausente'),
    'nearestVisibleEnemyKillsOnMe': ('inimigos vistos', 'contagem', 'abates sobre você do inimigo visto mais próximo'),
    'alliesAlive': ('aliados', 'contagem', 'aliados vivos'),
    'nearestAllyDistance': ('aliados', 'unidades do mapa', 'distância ao aliado mais próximo'),
    'alliesWithinFightRange': ('aliados', 'contagem', 'aliados em alcance de luta'),
    'lowestAllyHealthShare': ('aliados', '0..1', 'menor fração de vida entre aliados'),
    'alliesWithTeleportReady': ('aliados', 'contagem', 'aliados com teleporte pronto'),
    'region': ('lugar', 'região', 'nome da região'),
    'onEnemySide': ('lugar', 'sim/não', 'no lado inimigo do mapa'),
    'onEnemyHighGround': ('lugar', 'sim/não', 'no terreno elevado inimigo'),
    'terrainHeightAdvantage': ('lugar', 'níveis', 'vantagem de altura do terreno'),
    'terrainOpenness': ('lugar', '0..1', 'abertura do terreno'),
    'inOwnObserverVision': ('lugar', 'sim/não', 'sob visão de observadora própria'),
    'teamLocalVisionCoverage': ('lugar', '0..1', 'cobertura local da visão do time'),
    'nearestOwnTowerDistance': ('lugar', 'unidades do mapa', 'distância à torre própria mais próxima'),
    'nearestEnemyTowerDistance': ('lugar', 'unidades do mapa', 'distância à torre inimiga mais próxima'),
    'aheadOfOwnWave': ('rota', 'unidades do mapa', 'avanço sobre a própria onda; negativo = atrás'),
    'waveClashPoint': ('rota', 'unidades do mapa', 'ponto de encontro das ondas'),
    'waveClashPastOwnTower': ('rota', 'unidades do mapa', 'avanço do encontro das ondas além da própria torre'),
    'ownBuildingsUnderAttack': ('rota', 'contagem', 'construções próprias sob ataque'),
    'teamNetWorthLead': ('momento', 'ouro', 'vantagem de patrimônio do time'),
    'teamExperienceLead': ('momento', 'XP', 'vantagem de experiência do time'),
    'isNight': ('momento', 'sim/não', 'é noite agora'),
    'secondsSinceRoshanKill': ('momento', 's', 'tempo desde o abate de Roshan'),
    'roshanAlive': ('momento', 'sim/não', 'Roshan vivo'),
    'hasAegis': ('momento', 'sim/não', 'você possui Aegis'),
    'enemyHasAegis': ('momento', 'sim/não', 'inimigo possui Aegis'),
    'recentDeaths': ('momento', 'contagem', 'mortes recentes'),
    'enemyKillsLastMinute': ('momento', 'contagem', 'abates inimigos no último minuto'),
    'damageFromHeroesLast6Seconds': ('agora', 'dano', 'dano recebido de heróis nos últimos 6 segundos'),
    'enemiesHittingMe': ('agora', 'contagem', 'inimigos atacando você'),
    'enemySpellsOnMeLast10': ('agora', 'contagem', 'magias inimigas sobre você nos últimos 10 segundos'),
    'underSmoke': ('agora', 'sim/não', 'sob efeito de fumaça'),
    # v5.1: the player's own orders in the last 10 s and the team's pings (the observable intent and communication).
    'intentMoves': ('intenção', 'contagem', 'ordens de movimento nos últimos 10 s'),
    'intentAttacks': ('intenção', 'contagem', 'ordens de ataque nos últimos 10 s'),
    'intentCasts': ('intenção', 'contagem', 'ordens de magia/item nos últimos 10 s'),
    'intentDestinationDistance': ('intenção', 'unidades do mapa', 'distância até o último destino clicado'),
    'intentDestinationAdvance': ('intenção', 'unidades do mapa', 'quanto o destino avança rumo à base inimiga'),
    'intentDestinationOnEnemySide': ('intenção', 'sim/não', 'destino no lado inimigo'),
    'allyPingsNearLast10': ('comunicação', 'contagem', 'pings de aliados perto de você nos últimos 10 s'),
    'ownPingsLast10': ('comunicação', 'contagem', 'seus pings nos últimos 10 s'),
    # v5.1: ground routes (A* on the patch map, standing trees) to the nearest own tower/fountain and nearest ally.
    'towerPathLength': ('rotas', 'unidades do mapa', 'comprimento da rota até a torre própria mais próxima'),
    'towerStraightDistance': ('rotas', 'unidades do mapa', 'distância em linha reta até essa torre'),
    'towerDetour': ('rotas', 'razão', 'rota dividida pela linha reta'),
    'towerSeenShare': ('rotas', '0..1', 'parte da rota vista pelo time'),
    'towerNearestSeenEnemy': ('rotas', 'unidades do mapa', 'inimigo visto mais próximo da rota'),
    'allyPathLength': ('rotas', 'unidades do mapa', 'comprimento da rota até o aliado mais próximo'),
    'allyStraightDistance': ('rotas', 'unidades do mapa', 'distância em linha reta até esse aliado'),
    'allyDetour': ('rotas', 'razão', 'rota dividida pela linha reta'),
    'allySeenShare': ('rotas', '0..1', 'parte da rota vista pelo time'),
    'allyNearestSeenEnemy': ('rotas', 'unidades do mapa', 'inimigo visto mais próximo da rota'),
}
# The escape snapshot's routes and their fields, flattened into the 'rotas' group (and rebuilt by the audit).
ROUTE_FIELDS = {'toTower': 'tower', 'toAlly': 'ally'}
ROUTE_VALUES = {'pathLength': 'PathLength', 'straightDistance': 'StraightDistance', 'detour': 'Detour',
                'seenShare': 'SeenShare', 'nearestSeenEnemyToPath': 'NearestSeenEnemy'}
V51_FIELDS = frozenset(('intentMoves', 'intentAttacks', 'intentCasts', 'intentDestinationDistance',
                        'intentDestinationAdvance', 'intentDestinationOnEnemySide', 'allyPingsNearLast10',
                        'ownPingsLast10', *(prefix + suffix for prefix in ('tower', 'ally')
                                            for suffix in ('PathLength', 'StraightDistance', 'Detour', 'SeenShare',
                                                           'NearestSeenEnemy'))))
# Facts whose presence removes a default unknown: a group with any non-null value answers it.
UNKNOWN_SOURCES = {'visibility': 'inimigos vistos', 'health-mana': 'você', 'cooldowns': 'prontidão',
                   'inventory-readiness': 'prontidão', 'creep-waves': 'rota', 'intent': 'intenção',
                   'safe-path': 'rotas'}
HERO_KEYS = ('heroId', 'name', 'team', 'roles', 'kitEffects')


def flatten_escape(escape):
    """{'toTower': {...} | None, 'toAlly': ...} -> towerPathLength... (a null route = every field null)."""
    return {prefix + suffix: (route or {}).get(key)
            for name, prefix in ROUTE_FIELDS.items() for route in [escape.get(name)]
            for key, suffix in ROUTE_VALUES.items()}


def unflatten_escape(values):
    """The inverse, for the audit: a route exists exactly when its path length does."""
    return {name: None if values.get(prefix + 'PathLength') is None
            else {key: values.get(prefix + suffix) for key, suffix in ROUTE_VALUES.items()}
            for name, prefix in ROUTE_FIELDS.items()}


def _reject_hidden(value):
    if isinstance(value, dict):
        for key, child in value.items():
            require('hidden' not in key.lower(), 'Hidden field in v5 context')
            _reject_hidden(child)
    elif isinstance(value, list):
        for child in value:
            _reject_hidden(child)


def _keys(value, allowed):
    require(type(value) is dict and not (value.keys() - set(allowed)), 'Unknown v5 field')


def project_situated_scene(context, index):
    """Reuse v1 facts verbatim and append the separately versioned v5 facts."""
    _reject_hidden(context)
    _keys(context, ('schemaVersion', 'decision', 'heroes', 'situations'))
    require(context['schemaVersion'] == 5, 'Expected schemaVersion 5')
    decision = context['decision']
    require(type(index) is int and 0 <= index < len(decision['scenes']), 'Invalid scene index')
    require(len(context['situations']) == len(decision['scenes']), 'Situation count mismatch')
    for snapshot in context['situations']:
        _keys(snapshot, ('anchorSeconds', 'situation', 'escape'))
        escape = snapshot.get('escape')
        if escape is not None:
            _keys(escape, ROUTE_FIELDS)
            for route in escape.values():
                require(route is None or (type(route) is dict and set(route) == set(ROUTE_VALUES)
                                          and type(route['pathLength']) is int), 'Invalid escape route')
        if snapshot['situation'] is not None:
            _keys(snapshot['situation'], FIELDS)
            require(all(value is None or type(value) in (bool, int, float, str)
                        for value in snapshot['situation'].values()), 'Invalid situation value')
    scene = decision['scenes'][index]
    snapshot = context['situations'][index]
    anchor = scene['atOrBeforeAnchor']['anchorSeconds']
    require(type(snapshot['anchorSeconds']) is int and snapshot['anchorSeconds'] == anchor,
            'Situation anchor mismatch')
    request = project_scene(decision, scene)
    situation = snapshot['situation']
    if situation is not None and snapshot.get('escape') is not None:
        situation = {**situation, **flatten_escape(snapshot['escape'])}
    groups = None
    if situation is not None:
        # Values only: units and meanings cost tokens on every example; the names and the prompt's legend carry them.
        groups = {}
        for field, (group, _, _) in FIELDS.items():
            # v5.1 fields only when the context has them: older contexts project exactly as before.
            if field in V51_FIELDS and field not in situation:
                continue
            groups.setdefault(group, {})[field] = situation.get(field)
        # A group is available even when its recorded value is false or zero.
        available = {group for field, (group, _, _) in FIELDS.items()
                     if situation.get(field) is not None}
        request['unknowns'] = [item for item in request['unknowns']
                               if UNKNOWN_SOURCES.get(item) not in available]

    heroes = context['heroes']
    require(type(heroes) is list and len(heroes) in (0, 10), 'Expected ten heroes or unavailable catalog')
    ids = set()
    for hero in heroes:
        _keys(hero, HERO_KEYS)
        kit = hero.get('kitEffects')
        require(kit is None or type(kit) is list and all(type(effect) is str for effect in kit), 'Invalid kit effects')
        require(type(hero['heroId']) is int and hero['heroId'] > 0 and hero['heroId'] not in ids,
                'Duplicate or invalid roster hero')
        ids.add(hero['heroId'])
        require(hero['team'] in ('radiant', 'dire'), 'Invalid roster team')
        require(type(hero['name']) is str and type(hero['roles']) is list
                and all(type(role) is str for role in hero['roles']), 'Invalid catalog entry')
    viewer = next((hero for hero in heroes if hero['heroId'] == decision['heroId']), None)
    require(not heroes or viewer is not None, 'Player missing from roster')
    # kitEffects only when the context carries it: contexts from before v5.1 project exactly as they did.
    roster = [{'heroId': hero['heroId'], 'name': hero['name'],
               'team': 'aliado' if hero['team'] == viewer['team'] else 'inimigo',
               'roles': copy.deepcopy(hero['roles']),
               **({'kitEffects': copy.deepcopy(hero['kitEffects'])} if 'kitEffects' in hero else {})}
              for hero in heroes]
    request['facts'].extend([
        {'id': 'situation', 'kind': 'anchor-situation', 'sampleSeconds': anchor,
         'value': groups, 'source': 'situated-decision-document:situation',
         'perspective': 'team-visible', 'limitations': ['Nulo significa desconhecido.']},
        {'id': 'roster', 'kind': 'hero-catalog', 'sampleSeconds': anchor,
         'value': roster, 'source': 'situated-decision-document:heroes',
         'perspective': 'recorded', 'limitations': ['Papéis do catálogo não indicam intenção.']},
    ])
    request['schemaVersion'] = 5
    request['task'] = 'tactical-review-v5'
    return request
