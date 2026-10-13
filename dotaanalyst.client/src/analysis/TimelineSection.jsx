import { useId, useLayoutEffect, useRef, useState } from 'react';
import { formatDuration, formatNumber, formatSignedNumber } from '../matches/format';
import SectionHead from '../ui/SectionHead';
import { KIND_LABELS, objectiveFullName, teamName as objectivesTeamName } from './objectives';

// Two charts from the replay's timeline (net worth or experience, picked with one toggle — never two scales on one
// chart): the perspective team's lead over the other team, and the player against the heroes of their lane.
// Hand-made SVG: lines and areas only, no chart library. Colors are CSS variables (App.css), validated for both
// the light and the dark surface.

const METRICS = {
    netWorth: { label: 'Patrimônio', lead: 'netWorthLead' },
    experience: { label: 'Experiência', lead: 'experienceLead' },
};

const SIDE_LABELS = { player: 'você', ally: 'aliado', opponent: 'adversário' };

const HEIGHT = 220;
const MARGIN = { top: 12, right: 96, bottom: 28, left: 56 };

// Width of the chart's container, so the SVG is drawn at its real size (text never stretched).
function useWidth() {
    const ref = useRef(null);
    const [width, setWidth] = useState(640);
    useLayoutEffect(() => {
        const element = ref.current;
        if (!element) {
            return undefined;
        }
        const observer = new ResizeObserver(([entry]) => setWidth(Math.max(280, Math.floor(entry.contentRect.width))));
        observer.observe(element);
        return () => observer.disconnect();
    }, []);
    return [ref, width];
}

// "+12,3 mil" style ticks are noisy; thousands with a "k" stay short on the axis.
function shortNumber(value) {
    const abs = Math.abs(value);
    const text = abs >= 1000 ? `${formatNumber(Math.round(abs / 100) / 10)}k` : formatNumber(abs);
    return value < 0 ? `−${text}` : value > 0 && text !== '0' ? `+${text}` : text;
}

function minuteTicks(lastSecond) {
    const minutes = lastSecond / 60;
    const step = minutes > 40 ? 10 : 5;
    const ticks = [];
    for (let minute = 0; minute <= minutes; minute += step) {
        ticks.push(minute * 60);
    }
    return ticks;
}

function scales(seconds, width, yMin, yMax) {
    const innerWidth = width - MARGIN.left - MARGIN.right;
    const innerHeight = HEIGHT - MARGIN.top - MARGIN.bottom;
    const last = seconds[seconds.length - 1] || 1;
    const x = second => MARGIN.left + (second / last) * innerWidth;
    const y = value => MARGIN.top + ((yMax - value) / (yMax - yMin || 1)) * innerHeight;
    return { x, y, innerWidth, innerHeight };
}

// The point nearest to the pointer, for the crosshair and the tooltip.
function nearestPoint(event, svg, seconds, x) {
    const box = svg.getBoundingClientRect();
    const pointerX = event.clientX - box.left;
    let best = 0;
    seconds.forEach((second, index) => {
        if (Math.abs(x(second) - pointerX) < Math.abs(x(seconds[best]) - pointerX)) {
            best = index;
        }
    });
    return best;
}

// offset: extra room between the plot and the minute labels (the objectives strip of the lead chart).
function TimeAxis({ seconds, x, offset = 0 }) {
    const bottom = HEIGHT - MARGIN.bottom + offset;
    return (
        <g className="chart-axis" aria-hidden="true">
            {minuteTicks(seconds[seconds.length - 1]).map(second => (
                <text key={second} x={x(second)} y={bottom + 18} textAnchor="middle">{second / 60}′</text>
            ))}
        </g>
    );
}

function Tooltip({ point, x, width, children }) {
    if (point === null) {
        return null;
    }
    // Kept inside the chart: flips to the left of the crosshair on the right half.
    const left = x(point);
    const style = left > width / 2 ? { right: width - left + 8 } : { left: left + 8 };
    return <div className="chart-tooltip" style={style}>{children}</div>;
}

