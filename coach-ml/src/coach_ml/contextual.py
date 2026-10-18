"""Contextual replay exercises from validated observations, without tactical verdicts."""


def contextual_exercise(request, response):
    skill = request['competencyId']
    event = request['evidence']['event']
    anchor = event['anchorSeconds']
    observation = response['observation']
    prefix = f'Na âncora {anchor}s, '
    if skill == 'economy-snapshot':
        if observation['status'] == 'unavailable':
            task = 'registre se existe uma amostra econômica anterior válida e seu instante; se não existir, marque a avaliação econômica como indisponível.'
        else:
            age = observation['sampleAgeSeconds']
            sample = anchor - age
            if age > 60:
                task = (f'compare o snapshot de {sample}s (idade {age}s) com o estado no replay. '
                        'Anote o que mudou no intervalo; não use o valor antigo como recurso atual.')
            elif request['role']['position'] in [4, 5]:
                task = (f'use o snapshot de {sample}s como referência temporal. Anote uma tarefa de suporte '
                        'observada e os recursos que ela exigiu; não transforme LH isolado em meta de farm.')
            else:
                task = (f'use o snapshot de {sample}s como referência temporal. Registre uma fonte de farm '
                        'e verifique no replay se era acessível; só proponha treino se confirmar uma oportunidade não convertida.')
    elif skill == 'support-change':
        coverage = observation['pairedCoverage']
        ally = observation['allyHeroId']
        if coverage == 'no-window':
            task = 'anote a cobertura disponível no início da partida; marque a comparação anterior como indisponível, sem procurar uma janela anterior ao jogo.'
        elif observation['change'] == 'unavailable':
            task = (f'procure registros do mesmo aliado em {anchor - 10}s e {anchor}s. '
                    'Anote qual extremo falta; sem os dois, não registre direção de mudança.')
        else:
            task = (f'compare o herói {request["heroId"]} e o aliado {ally} em {anchor - 10}s e {anchor}s. '
                    'Anote quem se moveu e um impedimento observado à interação; se não houver evidência, marque-o desconhecido.')
            if observation['change'] == 'recorded-separation-increased':
                task += ' Verifique quais opções mudaram com a separação registrada, sem atribuir culpa.'
            elif observation['change'] == 'recorded-separation-decreased':
                task += ' Verifique se a aproximação criou uma opção real, sem presumir que era melhor.'
            if coverage != 'all-paired-seconds':
                task += ' Confira as lacunas entre os extremos antes de descrever um trajeto.'
            elif observation['change'] == 'recorded-separation-unchanged':
                task += ' Extremos iguais não provam que ficaram parados.'
    elif skill == 'position-coverage':
        if not observation['viewerObserved']:
            task = 'localize primeiro o próprio herói no replay e registre seu instante; sem essa referência, suspenda a interpretação de apoio deste recorte.'
        elif observation['sampleSynchronization'] == 'mixed-recorded-seconds':
            task = 'alinhe jogador e aliados a um mesmo instante no replay. Registre quais amostras precisaram de alinhamento antes de discutir formação.'
        elif observation['observedAllies'] == 0:
            task = 'procure os aliados ausentes deste recorte e registre suas posições no replay; não converta falta de amostra em ausência real de apoio.'
        else:
            task = (f'confira os {observation["observedAllies"]} aliados observados num instante comum. '
                    'Registre uma condição de intervenção verificada ou marque-a desconhecida; contagem de amostras não mede distância.')
    else:
        status = observation['status']
        noun = {'purchase-records': 'compras', 'ability-records': 'upgrades', 'objective-records': 'objetivos'}[skill]
        if status == 'unavailable':
            task = f'procure o histórico de {noun} e registre se foi recuperado; mantenha o campo desconhecido se o replay não permitir verificá-lo.'
        elif status == 'none-recorded':
            task = f'confira o estado do herói e do mapa no replay, sem atribuir um evento de {noun} a esta janela; registre uma observação verificável ou a impossibilidade de verificá-la.'
        else:
            latest = observation['latestEvent']['seconds']
            task = f'localize o registro mais recente de {noun}, em {latest}s, entre {observation["count"]} eventos da janela. '
            task += {
                'purchase-records': 'Anote entrega e presença no inventário antes de considerar uso; se não forem verificáveis, marque disponibilidade desconhecida.',
                'ability-records': 'Anote se o registro é ativa, passiva ou talento; para uma ativa, confira mana e cooldown no replay antes de discutir possibilidade de uso.',
                'objective-records': 'Anote a ordem temporal entre objetivo e cena e uma tarefa concorrente observada; se não houver evidência, registre desconhecido, sem julgar a troca.'}[skill]
    exercise = prefix + task
    if len(exercise) > 700:
        raise ValueError('Contextual exercise exceeds response budget')
    return exercise
