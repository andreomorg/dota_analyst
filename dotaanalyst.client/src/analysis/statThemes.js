import { formatDuration, formatNumber } from '../matches/format';
import { lifetime, percent, plain } from './insights';

// Where each number of the smart stats lives: every statistic has ONE home, the tab of its subject, so no tab repeats
// another. A row: the player's / rival's / match average value (smartStats.player|counterpart|matchAverage) and, when
// the comparison base has it, the matching ComparedMetric (pro/high-rank/lane matchup).

const seconds = value => formatDuration(Math.round(value));

export const THEMES = {
    laning: {
        title: 'Números da fase de rotas',
        rows: [
            { key: 'lastHitsAt10', label: 'Last hits aos 10:00', format: plain, compared: 'lastHitsAt10' },
            { key: 'netWorthAt10', label: 'Patrimônio aos 10:00', format: plain, compared: 'netWorthAt10' },
            { key: 'laneDamageDealt', label: 'Dano causado em heróis até 10:00', format: plain, compared: 'laneDamageDealt' },
            { key: 'laneDamageTaken', label: 'Dano recebido de heróis até 10:00', format: plain, compared: 'laneDamageTaken', lower: true },
        ],
    },
    deaths: {
        title: 'Mortes',
        rows: [
            { key: 'deaths', label: 'Mortes', format: plain, compared: 'deathsPer30', lower: true },
            { key: 'isolatedDeaths', label: 'Mortes sem aliado por perto', format: plain, compared: 'isolatedDeathsPer30', lower: true },
            { key: 'deathsByRegion.enemyJungle', label: 'Mortes na selva inimiga', format: plain, compared: 'enemyJungleDeathsPer30', lower: true },
            { key: 'deathsByRegion.river', label: 'Mortes no rio', format: plain, lower: true },
            { key: 'quickRedeaths', label: 'Mortes logo depois de voltar', format: plain, compared: 'quickRedeathsPer30', lower: true },
            { key: 'aloneOnEnemySideShare', label: 'Tempo sozinho no lado inimigo', format: percent, compared: 'aloneOnEnemySide', lower: true },
        ],
    },
    vision: {
        title: 'Visão',
        rows: [
            { key: 'observersPlaced', label: 'Observers colocados', format: plain, compared: 'observersPer30' },
            { key: 'sentriesPlaced', label: 'Sentries colocados', format: plain, compared: 'sentriesPer30' },
            { key: 'observerAverageLifetimeSeconds', label: 'Vida média dos observers', format: lifetime, compared: 'observerLifetime' },
            { key: 'teamObserverUptime', label: 'Tempo com observer do time', format: percent, compared: 'teamObserverUptime' },
            { key: 'enemyWardsDestroyed', label: 'Wards inimigos destruídos', format: plain, compared: 'enemyWardsDestroyedPer30' },
            { key: 'wardsLostToEnemy', label: 'Seus wards destruídos pelo inimigo', format: plain, lower: true },
        ],
    },
    fights: {
        title: 'Números das lutas',
        rows: [
            {
                key: 'fightsNearby',
                label: 'Lutas do time em que esteve perto',
                value: summary => (summary?.teamFights === null || summary?.teamFights === undefined
                    ? null
                    : `${plain(summary.fightsNearby)} de ${plain(summary.teamFights)}`),
                compared: 'fightPresence',
            },
            { key: 'killParticipation', label: 'Participação em abates', format: percent, compared: 'killParticipation' },
            { key: 'leadLostPerDeath', label: 'Vantagem do time perdida por morte', format: value => formatNumber(Math.round(value)), compared: 'leadLostPerDeath', lower: true },
        ],
    },
    economy: {
        title: 'Economia',
        rows: [
            { key: 'firstBigItemSeconds', label: 'Primeiro item grande (2.000+ de ouro)', format: seconds, compared: 'firstBigItemSeconds', lower: true },
            { key: 'netWorthAt20', label: 'Patrimônio aos 20:00', format: plain, compared: 'netWorthAt20' },
            { key: 'netWorthShare', label: 'Parte do patrimônio do time', format: percent },
        ],
    },
};

// How the comparison base writes its values (per 30 minutes for counts).
export const COMPARED_FORMAT = {
    deathsPer30: plain, isolatedDeathsPer30: plain, enemyJungleDeathsPer30: plain, quickRedeathsPer30: plain,
    observersPer30: plain, sentriesPer30: plain, enemyWardsDestroyedPer30: plain,
    aloneOnEnemySide: percent, fightPresence: percent, killParticipation: percent, teamObserverUptime: percent,
    observerLifetime: lifetime, firstBigItemSeconds: seconds,
    netWorthAt10: plain, netWorthAt20: plain, lastHitsAt10: plain, laneDamageDealt: plain, laneDamageTaken: plain,
    leadLostPerDeath: value => formatNumber(Math.round(value)),
};

export const PER_30 = new Set([
    'deathsPer30', 'isolatedDeathsPer30', 'enemyJungleDeathsPer30', 'quickRedeathsPer30', 'observersPer30',
    'sentriesPer30', 'enemyWardsDestroyedPer30',
]);

// The tab whose numbers back each tip (InsightKind), for the "ver os números" link of the tips.
export const INSIGHT_TABS = {
    behindAtTen: 'laning',
    lostLaneTrades: 'laning',
    isolatedDeaths: 'positioning',
    aloneOnEnemySide: 'positioning',
    deathsInEnemyJungle: 'positioning',
    quickRedeaths: 'positioning',
    fewObservers: 'positioning',
    shortObserverLife: 'positioning',
    lowObserverUptime: 'positioning',
    lowFightPresence: 'fights',
    gapGrewAfterLane: 'build',
};

export const TAB_NAMES = {
    laning: 'Fase de rotas',
    positioning: 'Posicionamento',
    fights: 'Lutas e objetivos',
    build: 'Build',
};