// One mark per objective, in a strip under the plot: the shape tells the kind, the color whether the chart's team
// took it (the lead's own blue/red). Centered on (0, 0); size in pixels.
function ObjectiveMark({ kind }) {
    switch (kind) {
        case 'tower':
            return <path d="M0,-5L5,4L-5,4Z" />;
        case 'barracks':
            return <rect x="-4" y="-4" width="8" height="8" />;
        case 'ancient':
            return <path d="M0,-6L6,0L0,6L-6,0Z" />;
        case 'tormentor':
            return <circle r="4" className="objective-hollow" />;
        default:
            return <circle r="4.5" />;
    }
}

const OBJECTIVE_STRIP = 22;

// The objectives between the previous point and this one (what happened in that minute).
function objectivesAt(objectives, seconds, point) {
    const from = point > 0 ? seconds[point - 1] : -Infinity;
    return objectives.filter(objective => objective.seconds > from && objective.seconds <= seconds[point]);
}

function LeadChart({ timeline, metric, teamName, objectives }) {
    const [ref, width] = useWidth();
    const [point, setPoint] = useState(null);
    const clipId = useId();
    const { seconds } = timeline;
    const lead = timeline[METRICS[metric].lead];
    const strip = objectives.length > 0 ? OBJECTIVE_STRIP : 0;
    const stripY = HEIGHT - MARGIN.bottom + 12;

    const extent = Math.max(1000, ...lead.map(Math.abs));
    const { x, y } = scales(seconds, width, -extent, extent);
    const zero = y(0);
    const line = lead.map((value, i) => `${i ? 'L' : 'M'}${x(seconds[i])},${y(value)}`).join('');
    const area = `${line}L${x(seconds[seconds.length - 1])},${zero}L${x(0)},${zero}Z`;
    const last = lead[lead.length - 1];

    return (
        <figure className="timeline-chart" ref={ref}>
            <figcaption>Vantagem de {METRICS[metric].label.toLowerCase()} do {teamName}</figcaption>
            <svg
                width={width}
                height={HEIGHT + strip}
                role="img"
                aria-label={`Vantagem de ${METRICS[metric].label.toLowerCase()} do ${teamName} ao longo da partida; no fim, ${formatSignedNumber(last)}.${objectives.length > 0 ? ' Os objetivos estão marcados abaixo do gráfico e listados na seção Objetivos.' : ''}`}
                onPointerMove={event => setPoint(nearestPoint(event, event.currentTarget, seconds, x))}
                onPointerLeave={() => setPoint(null)}
            >
                <defs>
                    <clipPath id={`${clipId}-above`}><rect x="0" y="0" width={width} height={zero} /></clipPath>
                    <clipPath id={`${clipId}-below`}><rect x="0" y={zero} width={width} height={HEIGHT - zero} /></clipPath>
                </defs>
                <g className="chart-axis" aria-hidden="true">
                    {[extent, 0, -extent].map(value => (
                        <g key={value}>
                            <line className={value === 0 ? 'chart-zero' : 'chart-grid'} x1={MARGIN.left} x2={width - MARGIN.right} y1={y(value)} y2={y(value)} />
                            <text x={MARGIN.left - 8} y={y(value) + 4} textAnchor="end">{shortNumber(value)}</text>
                        </g>
                    ))}
                </g>
                <TimeAxis seconds={seconds} x={x} offset={strip} />
                {objectives.length > 0 && (
                    <g aria-hidden="true">
                        {objectives.map((objective, index) => (
                            <g
                                key={`${objective.seconds}-${index}`}
                                className={`objective-mark ${objective.takenBy === timeline.perspective ? 'ahead' : 'behind'}`}
                                transform={`translate(${x(Math.min(objective.seconds, seconds[seconds.length - 1]))},${stripY})`}
                            >
                                <ObjectiveMark kind={objective.kind} />
                            </g>
                        ))}
                    </g>
                )}
                <path className="lead-area ahead" d={area} clipPath={`url(#${clipId}-above)`} />
                <path className="lead-area behind" d={area} clipPath={`url(#${clipId}-below)`} />
                <path className="lead-line ahead" d={line} clipPath={`url(#${clipId}-above)`} />
                <path className="lead-line behind" d={line} clipPath={`url(#${clipId}-below)`} />
                <text className="chart-end-label" x={x(seconds[seconds.length - 1]) + 8} y={y(last) + 4}>
                    {shortNumber(last)}
                </text>
                {point !== null && (
                    <g aria-hidden="true">
                        <line className="chart-crosshair" x1={x(seconds[point])} x2={x(seconds[point])} y1={MARGIN.top} y2={HEIGHT - MARGIN.bottom} />
                        <circle className={`chart-dot ${lead[point] >= 0 ? 'ahead' : 'behind'}`} cx={x(seconds[point])} cy={y(lead[point])} r="5" />
                    </g>
                )}
            </svg>
            <Tooltip point={point === null ? null : seconds[point]} x={x} width={width}>
                {point !== null && (
                    <>
                        <strong>{formatDuration(seconds[point])}</strong>
                        <span>{lead[point] >= 0 ? 'À frente' : 'Atrás'}: {formatSignedNumber(lead[point])}</span>
                        {objectivesAt(objectives, seconds, point).map((objective, index) => (
                            <span key={index}>
                                {formatDuration(objective.seconds)} {objectiveFullName(objective)}
                                {objective.kind === 'roshan' || objective.kind === 'tormentor' ? ` (${objectivesTeamName(objective.takenBy)})` : ''}
                            </span>
                        ))}
                    </>
                )}
            </Tooltip>
            {objectives.length > 0 && <ObjectiveLegend teamName={teamName} />}
        </figure>
    );
}

