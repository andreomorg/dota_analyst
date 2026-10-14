import { useId, useState } from 'react';
import { formatDuration, formatSignedNumber } from '../matches/format';
import { HEIGHT, MARGIN, nearestPoint, scales, shortNumber, useWidth } from './chartKit';
import { TimeAxis, Tooltip } from './ChartParts';
import { KIND_LABELS, objectiveFullName, objectiveName, teamName as objectivesTeamName } from './objectives';
import TabIcon from './TabIcon';

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
// The lead chart has no end label (the final value is above it), so the line runs almost to the edge.
const RIGHT = 12;

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

    const minimum = Math.min(0, ...lead);
    const maximum = Math.max(0, ...lead) || (minimum === 0 ? 1000 : 0);
    const ticks = minimum < 0 && maximum > 0
        ? [maximum, 0, minimum]
        : maximum > 0 ? [maximum, maximum / 2, 0] : [0, minimum / 2, minimum];
    const { x, y } = scales(seconds, width, minimum, maximum, RIGHT);
    const zero = y(0);
    const line = lead.map((value, i) => `${i ? 'L' : 'M'}${x(seconds[i])},${y(value)}`).join('');
    const area = `${line}L${x(seconds[seconds.length - 1])},${zero}L${x(0)},${zero}Z`;
    const last = lead[lead.length - 1];

    return (
        <figure className="timeline-chart" ref={ref}>
            <figcaption className="visually-hidden">Vantagem de {METRICS[metric].label.toLowerCase()} do {teamName}</figcaption>
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
                    <linearGradient id={`${clipId}-ahead`} gradientUnits="userSpaceOnUse" x1="0" x2="0" y1={MARGIN.top} y2={zero}>
                        <stop offset="0" className="lead-stop-ahead" stopOpacity="0.3" />
                        <stop offset="1" className="lead-stop-ahead" stopOpacity="0" />
                    </linearGradient>
                    <linearGradient id={`${clipId}-behind`} gradientUnits="userSpaceOnUse" x1="0" x2="0" y1={zero} y2={HEIGHT - MARGIN.bottom}>
                        <stop offset="0" className="lead-stop-behind" stopOpacity="0" />
                        <stop offset="1" className="lead-stop-behind" stopOpacity="0.3" />
                    </linearGradient>
                </defs>
                <g className="chart-axis" aria-hidden="true">
                    {ticks.map(value => (
                        <g key={value}>
                            <line className={value === 0 ? 'chart-zero' : 'chart-grid'} x1={MARGIN.left} x2={width - RIGHT} y1={y(value)} y2={y(value)} />
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
                <path className="lead-area" fill={`url(#${clipId}-ahead)`} d={area} clipPath={`url(#${clipId}-above)`} />
                <path className="lead-area" fill={`url(#${clipId}-behind)`} d={area} clipPath={`url(#${clipId}-below)`} />
                <path className="lead-line ahead" d={line} clipPath={`url(#${clipId}-above)`} />
                <path className="lead-line behind" d={line} clipPath={`url(#${clipId}-below)`} />
                <circle className={`chart-end-dot ${last >= 0 ? 'ahead' : 'behind'}`} cx={x(seconds[seconds.length - 1])} cy={y(last)} r="5" />
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

// The milestones under the chart: the start, the first tower the chart's team took, the first Roshan and the end —
// only the ones that happened (the full list is in the "Lutas e objetivos" tab).
function milestones(timeline, objectives, durationSeconds, radiantWon, teamName) {
    const firstTower = objectives.find(objective => objective.kind === 'tower' && !objective.denied && objective.takenBy === timeline.perspective);
    const firstRoshan = objectives.find(objective => objective.kind === 'roshan');
    const won = radiantWon === null || radiantWon === undefined ? null : radiantWon === (timeline.perspective !== 'dire');
    return [
        { key: 'start', seconds: 0, label: 'Início da partida' },
        firstTower && { key: 'tower', seconds: firstTower.seconds, label: `${objectiveName(firstTower)} caiu`, tone: 'win', icon: 'tower' },
        firstRoshan && {
            key: 'roshan',
            seconds: firstRoshan.seconds,
            label: `Roshan (${objectivesTeamName(firstRoshan.takenBy)})${firstRoshan.aegisHeroName ? ' + Aegis' : ''}`,
            tone: firstRoshan.takenBy === timeline.perspective ? 'win' : 'loss',
            icon: 'roshan',
        },
        {
            key: 'end',
            seconds: durationSeconds,
            label: won === null ? 'Fim da partida' : `${won ? 'Vitória' : 'Derrota'} do ${teamName}`,
            tone: won === null ? undefined : won ? 'win' : 'loss',
        },
    ].filter(Boolean);
}

const MILESTONE_ICONS = {
    tower: <path d="M4 21V9l8-6 8 6v12h-6v-7h-4v7z" />,
    roshan: <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" />,
};

// timeline comes from the API (null until the replay is read; absent on older responses: then nothing shows).
// objectives (null/absent on older responses) are marked under the chart.
function TimelineSection({ timeline, objectives, durationSeconds, radiantWon }) {
    const [metric, setMetric] = useState('netWorth');
    if (!timeline || timeline.seconds.length < 2) {
        return null;
    }

    const teamName = timeline.perspective === 'dire' ? 'Dire' : 'Radiant';
    const lead = timeline[METRICS[metric].lead];
    const last = lead[lead.length - 1];
    const end = timeline.seconds[timeline.seconds.length - 1];
    const marks = milestones(timeline, objectives ?? [], durationSeconds ?? end, radiantWon, teamName);
    return (
        <section className="card timeline-card" aria-labelledby="timeline-title">
            <div className="timeline-card-head">
                <div>
                    <h2 id="timeline-title" className="card-heading">
                        <TabIcon tab="trend" className="section-icon" />
                        Vantagem de {METRICS[metric].label.toLowerCase()} do {teamName}
                    </h2>
                    <p className="card-subtitle">Como a partida andou, minuto a minuto, com os objetivos marcados abaixo do gráfico</p>
                </div>
                <div className="timeline-card-tools">
                    <ul className="timeline-key mono">
                        <li className={last >= 0 ? 'text-win' : 'text-loss'}>
                            <span className="key-dot" aria-hidden="true" />
                            {last >= 0 ? 'Vantagem' : 'Desvantagem'} {teamName} ({shortNumber(last)})
                        </li>
                        <li><span className="key-line" aria-hidden="true" /> Equilíbrio (0)</li>
                    </ul>
                    <div className="segmented" role="group" aria-label="Medida do gráfico">
                        {Object.entries(METRICS).map(([key, { label }]) => (
                            <button key={key} type="button" aria-pressed={metric === key} onClick={() => setMetric(key)}>
                                {label}
                            </button>
                        ))}
                    </div>
                </div>
            </div>
            <div className="timeline-box">
                <p className={`timeline-final mono ${last >= 0 ? 'text-win' : 'text-loss'}`}>
                    Final: {shortNumber(last)} aos {formatDuration(end)}
                </p>
                <LeadChart timeline={timeline} metric={metric} teamName={teamName} objectives={objectives ?? []} />
                <ol className="timeline-milestones mono" style={{ '--count': marks.length }}>
                    {marks.map(mark => (
                        <li key={mark.key}>
                            <span className="milestone-time">{formatDuration(mark.seconds)}</span>
                            <span className={`milestone-label${mark.tone ? ` text-${mark.tone}` : ''}`}>
                                {mark.icon && <svg viewBox="0 0 24 24" aria-hidden="true">{MILESTONE_ICONS[mark.icon]}</svg>}
                                {mark.label}
                            </span>
                        </li>
                    ))}
                </ol>
            </div>
        </section>
    );
}

export default TimelineSection;
