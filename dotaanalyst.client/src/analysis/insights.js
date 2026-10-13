import { formatDuration, formatNumber, toPercent } from '../matches/format';

// The words of the API's findings (analysis.smartStats.insights): the rules and numbers come from the API (Domain),
// only the sentences are here.

export const ROLES = {
    1: 'carry (posição 1)',
    2: 'mid (posição 2)',
    3: 'offlane (posição 3)',
    4: 'suporte (posição 4)',
    5: 'suporte (posição 5)',
};

export const percent = value => `${toPercent(value)}%`;
export const lifetime = value => formatDuration(Math.round(value));
export const plain = value => formatNumber(Math.round(value * 10) / 10);

// One sentence per finding; counterpart = the enemy of the same role. Null for a kind added later on the back-end.
export function insightText(insight, stats) {
    const them = stats.counterpartHeroName;
    switch (insight.kind) {
        case 'isolatedDeaths':
            return `${insight.value} das suas ${stats.player.deaths} mortes foram sem nenhum aliado por perto (${them}: ${insight.counterpart}). Antes de avançar, ande com o time ou tenha visão da área.`;
        case 'aloneOnEnemySide':
            return `Você passou ${percent(insight.value)} do tempo sozinho no lado inimigo do mapa (${them}: ${percent(insight.counterpart)}) e morreu ${stats.player.deathsOnEnemySide} vezes lá. Avance com o time ou com visão.`;
        case 'behindAtTen':
            return `Aos 10:00 você tinha ${formatNumber(insight.value)} de patrimônio, ${formatNumber(insight.counterpart - insight.value)} a menos que ${them} (${formatNumber(insight.counterpart)}). Vale rever a fase de rotas: last hits, trocas e mortes.`;
        case 'fewObservers':
            return `Você colocou ${insight.value} observers; ${them} colocou ${insight.counterpart}. Como suporte, visão é parte do seu trabalho.`;
        case 'deathsInEnemyJungle':
            return `Você morreu ${insight.value} vezes na selva inimiga (${them}: ${insight.counterpart ?? 0} na sua). Entrar lá sem visão ou sem o time costuma custar caro.`;
        case 'shortObserverLife':
            return `Seus observers duraram em média ${lifetime(insight.value)} (o máximo é 6:00): o inimigo os encontrou cedo. Evite lugares óbvios e coloque sentries junto.`;
        case 'lowFightPresence':
            return `Você esteve perto de ${percent(insight.value)} das lutas do time (${them}: ${percent(insight.counterpart)}). Fique mais perto do time quando uma luta é provável.`;
        case 'quickRedeaths':
            return `Você morreu ${insight.value} vezes logo depois de voltar (até 1:30 depois de renascer). Antes de voltar para a luta, espere o time ou recupere o mapa.`;
        case 'lostLaneTrades':
            return `Na fase de rotas você recebeu ${formatNumber(insight.value)} de dano de heróis (${them}: ${formatNumber(insight.counterpart)}) e causou ${formatNumber(stats.player.laneDamageDealt)}. Você perdeu as trocas: cuidado com o posicionamento perto dos creeps inimigos.`;
        case 'lowObserverUptime':
            return `Seu time teve observer no mapa em ${percent(insight.value)} da partida; o inimigo, em ${percent(insight.counterpart)}. Como suporte, mantenha sempre um observer ativo.`;
        case 'gapGrewAfterLane':
            return `Aos 20:00 você tinha ${formatNumber(insight.value)} de patrimônio contra ${formatNumber(insight.counterpart)} de ${them}, e a diferença cresceu depois da fase de rotas. Reveja onde você farmou entre 10 e 20 minutos.`;
        default:
            return null;
    }
}

// A short title for each finding (the cards of the page).
export const INSIGHT_TITLES = {
    isolatedDeaths: 'Mortes sem aliado por perto',
    aloneOnEnemySide: 'Sozinho no lado inimigo',
    behindAtTen: 'Atrás aos 10:00',
    fewObservers: 'Pouca visão',
    deathsInEnemyJungle: 'Mortes na selva inimiga',
    shortObserverLife: 'Observers encontrados cedo',
    lowFightPresence: 'Longe das lutas',
    quickRedeaths: 'Morreu logo depois de voltar',
    lostLaneTrades: 'Perdeu as trocas na rota',
    lowObserverUptime: 'Mapa sem observer',
    gapGrewAfterLane: 'Diferença cresceu depois da rota',
};
