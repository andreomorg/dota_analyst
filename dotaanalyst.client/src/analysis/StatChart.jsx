import { useState } from 'react';
import { formatDuration } from '../matches/format';
import { statLabel } from '../stats/statLabels';
import { definitionOf, formatStat, heroOf, seriesOf } from '../stats/statQuery';
import SectionHead from '../ui/SectionHead';
import { HEIGHT, MARGIN, nearestPoint, scales, shortNumber, spreadLabels, useWidth } from './chartKit';
import { TimeAxis, Tooltip } from './ChartParts';

// Any statistic of the API's catalog (analysis.stats) minute by minute, one line per hero: the statistic is picked
// above the chart and, with `pickHeroes`, the heroes too (up to five, one color each).

const MAX_HEROES = 5;

function Chart({ stats, lines, statKey, lastSecond }) {
    const [ref, width] = useWidth();
    const [point, setPoint] = useState(null);
    const count = stats.seconds.filter(second => second <= lastSecond).length;
    const seconds = stats.seconds.slice(0, count);
    const definition = definitionOf(stats, statKey);
    const series = lines
        .map(line => ({ ...line, values: seriesOf(heroOf(stats, line.heroId), statKey)?.slice(0, count) }))
        .filter(line => line.values);
    if (series.length === 0 || seconds.length < 2) {
        return <p className="empty-state">Esta partida não tem {statLabel(statKey).toLowerCase()} minuto a minuto.</p>;
    }

    const yMax = Math.max(1, ...series.flatMap(line => line.values));
    const { x, y } = scales(seconds, width, 0, yMax);
    const last = seconds.length - 1;
    const labels = spreadLabels(series.map(line => ({ key: line.heroId, name: line.name, y: y(line.values[last]) + 4 })));
    const format = value => formatStat(value, definition);

    return (
        <figure className="timeline-chart" ref={ref}>
            <ul className="chart-legend">
                {series.map(line => (
                    <li key={line.heroId}>
                        <span className={`legend-swatch series-${line.slot}`} aria-hidden="true" />
                        {line.name} {line.side && <span className="legend-side">({line.side})</span>}
                    </li>
                ))}
            </ul>
            <svg
                width={width}
                height={HEIGHT}
                role="img"
                aria-label={`${statLabel(statKey)} de ${series.map(line => line.name).join(', ')} ao longo da partida.`}
                onPointerMove={event => setPoint(nearestPoint(event, event.currentTarget, seconds, x))}
                onPointerLeave={() => setPoint(null)}
            >
                <g className="chart-axis" aria-hidden="true">
                    {[yMax, yMax / 2, 0].map(value => (
                        <g key={value}>
                            <line className={value === 0 ? 'chart-zero' : 'chart-grid'} x1={MARGIN.left} x2={width - MARGIN.right} y1={y(value)} y2={y(value)} />
                            <text x={MARGIN.left - 8} y={y(value) + 4} textAnchor="end">{shortNumber(value).replace('+', '')}</text>
                        </g>
                    ))}
                </g>
                <TimeAxis seconds={seconds} x={x} />
                {series.map(line => (
                    <path
                        key={line.heroId}
                        className={`lane-line series-${line.slot}${line.isPlayer ? ' is-player' : ''}`}
                        d={line.values.map((value, i) => `${i ? 'L' : 'M'}${x(seconds[i])},${y(value)}`).join('')}
                    />
                ))}
                {labels.map(label => (
                    <text key={label.key} className="chart-end-label" x={x(seconds[last]) + 8} y={label.y}>{label.name}</text>
                ))}
                {point !== null && (
                    <g aria-hidden="true">
                        <line className="chart-crosshair" x1={x(seconds[point])} x2={x(seconds[point])} y1={MARGIN.top} y2={HEIGHT - MARGIN.bottom} />
                        {series.map(line => (
                            <circle key={line.heroId} className={`chart-dot series-${line.slot}`} cx={x(seconds[point])} cy={y(line.values[point])} r="5" />
                        ))}
                    </g>
                )}
            </svg>
            <Tooltip point={point === null ? null : seconds[point]} x={x} width={width}>
                {point !== null && (
                    <>
                        <strong>{formatDuration(seconds[point])}</strong>
                        {series.map(line => (
                            <span key={line.heroId}>
                                <span className={`legend-swatch series-${line.slot}`} aria-hidden="true" /> {line.name}: {format(line.values[point])}
                            </span>
                        ))}
                    </>
                )}
            </Tooltip>
            <details className="timeline-table">
                <summary>Ver os valores minuto a minuto</summary>
                <div className="table-scroll">
                    <table className="data-table">
                        <caption className="visually-hidden">{statLabel(statKey)} minuto a minuto</caption>
                        <thead>
                            <tr>
                                <th scope="col">Tempo</th>
                                {series.map(line => <th key={line.heroId} scope="col" className="num-col">{line.name}</th>)}
                            </tr>
                        </thead>
                        <tbody>
                            {seconds.map((second, i) => (
                                <tr key={second}>
                                    <th scope="row">{formatDuration(second)}</th>
                                    {series.map(line => <td key={line.heroId} className="num-col mono">{format(line.values[i])}</td>)}
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            </details>
        </figure>
    );
}

// stats: analysis.stats from the API (null until the replay is read: then nothing shows). heroes: the lines, in
// order, as { heroId, side } (side: a short note such as "você" or "adversário"); with pickHeroes they are only the
// ones selected at first, and any of the match's heroes can be added. upTo: the last second shown (default: the end).
function StatChart({ stats, heroes, pickHeroes = false, defaultStat = 'netWorth', upTo, title, titleId, note }) {
    const [statKey, setStatKey] = useState(defaultStat);
    // null until the visitor picks: the heroes passed in (which may arrive after the first render).
    const [pickedIds, setPicked] = useState(null);
    if (!stats || stats.seconds.length < 2) {
        return null;
    }

    const picked = pickedIds ?? heroes.map(hero => hero.heroId);
    const sides = new Map(heroes.map(hero => [hero.heroId, hero.side]));
    const chosen = pickHeroes ? picked : heroes.map(hero => hero.heroId);
    // Colors follow the place in the selection, never the hero's rank.
    const lines = chosen
        .map(heroId => heroOf(stats, heroId))
        .filter(Boolean)
        .slice(0, MAX_HEROES)
        .map((hero, index) => ({
            heroId: hero.heroId,
            name: hero.heroName,
            side: sides.get(hero.heroId) ?? (hero.isCurrentPlayer ? 'você' : null),
            isPlayer: hero.isCurrentPlayer,
            slot: index + 1,
        }));
    const toggle = heroId => setPicked(() => (picked.includes(heroId)
        ? picked.filter(id => id !== heroId)
        : picked.length >= MAX_HEROES ? picked : [...picked, heroId]));

    return (
        <section className="section" aria-labelledby={titleId}>
            <SectionHead id={titleId} title={title}>
                <label className="stat-picker">
                    Estatística
                    <select value={statKey} onChange={event => setStatKey(event.target.value)}>
                        {stats.definitions.map(definition => (
                            <option key={definition.key} value={definition.key}>{statLabel(definition.key)}</option>
                        ))}
                    </select>
                </label>
            </SectionHead>
            <div className="card">
                {pickHeroes && (
                    <div className="hero-picker" role="group" aria-label={`Heróis no gráfico (até ${MAX_HEROES})`}>
                        {stats.heroes.map(hero => (
                            <button
                                key={hero.heroId}
                                type="button"
                                className={`hero-chip ${hero.team}`}
                                aria-pressed={picked.includes(hero.heroId)}
                                disabled={!picked.includes(hero.heroId) && picked.length >= MAX_HEROES}
                                onClick={() => toggle(hero.heroId)}
                            >
                                {hero.heroName}
                            </button>
                        ))}
                    </div>
                )}
                <Chart stats={stats} lines={lines} statKey={statKey} lastSecond={upTo ?? Infinity} />
                {note && <p className="section-note">{note}</p>}
            </div>
        </section>
    );
}

export default StatChart;
