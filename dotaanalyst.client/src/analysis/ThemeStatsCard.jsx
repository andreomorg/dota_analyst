import { useState } from 'react';
import { statLabel } from '../stats/statLabels';
import { averageIn, definitionOf, endOf, formatStat, momentLabel, valueIn } from '../stats/statQuery';
import Standing from '../ui/Standing';
import { COMPARED_FORMAT, PER_30, THEMES } from './statThemes';

// The numbers of one subject (statThemes.js), shown once, in the tab of that subject: for each statistic, the player,
// the enemy of the same role, the match average and — when the comparison base has it — how the player did against
// other players of the hero (high rank, pros or the same lane matchup, picked above the table). Subjects with
// `moments` read the API's catalog (analysis.stats) at the moment picked above the table.

function valueOf(summary, key) {
    return key.split('.').reduce((value, part) => value?.[part], summary);
}

function summaryCell(summary, row) {
    if (row.value) {
        return row.value(summary) ?? '—';
    }
    const value = valueOf(summary, row.key);
    return value === null || value === undefined ? '—' : row.format(value);
}

const SOURCES = { highRank: 'Alto nível', pro: 'Profissionais' };

// The base groups that compare at least one of these rows: keys are the saved metrics (deathsPer30...), stats the
// catalog's statistics compared at the checkpoints (timed: { stat, second, value, median, betterThan }).
function groupsOf(smartStats, keys, stats) {
    const groups = [];
    const add = (baselines, prefix, label) => (baselines ?? []).forEach(baseline => {
        const metrics = baseline.metrics.filter(metric => keys.has(metric.metric));
        const timed = (baseline.timed ?? []).filter(comparison => stats.has(comparison.stat));
        if (metrics.length > 0 || timed.length > 0) {
            groups.push({ key: `${prefix}${baseline.source}`, label: label(baseline), matches: baseline.matches, metrics, timed });
        }
    });
    add(smartStats.baselines, '', baseline => SOURCES[baseline.source] ?? baseline.source);
    if (smartStats.laneMatchupHeroName) {
        add(smartStats.laneMatchupBaselines, 'matchup-',
            baseline => `Contra ${smartStats.laneMatchupHeroName} · ${SOURCES[baseline.source] ?? baseline.source}`);
    }
    return groups;
}

// The moments of the subject that fit the match ("30:00" in a 25-minute match is the end, already listed); `atEnd`
// becomes the end of the match.
function momentsOf(section, stats) {
    if (!section.moments || !stats) {
        return [];
    }
    const end = endOf(stats);
    return section.moments
        .map(moment => (moment.atEnd ? { ...moment, at: end } : moment))
        .filter(moment => (moment.at !== undefined ? moment.atEnd || moment.at < end : moment.to <= end));
}

