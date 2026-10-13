// The Portuguese name of each statistic of the API's catalog (analysis.stats.definitions; StatCatalog.cs in the
// Domain). Only the name lives here: what each statistic is (unit, whether more is better...) comes from the catalog.
// One entry per key of the catalog: a .NET test (StatLabelsTests) fails when one is missing or left over.
export const STAT_LABELS = {
    gold: 'Ouro ganho',
    experience: 'Experiência',
    netWorth: 'Patrimônio',
    lastHits: 'Last hits',
    denies: 'Denies',
    level: 'Nível',
    kills: 'Abates',
    deaths: 'Mortes',
    assists: 'Assistências',
    heroDamage: 'Dano em heróis',
    damageFromHeroes: 'Dano recebido de heróis',
    damageTaken: 'Dano recebido (de tudo)',
    healing: 'Cura em aliados',
    observersPlaced: 'Observers colocados',
    sentriesPlaced: 'Sentries colocados',
    enemyWardsDestroyed: 'Wards inimigos destruídos',
};

export const statLabel = key => STAT_LABELS[key] ?? key;
