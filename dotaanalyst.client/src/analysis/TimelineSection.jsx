import { useId, useLayoutEffect, useRef, useState } from 'react';
import { formatDuration, formatNumber, formatSignedNumber } from '../matches/format';

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

function TimeAxis({ seconds, x }) {
    const bottom = HEIGHT - MARGIN.bottom;
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

function LeadChart({ timeline, metric, teamName }) {
    const [ref, width] = useWidth();
    const [point, setPoint] = useState(null);
    const clipId = useId();
    const { seconds } = timeline;
    const lead = timeline[METRICS[metric].lead];

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
                height={HEIGHT}
                role="img"
                aria-label={`Vantagem de ${METRICS[metric].label.toLowerCase()} do ${teamName} ao longo da partida; no fim, ${formatSignedNumber(last)}.`}
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
                <TimeAxis seconds={seconds} x={x} />
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
                    </>
                )}
            </Tooltip>
        </figure>
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
                <table>
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

// timeline comes from the API (null until the replay is read; absent on older responses: then nothing shows).
function TimelineSection({ timeline }) {
    const [metric, setMetric] = useState('netWorth');
    if (!timeline || timeline.seconds.length < 2) {
        return null;
    }

    const teamName = timeline.perspective === 'dire' ? 'Dire' : 'Radiant';
    return (
        <section className="timeline-section" aria-labelledby="timeline-title">
            <div className="timeline-header">
                <h2 id="timeline-title">Ao longo da partida</h2>
                <div className="segmented" role="group" aria-label="Medida dos gráficos">
                    {Object.entries(METRICS).map(([key, { label }]) => (
                        <button key={key} type="button" aria-pressed={metric === key} onClick={() => setMetric(key)}>
                            {label}
                        </button>
                    ))}
                </div>
            </div>
            <LeadChart timeline={timeline} metric={metric} teamName={teamName} />
            {timeline.lane.length > 0 && <LaneChart timeline={timeline} metric={metric} />}
            <TimelineTable timeline={timeline} metric={metric} teamName={teamName} />
        </section>
    );
}

export default TimelineSection;