function ObjectiveLegend({ teamName }) {
    return (
        <ul className="chart-legend objective-legend">
            {['tower', 'barracks', 'ancient', 'roshan', 'tormentor'].map(kind => (
                <li key={kind}>
                    <svg width="14" height="14" viewBox="-7 -7 14 14" aria-hidden="true" className="objective-mark neutral">
                        <ObjectiveMark kind={kind} />
                    </svg>
                    {KIND_LABELS[kind]}
                </li>
            ))}
            <li><span className="legend-swatch objective-ahead" aria-hidden="true" /> Conquistado pelo {teamName}</li>
            <li><span className="legend-swatch objective-behind" aria-hidden="true" /> Contra o {teamName}</li>
        </ul>
    );
}

// End labels move apart when two lines end close together (at least 14 px between them).
function spreadLabels(labels) {
    const sorted = [...labels].sort((a, b) => a.y - b.y);
    for (let i = 1; i < sorted.length; i++) {
        sorted[i].y = Math.max(sorted[i].y, sorted[i - 1].y + 14);
    }
    return sorted;
}

function LaneChart({ timeline, metric }) {
    const [ref, width] = useWidth();
    const [point, setPoint] = useState(null);
    const { seconds, lane } = timeline;
    const values = hero => hero[metric];

    const yMax = Math.max(1000, ...lane.flatMap(values));
    const { x, y } = scales(seconds, width, 0, yMax);
    const last = seconds.length - 1;
    // Color follows the hero's place in the list (player first, then allies, then opponents), never its rank.
    const series = lane.map((hero, index) => ({ hero, slot: index + 1 }));
    const labels = spreadLabels(series.map(({ hero, slot }) => ({ key: hero.heroId, slot, name: hero.heroName, y: y(values(hero)[last]) + 4 })));

    return (
        <figure className="timeline-chart" ref={ref}>
            <figcaption>{METRICS[metric].label} na sua rota</figcaption>
            <ul className="chart-legend">
                {series.map(({ hero, slot }) => (
                    <li key={hero.heroId}>
                        <span className={`legend-swatch series-${slot}`} aria-hidden="true" />
                        {hero.heroName} <span className="legend-side">({SIDE_LABELS[hero.side]})</span>
                    </li>
                ))}
            </ul>
            <svg
                width={width}
                height={HEIGHT}
                role="img"
                aria-label={`${METRICS[metric].label} de cada herói da sua rota ao longo da partida.`}
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
                {series.map(({ hero, slot }) => (
                    <path
                        key={hero.heroId}
                        className={`lane-line series-${slot}${hero.side === 'player' ? ' is-player' : ''}`}
                        d={values(hero).map((value, i) => `${i ? 'L' : 'M'}${x(seconds[i])},${y(value)}`).join('')}
                    />
                ))}
                {labels.map(label => (
                    <text key={label.key} className="chart-end-label" x={x(seconds[last]) + 8} y={label.y}>{label.name}</text>
                ))}
                {point !== null && (
                    <g aria-hidden="true">
                        <line className="chart-crosshair" x1={x(seconds[point])} x2={x(seconds[point])} y1={MARGIN.top} y2={HEIGHT - MARGIN.bottom} />
                        {series.map(({ hero, slot }) => (
                            <circle key={hero.heroId} className={`chart-dot series-${slot}`} cx={x(seconds[point])} cy={y(values(hero)[point])} r="5" />
                        ))}
                    </g>
                )}
            </svg>
            <Tooltip point={point === null ? null : seconds[point]} x={x} width={width}>
                {point !== null && (
                    <>
                        <strong>{formatDuration(seconds[point])}</strong>
                        {series.map(({ hero, slot }) => (
                            <span key={hero.heroId}>
                                <span className={`legend-swatch series-${slot}`} aria-hidden="true" /> {hero.heroName}: {formatNumber(values(hero)[point])}
                            </span>
                        ))}
                    </>
                )}
            </Tooltip>
        </figure>
    );
}

