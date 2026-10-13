import { formatDuration, formatNumber } from '../matches/format';
import { lifetime, percent, plain } from './insights';

// Where each number of the smart stats lives: every statistic has ONE home, the tab of its subject, so no tab repeats
// another. A row: the player's / rival's / match average value (smartStats.player|counterpart|matchAverage) and, when
// the comparison base has it, the matching ComparedMetric (pro/high-rank/lane matchup).

const seconds = value => formatDuration(Math.round(value));

// A row with `stat` reads the API's catalog (analysis.stats, src/stats/) at the moment picked above the card
// (`moments`); `key` is then only the fallback for analyses read before the catalog, and the comparison base
// (`compared`) only exists at `comparedAt`. A row without `stat` is a number of the whole match.
const AT_TEN = 600;
const AT_TWENTY = 1200;

export const THEMES = {
    laning: {
        title: 'Números da fase de rotas',
        moments: [
            { key: 'at5', label: '5:00', at: 300 },
            { key: 'at10', label: '10:00', at: AT_TEN, isDefault: true },
            { key: 'at15', label: '15:00', at: 900 },
        ],
        rows: [
            { stat: 'lastHits', key: 'lastHitsAt10', label: 'Last hits', format: plain, compared: 'lastHitsAt10', comparedAt: AT_TEN },
            { stat: 'netWorth', key: 'netWorthAt10', label: 'Patrimônio', format: plain, compared: 'netWorthAt10', comparedAt: AT_TEN },
            { stat: 'heroDamage', key: 'laneDamageDealt', label: 'Dano causado em heróis', format: plain, compared: 'laneDamageDealt', comparedAt: AT_TEN },
            { stat: 'damageFromHeroes', key: 'laneDamageTaken', label: 'Dano recebido de heróis', format: plain, compared: 'laneDamageTaken', comparedAt: AT_TEN, lower: true },
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
        moments: [
            // From 20:00: up to 15:00 is the laning card's.
            { key: 'at20', label: '20:00', at: AT_TWENTY, isDefault: true },
            { key: 'at30', label: '30:00', at: 1800 },
            { key: 'end', label: 'Fim', atEnd: true },
            { key: 'gain10to20', label: 'De 10 a 20 min', from: AT_TEN, to: AT_TWENTY },
        ],
        rows: [
            { stat: 'netWorth', key: 'netWorthAt20', label: 'Patrimônio', format: plain, compared: 'netWorthAt20', comparedAt: AT_TWENTY },
            { stat: 'gold', label: 'Ouro ganho', format: plain },
            { stat: 'level', label: 'Nível', format: plain },
            { key: 'firstBigItemSeconds', label: 'Primeiro item grande (2.000+ de ouro)', format: seconds, compared: 'firstBigItemSeconds', lower: true },
            { key: 'netWorthShare', label: 'Parte do patrimônio do time (fim)', format: percent },
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
