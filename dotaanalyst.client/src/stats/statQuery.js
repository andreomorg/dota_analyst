import { formatDuration, formatNumber, formatSignedNumber } from '../matches/format';

// Questions about time asked to analysis.stats (the API's catalog: one value per point of the match, each minute
// and the end). The same rules as MatchStatTimeline in the Domain: the value at a moment is the last point at or
// before it (nothing made up between two minutes); a period is the value at its end minus the value at its start.

// Index of the point a moment falls on.
export function pointAt(stats, second) {
    let index = 0;
    stats.seconds.forEach((pointSecond, i) => {
        if (pointSecond <= second) {
            index = i;
        }
    });
    return index;
}

export function definitionOf(stats, key) {
    return stats.definitions.find(definition => definition.key === key) ?? null;
}

export function heroOf(stats, heroId) {
    return stats.heroes.find(hero => hero.heroId === heroId) ?? null;
}

// The hero's values of a statistic, one per point; null when the hero doesn't have it.
export function seriesOf(hero, key) {
    return hero?.series.find(series => series.stat === key)?.values ?? null;
}

export function valueAt(stats, heroId, key, second) {
    const values = seriesOf(heroOf(stats, heroId), key);
    return values ? values[pointAt(stats, second)] : null;
}

export function between(stats, heroId, key, from, to) {
    const end = valueAt(stats, heroId, key, to);
    const start = valueAt(stats, heroId, key, from);
    return end === null || start === null ? null : end - start;
}

// A moment ({ at }) or a period ({ from, to }) of the time selectors: what the statistic was then.
export function valueIn(stats, heroId, key, moment) {
    return moment.at !== undefined
        ? valueAt(stats, heroId, key, moment.at)
        : between(stats, heroId, key, moment.from, moment.to);
}

// The average of the heroes that have the statistic (the "match average" column).
export function averageIn(stats, key, moment) {
    const values = stats.heroes
        .map(hero => valueIn(stats, hero.heroId, key, moment))
        .filter(value => value !== null);
    return values.length === 0 ? null : values.reduce((sum, value) => sum + value, 0) / values.length;
}

// The end of the match, as a moment.
export function endOf(stats) {
    return stats.seconds[stats.seconds.length - 1];
}

// A number of the catalog as text: a gain in a period of a snapshot (net worth) gets its sign.
export function formatStat(value, definition, moment) {
    if (value === null || value === undefined) {
        return '—';
    }
    const rounded = Math.abs(value) < 10 && !Number.isInteger(value) ? Math.round(value * 10) / 10 : Math.round(value);
    return definition?.kind === 'state' && moment?.from !== undefined ? formatSignedNumber(rounded) : formatNumber(rounded);
}

// "aos 10:00", "de 10:00 a 20:00", "no fim".
export function momentLabel(moment, stats) {
    if (moment.at !== undefined) {
        return stats && moment.at >= endOf(stats) ? 'no fim' : `aos ${formatDuration(moment.at)}`;
    }
    return `de ${formatDuration(moment.from)} a ${formatDuration(moment.to)}`;
}