// themes: keys of THEMES shown in this card (one group header each when more than one); smartStats and stats from
// the API (smartStats is null without role queue: then nothing shows).
function ThemeStatsCard({ themes, smartStats, stats, heroName, title }) {
    const [selectedKey, setSelectedKey] = useState(null);
    const [momentKey, setMomentKey] = useState(null);
    if (!smartStats) {
        return null;
    }

    const sections = themes.map(key => THEMES[key]);
    const moments = sections.length === 1 ? momentsOf(sections[0], stats) : [];
    const moment = moments.find(candidate => candidate.key === momentKey)
        ?? moments.find(candidate => candidate.isDefault)
        ?? moments[0]
        ?? null;
    const comparedKeys = new Set(sections.flatMap(section => section.rows.map(row => row.compared).filter(Boolean)));
    const timedStats = new Set(moments.length > 0 ? sections.flatMap(section => section.rows.map(row => row.stat).filter(Boolean)) : []);
    const groups = groupsOf(smartStats, comparedKeys, timedStats);
    const group = groups.find(candidate => candidate.key === selectedKey)
        ?? groups.find(candidate => candidate.key === 'highRank')
        ?? groups[0];
    const who = `jogadores de ${heroName}${group?.key.startsWith('matchup-') ? ` contra ${smartStats.laneMatchupHeroName}` : ''}`;
    const { player, counterpart, matchAverage, counterpartHeroName, counterpartHeroId } = smartStats;
    const playerHeroId = stats?.heroes.find(hero => hero.isCurrentPlayer)?.heroId;

    // A catalog row at the picked moment; the saved number of the analysis when the catalog doesn't have it (older
    // analyses) and the moment is the one that number was taken at.
    const statCell = (row, heroId, summary) => {
        const value = heroId === null
            ? averageIn(stats, row.stat, moment)
            : heroId === undefined ? null : valueIn(stats, heroId, row.stat, moment);
        if (value !== null) {
            return formatStat(value, definitionOf(stats, row.stat), moment);
        }
        return row.key && moment.at === row.comparedAt ? summaryCell(summary, row) : '—';
    };
    const cell = (row, heroId, summary) => (row.stat && moment ? statCell(row, heroId, summary) : summaryCell(summary, row));

    return (
        <div className="card theme-stats">
            <div className="card-head">
                <h3>{title ?? sections[0].title}</h3>
                {groups.length > 1 && (
                    <div className="segmented" role="group" aria-label="Comparar com">
                        {groups.map(candidate => (
                            <button
                                key={candidate.key}
                                type="button"
                                aria-pressed={candidate.key === group.key}
                                onClick={() => setSelectedKey(candidate.key)}
                            >
                                {candidate.label}
                                <span className="segmented-hint">{candidate.matches}</span>
                            </button>
                        ))}
                    </div>
                )}
            </div>
            {moments.length > 1 && (
                <div className="moment-picker">
                    <span className="moment-picker-label">Momento da partida</span>
                    <div className="segmented" role="group" aria-label="Momento da partida">
                        {moments.map(candidate => (
                            <button
                                key={candidate.key}
                                type="button"
                                aria-pressed={candidate.key === moment.key}
                                onClick={() => setMomentKey(candidate.key)}
                            >
                                {candidate.label}
                            </button>
                        ))}
                    </div>
                </div>
            )}
            <div className="table-scroll">
                <table className="data-table baseline-table">
                    <caption className="visually-hidden">
                        Seus números{moment ? ` ${momentLabel(moment, stats)}` : ''}, os de {counterpartHeroName}, a média da partida e a comparação com {who}
                    </caption>
                    <thead>
                        <tr>
                            <th scope="col">Estatística</th>
                            <th scope="col" className="num-col">Você</th>
                            <th scope="col" className="num-col">{counterpartHeroName}<span className="th-sub">mesma posição</span></th>
                            <th scope="col" className="num-col">Média da partida</th>
                            {group && (
                                <th scope="col">
                                    {group.label}
                                    <span className="th-sub">{group.matches} partidas de {heroName}</span>
                                </th>
                            )}
                        </tr>
                    </thead>
                    {sections.map(section => (
                        <tbody key={section.title}>
                            {sections.length > 1 && (
                                <tr className="group-row">
                                    <th scope="rowgroup" colSpan={group ? 5 : 4}>{section.title}</th>
                                </tr>
                            )}
                            {section.rows.map(row => {
                                const definition = row.stat && stats ? definitionOf(stats, row.stat) : null;
                                const timed = Boolean(row.stat && moment);
                                const lower = row.lower ?? definition?.better === 'lower';
                                // A catalog row compares at the picked moment when the base has it (checkpoints only: not
                                // the end, not a period); the others with the saved metric, at the moment it was taken.
                                const timedCompared = timed && !moment.atEnd && moment.at !== undefined
                                    ? group?.timed.find(comparison => comparison.stat === row.stat && comparison.second === moment.at)
                                    : null;
                                const comparable = !timed || moment.at === row.comparedAt;
                                const compared = timedCompared
                                    ?? (comparable ? group?.metrics.find(metric => metric.metric === row.compared) : null);
                                const format = timedCompared
                                    ? value => formatStat(value, definition)
                                    : COMPARED_FORMAT[row.compared];
                                return (
                                    <tr key={row.stat ?? row.key}>
                                        <th scope="row">
                                            {row.stat ? statLabel(row.stat) : row.label}
                                            {timed && definition?.kind === 'state' && moment.from !== undefined && (
                                                <span className="indicator-hint">ganho no período</span>
                                            )}
                                            {lower && <span className="indicator-hint">menos é melhor</span>}
                                        </th>
                                        <td className="num-col mono">{cell(row, playerHeroId, player)}</td>
                                        <td className="num-col mono">{cell(row, counterpartHeroId, counterpart)}</td>
                                        <td className="num-col mono">{cell(row, null, matchAverage)}</td>
                                        {group && (
                                            <td>
                                                {compared
                                                    ? (
                                                        <span className="baseline-cell">
                                                            <span className="baseline-typical">
                                                                Típico <span className="mono">{format(compared.median)}</span>
                                                                {!timedCompared && PER_30.has(row.compared) && (
                                                                    <> · você <span className="mono">{format(compared.value)}</span> por 30 min</>
                                                                )}
                                                            </span>
                                                            <Standing share={compared.betterThan} who={who} />
                                                        </span>
                                                    )
                                                    : (
                                                        <span className="indicator-hint">
                                                            {timed && (moment.atEnd || moment.at === undefined)
                                                                ? 'sem comparação no fim nem em períodos'
                                                                : 'sem comparação'}
                                                        </span>
                                                    )}
                                            </td>
                                        )}
                                    </tr>
                                );
                            })}
                        </tbody>
                    ))}
                </table>
            </div>
            {!group && (
                <p className="section-note">
                    A comparação com outros jogadores de {heroName} aparece quando a base tiver 10 partidas dele nesta posição.
                </p>
            )}
            {moments.length > 1 && (
                <p className="section-note">
                    Escolha o momento acima. A comparação com outros jogadores usa as partidas da base que chegaram até esse
                    momento (aos 5, 10, 15, 20, 25, 30 e 40 min).
                </p>
            )}
        </div>
    );
}

export default ThemeStatsCard;
