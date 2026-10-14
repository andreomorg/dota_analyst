import { useId, useState } from 'react';
import { formatDuration } from '../matches/format';
import { statLabel } from '../stats/statLabels';
import { definitionOf, formatStat, heroOf, seriesOf } from '../stats/statQuery';
import { HEIGHT, MARGIN, nearestPoint, scales, shortNumber, spreadLabels, useWidth } from './chartKit';
import { TimeAxis, Tooltip } from './ChartParts';
import TabIcon from './TabIcon';
import StatePanel from '../ui/StatePanel';

// Any statistic of the API's catalog (analysis.stats) minute by minute, one line per hero (Stitch references "Fase de
// rotas" and "Jogadores"): the statistic is picked above the chart and, with `pickHeroes`, the heroes too (up to
// five, one color each). Under the chart, the same values in a table.

const MAX_HEROES = 5;

function endLabel(name) {
    if (name.length <= 14) return name;
    const words = name.split(' ');
    return words.length > 1
        ? `${words[0]} ${words.slice(1).map(word => `${word[0]}.`).join('')}`
        : `${name.slice(0, 13)}…`;
}

function Chart({ series, seconds, statKey, format, shadePlayer = false }) {
    const [ref, width] = useWidth();
    const [point, setPoint] = useState(null);
    const yMax = Math.max(1, ...series.flatMap(line => line.values));
    const { x, y } = scales(seconds, width, 0, yMax);
    const last = seconds.length - 1;
    const labels = spreadLabels(series.map(line => ({ key: line.heroId, name: line.name, y: y(line.values[last]) + 4 })));

    return (
        <figure className="timeline-chart" ref={ref}>
            <figcaption className="visually-hidden">{statLabel(statKey)} minuto a minuto; os valores estão na tabela abaixo.</figcaption>
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
                {shadePlayer && series.filter(line => line.isPlayer).map(line => (
                    <path key={`area-${line.heroId}`} className={`lane-area series-${line.slot}`} aria-hidden="true"
                        d={`${line.values.map((value, i) => `${i ? 'L' : 'M'}${x(seconds[i])},${y(value)}`).join('')}L${x(seconds[last])},${y(0)}L${x(seconds[0])},${y(0)}Z`} />
                ))}
                {series.map(line => (
                    <path
                        key={line.heroId}
                        className={`lane-line series-${line.slot}${line.isPlayer ? ' is-player' : ''}`}
                        d={line.values.map((value, i) => `${i ? 'L' : 'M'}${x(seconds[i])},${y(value)}`).join('')}
                    />
                ))}
                {series.map(line => (
                    <circle key={line.heroId} className={`chart-dot series-${line.slot}`} cx={x(seconds[last])} cy={y(line.values[last])} r="3.5" aria-hidden="true" />
                ))}
                {labels.map(label => (
                    <text key={label.key} className="chart-end-label" x={x(seconds[last]) + 8} y={label.y}>
                        <title>{label.name}</title>{endLabel(label.name)}
                    </text>
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
        </figure>
    );
}

// stats: analysis.stats from the API (null until the replay is read: then nothing shows). heroes: the lines, in
// order, as { heroId, side } (side: a short note such as "você" or "adversário"); with pickHeroes they are only the
// ones selected at first, and any of the match's heroes can be added. upTo: the last second shown (default: the end).
function StatChart({ stats, heroes, pickHeroes = false, defaultStat = 'netWorth', upTo, title, titleId, note }) {
    const [statKey, setStatKey] = useState(defaultStat);
    const [valuesOpen, setValuesOpen] = useState(() => window.matchMedia('(min-width: 1025px)').matches);
    // null until the visitor picks: the heroes passed in (which may arrive after the first render).
    const [pickedIds, setPicked] = useState(null);
    const selectId = useId();
    if (!stats || stats.seconds.length < 2 || !stats.definitions.length) {
        return (
            <section className="card stat-chart-card" aria-labelledby={titleId}>
                <h2 id={titleId} className="card-heading">{title}</h2>
                <StatePanel icon="chart" title="Estatísticas minuto a minuto indisponíveis" titleAs="h3">
                    <p>Os dados de resumo da partida continuam disponíveis.</p>
                </StatePanel>
            </section>
        );
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
    const slotOf = new Map(lines.map(line => [line.heroId, line.slot]));
    const toggle = heroId => setPicked(() => (picked.includes(heroId)
        ? picked.filter(id => id !== heroId)
        : picked.length >= MAX_HEROES ? picked : [...picked, heroId]));

    const count = stats.seconds.filter(second => second <= (upTo ?? Infinity)).length;
    const seconds = stats.seconds.slice(0, count);
    const definition = definitionOf(stats, statKey);
    const format = value => formatStat(value, definition);
    const series = lines
        .map(line => ({ ...line, values: seriesOf(heroOf(stats, line.heroId), statKey)?.slice(0, count) }))
        .filter(line => line.values);
    const range = `${formatDuration(0)} – ${formatDuration(seconds[seconds.length - 1] ?? 0)}`;

    return (
        <section className={`card stat-chart-card ${pickHeroes ? 'is-hero-comparison' : 'is-lane-comparison'}`} aria-labelledby={titleId}>
            <div className="card-split-head">
                <div>
                    <h2 id={titleId} className="card-heading">
                        <TabIcon tab="chart" className="section-icon" />
                        {title}
                    </h2>
                    {note && <p className="card-subtitle">{note}</p>}
                </div>
                <span className="control-field">
                    <label htmlFor={selectId}>Estatística:</label>
                    <select id={selectId} aria-label="Estatística" value={statKey} onChange={event => setStatKey(event.target.value)}>
                        {stats.definitions.map(candidate => (
                            <option key={candidate.key} value={candidate.key}>{statLabel(candidate.key)}</option>
                        ))}
                    </select>
                </span>
            </div>

            {pickHeroes && (
                <div className="hero-picker-row">
                    <span className="hero-picker-label mono">Escolha até {MAX_HEROES} heróis ({picked.length}/{MAX_HEROES}):</span>
                    <div className="hero-picker" role="group" aria-label={`Heróis no gráfico (até ${MAX_HEROES})`}>
                        {stats.heroes.map(hero => {
                            const on = picked.includes(hero.heroId);
                            return (
                                <button
                                    key={hero.heroId}
                                    type="button"
                                    className={`hero-chip ${hero.team}${on && slotOf.has(hero.heroId) ? ` series-${slotOf.get(hero.heroId)}` : ''}`}
                                    aria-pressed={on}
                                    disabled={!on && picked.length >= MAX_HEROES}
                                    onClick={() => toggle(hero.heroId)}
                                >
                                    <span className="hero-chip-box" aria-hidden="true">{on ? '✓' : ''}</span>
                                    <span className="hero-chip-dot" aria-hidden="true" />
                                    {hero.heroName}{hero.isCurrentPlayer ? ' (você)' : ''}
                                    <span className="visually-hidden"> ({hero.team === 'radiant' ? 'Radiant' : 'Dire'})</span>
                                </button>
                            );
                        })}
                    </div>
                </div>
            )}

            {series.length === 0 || seconds.length < 2
                ? <p className="empty-state">Esta partida não tem {statLabel(statKey).toLowerCase()} minuto a minuto.</p>
                : (
                    <>
                        <ul className="value-legend mono">
                            {series.map(line => (
                                <li key={line.heroId} className={`series-${line.slot}`}>
                                    <span className="legend-dot" aria-hidden="true" />
                                    <span className="value-legend-name">{line.name}{line.side ? ` (${line.side})` : ''}:</span>
                                    <strong>{format(line.values[line.values.length - 1])}</strong>
                                </li>
                            ))}
                        </ul>
                        <div className="chart-box">
                            <p className="chart-box-head mono">
                                <span>{statLabel(statKey)} minuto a minuto</span>
                                <span>{range}</span>
                            </p>
                            <Chart series={series} seconds={seconds} statKey={statKey} format={format} shadePlayer={!pickHeroes} />
                        </div>
                        <details className="values-box" open={valuesOpen} onToggle={event => setValuesOpen(event.currentTarget.open)}>
                            <summary>
                                <span><TabIcon tab="table" className="section-icon" /> Ver os valores minuto a minuto</span>
                                <span className="mono">{range}</span>
                            </summary>
                            <div className="table-scroll values-scroll" tabIndex={0} role="region" aria-label="Estatística minuto a minuto, tabela com rolagem">
                                <table className="data-table values-table">
                                    <caption className="visually-hidden">{statLabel(statKey)} minuto a minuto</caption>
                                    <thead>
                                        <tr>
                                            <th scope="col">Tempo</th>
                                            {series.map(line => <th key={line.heroId} scope="col" className={`num-col series-${line.slot}`}>{line.name}</th>)}
                                        </tr>
                                    </thead>
                                    <tbody>
                                        {seconds.map((second, i) => (
                                            <tr key={second}>
                                                <th scope="row" className="mono">{formatDuration(second)}</th>
                                                {series.map(line => (
                                                    <td key={line.heroId} className={`num-col mono${line.isPlayer ? ' is-player-value' : ''}`}>{format(line.values[i])}</td>
                                                ))}
                                            </tr>
                                        ))}
                                    </tbody>
                                </table>
                            </div>
                        </details>
                    </>
                )}
        </section>
    );
}

export default StatChart;