// The same numbers as the charts, for screen readers and anyone who prefers a table.
function TimelineTable({ timeline, metric, teamName }) {
    const lead = timeline[METRICS[metric].lead];
    return (
        <details className="timeline-table">
            <summary>Ver os valores minuto a minuto</summary>
            <div className="table-scroll">
                <table className="data-table">
                    <thead>
                        <tr>
                            <th scope="col">Tempo</th>
                            <th scope="col">Vantagem do {teamName}</th>
                            {timeline.lane.map(hero => <th key={hero.heroId} scope="col">{hero.heroName}</th>)}
                        </tr>
                    </thead>
                    <tbody>
                        {timeline.seconds.map((second, i) => (
                            <tr key={second}>
                                <th scope="row">{formatDuration(second)}</th>
                                <td>{formatSignedNumber(lead[i])}</td>
                                {timeline.lane.map(hero => <td key={hero.heroId}>{formatNumber(hero[metric][i])}</td>)}
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>
        </details>
    );
}

const TITLES = {
    all: 'Vantagem de patrimônio e experiência',
    lead: 'Como a partida andou',
    lane: 'Patrimônio e experiência na sua rota',
};

// timeline comes from the API (null until the replay is read; absent on older responses: then nothing shows).
// objectives (null/absent on older responses) are marked under the lead chart. charts: 'all' (both charts and the
// table), 'lead' (only the team's lead, for the overview) or 'lane' (only the player's lane, for the laning tab).
function TimelineSection({ timeline, objectives, charts = 'all' }) {
    const [metric, setMetric] = useState('netWorth');
    if (!timeline || timeline.seconds.length < 2 || (charts === 'lane' && timeline.lane.length === 0)) {
        return null;
    }

    const teamName = timeline.perspective === 'dire' ? 'Dire' : 'Radiant';
    return (
        <section className="section" aria-labelledby={`timeline-title-${charts}`}>
            <SectionHead id={`timeline-title-${charts}`} title={TITLES[charts]}>
                <div className="segmented" role="group" aria-label="Medida dos gráficos">
                    {Object.entries(METRICS).map(([key, { label }]) => (
                        <button key={key} type="button" aria-pressed={metric === key} onClick={() => setMetric(key)}>
                            {label}
                        </button>
                    ))}
                </div>
            </SectionHead>
            <div className="card">
                {charts !== 'lane' && (
                    <LeadChart timeline={timeline} metric={metric} teamName={teamName} objectives={objectives ?? []} />
                )}
                {charts !== 'lead' && timeline.lane.length > 0 && <LaneChart timeline={timeline} metric={metric} />}
                {charts === 'all' && <TimelineTable timeline={timeline} metric={metric} teamName={teamName} />}
            </div>
        </section>
    );
}

export default TimelineSection;
