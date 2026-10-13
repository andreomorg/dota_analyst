import { useId, useState } from 'react';
import { formatDuration, formatSignedNumber } from '../matches/format';
import SectionHead from '../ui/SectionHead';
import { HEIGHT, MARGIN, nearestPoint, scales, shortNumber, useWidth } from './chartKit';
import { TimeAxis, Tooltip } from './ChartParts';
import { KIND_LABELS, objectiveFullName, teamName as objectivesTeamName } from './objectives';

// How the match went (the overview): the perspective team's lead over the other team in net worth or experience,
// picked with one toggle — never two scales on one chart — with the objectives marked under it. Each hero's own
// numbers over time are in StatChart (any statistic of the catalog).

const METRICS = {
    netWorth: { label: 'Patrimônio', lead: 'netWorthLead' },
    experience: { label: 'Experiência', lead: 'experienceLead' },
};
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

// timeline comes from the API (null until the replay is read; absent on older responses: then nothing shows).
// objectives (null/absent on older responses) are marked under the chart.
function TimelineSection({ timeline, objectives }) {
    const [metric, setMetric] = useState('netWorth');
    if (!timeline || timeline.seconds.length < 2) {
        return null;
    }

    const teamName = timeline.perspective === 'dire' ? 'Dire' : 'Radiant';
    return (
        <section className="section" aria-labelledby="timeline-title">
            <SectionHead id="timeline-title" title="Como a partida andou">
                <div className="segmented" role="group" aria-label="Medida do gráfico">
                    {Object.entries(METRICS).map(([key, { label }]) => (
                        <button key={key} type="button" aria-pressed={metric === key} onClick={() => setMetric(key)}>
                            {label}
                        </button>
                    ))}
                </div>
            </SectionHead>
            <div className="card">
                <LeadChart timeline={timeline} metric={metric} teamName={teamName} objectives={objectives ?? []} />
            </div>
        </section>
    );
}

export default TimelineSection;