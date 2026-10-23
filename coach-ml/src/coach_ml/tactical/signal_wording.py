"""Presentation shared with the historical policy; no numeric decision thresholds."""
CITED_FIELDS = (
    ('alliesWithinFightRange', 'visibleEnemiesWithinFightRange'),
    ('healthShare', 'visibleEnemiesWithinFightRange', 'nearestVisibleEnemyDistance'),
    ('damageFromHeroesLast6Seconds',),
    ('nearestAllyDistance', 'missingEnemies', 'missingEnemyLastSeenSecondsAgo'),
    ('missingEnemies',), ('ownBuildingsUnderAttack',), ('visibleEnemiesWithinFightRange',),
    ('teamNetWorthLead',), ('intentDestinationAdvance', 'missingEnemies', 'towerSeenShare'),
    ('healthShare', 'towerPathLength', 'towerNearestSeenEnemy', 'allyPathLength'),
    ('allyPingsNearLast10', 'nearestAllyDistance'))

def details_for(fmt):
    return [
        (
         f"com {fmt('alliesWithinFightRange')} aliados contra {fmt('visibleEnemiesWithinFightRange')} inimigos vistos em alcance de luta",
         'Lutar', 'Sair do alcance', 'há apoio e recursos para sustentar a aproximação',
         'existe caminho para sair do alcance sem deixar um aliado sozinho'),
        (
         f"com {fmt('healthShare', True)} de vida e {fmt('visibleEnemiesWithinFightRange')} inimigos vistos; distância ao mais próximo: {fmt('nearestVisibleEnemyDistance')}",
         'Recuar para recuperar vida', 'Permanecer no local', 'há recuperação e cobertura para permanecer',
         'o recuo mantém a rota ou o objetivo cobertos por outro aliado'),
        (
         f"com fuga pronta e {fmt('damageFromHeroesLast6Seconds')} de dano recebido de heróis nos últimos 6 s",
         'Usar a fuga agora', 'Guardar a fuga', 'a fuga disponível permite alcançar uma saída verificável',
         'o dano recebido ainda deixa margem para usar a fuga depois'),
        (
         f"no lado inimigo, aliado a {fmt('nearestAllyDistance')} unidades e {fmt('missingEnemies')} inimigos sumidos; última visão há {fmt('missingEnemyLastSeenSecondsAgo')} s",
         'Continuar o avanço', 'Aproximar-se do apoio', 'há apoio alcançável e informação para continuar',
         'o aliado mais próximo pode ser alcançado sem cruzar área sem visão'),
        (
         f"à frente da onda, sem visão de observadora própria e com {fmt('missingEnemies')} inimigos sumidos",
         'Manter o avanço sobre a onda', 'Retornar para junto da onda', 'há cobertura do time além da observadora própria',
         'voltar para junto da onda ainda permite pegar os creeps disponíveis'),
        (
         f"com {fmt('ownBuildingsUnderAttack')} construções próprias sob ataque e teleporte pronto",
         'Usar TP para defender', 'Seguir o plano atual', 'há destino de TP disponível e condições para defender',
         'o plano atual rende mais do que a construção sob ataque custa'),
        (
         f"com ultimate pronta e {fmt('visibleEnemiesWithinFightRange')} inimigos vistos em alcance de luta",
         'Usar a ultimate agora', 'Guardar a ultimate', 'alcance, recursos e apoio permitem usar a ultimate',
         'há uma ameaça maior para a qual guardar a ultimate'),
        (
         f"com Roshan vivo, vantagem de {fmt('teamNetWorthLead')} de patrimônio do time e sem Aegis próprio",
         'Preparar Roshan', 'Aplicar pressão em outro local', 'há recursos, cobertura e acesso para preparar Roshan',
         'outro objetivo está mais exposto do que o Roshan'),
        (
         f"indo para um destino {fmt('intentDestinationAdvance')} unidades adiante no lado inimigo, com {fmt('missingEnemies')} inimigos sumidos e {fmt('towerSeenShare', True)} da rota de volta vista pelo time",
         'Seguir para o destino', 'Esperar informação antes de avançar', 'o destino compensa voltar por uma rota pouco vista',
         'esperar alguns segundos traz visão dos inimigos sumidos'),
        (
         f"com {fmt('healthShare', True)} de vida e a rota até a torre ({fmt('towerPathLength')} unidades) passando a {fmt('towerNearestSeenEnemy')} de um inimigo visto",
         'Voltar pela rota da torre', 'Ir até o aliado mais próximo', 'a rota da torre pode ser percorrida antes do inimigo visto chegar',
         f"a rota até o aliado ({fmt('allyPathLength')} unidades) passa mais longe dos inimigos vistos"),
        (
         f"com {fmt('allyPingsNearLast10')} pings de aliados perto de você nos últimos 10 s e o aliado mais próximo a {fmt('nearestAllyDistance')} unidades",
         'Responder aos pings', 'Manter a tarefa atual', 'os pings pedem algo alcançável com os recursos prontos',
         'a tarefa atual ainda rende mais do que o pedido do aliado'),
    ]
