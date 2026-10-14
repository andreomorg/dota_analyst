import { useId, useMemo, useState } from 'react';
import { toPercent } from '../matches/format';
import GameMap from './GameMap';
import HeatmapIndicators from './HeatmapIndicators';
import { renderHeatmap, sumCells } from './heatmap';
import { MINIMAP_ORIGIN, MINIMAP_SIZE, svgY, VIEW_BOX } from './mapGeometry';
import TabIcon from './TabIcon';

// Where the player spent the match, as a heatmap on the game's minimap, next to another hero (by default the enemy
// of the same role). Like the heatmaps of a football player: red where they spent the most time. Two layouts of the
// Stitch references: the lane card of "Fase de rotas" (0–10 min) and the filters + maps + regions of "Posicionamento".

// indicators: the key of the phase in the API's indicators (HeatmapIndicators.jsx); regions: the key of the phase in
// smartStats' time by region (null = the whole match).
const PHASES = [
    { key: 'all', label: 'Partida inteira', fields: ['earlyGame', 'midGame', 'lateGame'], indicators: 'all', regions: null, from: 0, to: Infinity },
    { key: 'early', label: '0–10 min', fields: ['earlyGame'], indicators: 'earlyGame', regions: 'earlyGame', from: 0, to: 600 },
    { key: 'mid', label: '10–20 min', fields: ['midGame'], indicators: 'midGame', regions: 'midGame', from: 600, to: 1200 },
    { key: 'late', label: '20+ min', fields: ['lateGame'], indicators: 'lateGame', regions: 'lateGame', from: 1200, to: Infinity },
];

const LANES = { top: 'Rota de cima', middle: 'Rota do meio', bottom: 'Rota de baixo', jungle: 'Selva', roaming: 'Roaming' };

const REGIONS = [
    { key: 'ownBase', label: 'Sua base' },
    { key: 'topLane', label: 'Rota de cima' },
    { key: 'middleLane', label: 'Rota do meio' },
    { key: 'bottomLane', label: 'Rota de baixo' },
    { key: 'ownJungle', label: 'Sua selva' },
    { key: 'river', label: 'Rio' },
    { key: 'enemyJungle', label: 'Selva inimiga' },
    { key: 'enemyBase', label: 'Base inimiga' },
];
const ENEMY_REGIONS = new Set(['enemyJungle', 'enemyBase']);

const teamName = team => (team === 'radiant' ? 'Radiant' : 'Dire');

function HeatmapImage({ hero, phase, gridSize }) {
    const totals = useMemo(
        () => sumCells(...phase.fields.map(field => hero[field])),
        [hero, phase],
    );
    const url = useMemo(() => renderHeatmap(totals, gridSize), [totals, gridSize]);
    if (!url) {
        return null;
    }
    // The grid covers the same square as the minimap image, so the heatmap goes exactly over it.
    return (
        <image
            className="heatmap-layer"
            href={url}
            x={MINIMAP_ORIGIN}
            y={svgY(-MINIMAP_ORIGIN)}
            width={MINIMAP_SIZE}
            height={MINIMAP_SIZE}
            preserveAspectRatio="none"
        />
    );
}

function aliveMinutes(hero, phase, secondsPerSample) {
    const samples = phase.fields
        .flatMap(field => hero[field] ?? [])
        .reduce((sum, value, index) => (index % 3 === 2 ? sum + value : sum), 0);
    return Math.round((samples * secondsPerSample) / 60);
}

// One hero's map: a caption above (who, team, minutes alive) and the minimap with the heat over it.
function HeatmapPanel({ hero, label, tone, phase, heatmaps, deaths }) {
    const minutes = aliveMinutes(hero, phase, heatmaps.secondsPerSample);
    return (
        <figure className="heatmap-panel">
            <figcaption>
                <span className={`heatmap-who text-${tone}`}>
                    <span className="key-dot" aria-hidden="true" />
                    {label} ({hero.heroName})
                </span>
                <span className="heatmap-minutes mono">{teamName(hero.team)} · {minutes} min vivo</span>
            </figcaption>
            <div className="heatmap-frame">
                <svg viewBox={VIEW_BOX} role="img" aria-label={`Mapa de calor de ${hero.heroName}, ${phase.label}${deaths?.length ? `, com ${deaths.length} mortes marcadas` : ''}.`}>
                    <GameMap />
                    <rect className="heatmap-dim" x={MINIMAP_ORIGIN} y={MINIMAP_ORIGIN} width={MINIMAP_SIZE} height={MINIMAP_SIZE} />
                    <HeatmapImage hero={hero} phase={phase} gridSize={heatmaps.gridSize} />
                    {deaths?.map(death => (
                        <g key={death.seconds} className="heatmap-death" transform={`translate(${death.x},${svgY(death.y)})`}>
                            <circle r="330" />
                            <path d="M -150 -150 L 150 150 M 150 -150 L -150 150" />
                        </g>
                    ))}
                </svg>
            </div>
        </figure>
    );
}

function DensityLegend({ withDeaths }) {
    return (
        <p className="heatmap-legend mono">
            <span className="heatmap-legend-title">Densidade:</span>
            <span><span className="density-dot density-low" aria-hidden="true" /> Pouco tempo</span>
            <span><span className="density-dot density-mid" aria-hidden="true" /> Médio</span>
            <span><span className="density-dot density-high" aria-hidden="true" /> Muito tempo</span>
            {withDeaths && <span className="heatmap-legend-death"><span className="death-x" aria-hidden="true">×</span> Sua morte</span>}
        </p>
    );
}

// The player's share of time alive in each region of the map in the period (smartStats, replay): the side panel of
// "Posicionamento". null for matches without role queue.
function RegionShares({ shares, phase }) {
    if (!shares) {
        return null;
    }
    const rows = REGIONS
        .map(region => ({ ...region, share: shares[region.key] ?? 0 }))
        .filter(region => region.share > 0)
        .sort((a, b) => b.share - a.share);
    return (
        <div className="region-panel">
            <h3 className="region-title">
                <TabIcon tab="positioning" className="section-icon" />
                Seu tempo por região
                <span className="region-phase mono">{phase.label}</span>
            </h3>
            <ul className="region-list">
                {rows.map(region => (
                    <li key={region.key}>
                        <span className="region-name">{region.label}</span>
                        <span className={`region-share mono ${ENEMY_REGIONS.has(region.key) ? 'text-loss' : 'text-win'}`}>{toPercent(region.share)}%</span>
                        <span className="region-bar" aria-hidden="true">
                            <span className={ENEMY_REGIONS.has(region.key) ? 'is-enemy' : undefined} style={{ width: `${toPercent(region.share)}%` }} />
                        </span>
                    </li>
                ))}
            </ul>
            <p className="region-note">Parte do tempo vivo, pelas regiões do mapa (bases, rotas, rio e selvas).</p>
        </div>
    );
}

// heatmaps, deaths, smartStats and laning come from the API (heatmaps is null until the replay is read and for
// replays read before heatmaps existed: then nothing shows). variant 'laning': only the first 10 minutes, compared
// with the heroes of the player's lane (the "Fase de rotas" tab); 'afterLaning': every period but the first 10
// minutes (the "Posicionamento" tab). Outside 'laning', every hero.
function HeatmapSection({ heatmaps, deaths, smartStats, laning, variant }) {
    const isLaning = variant === 'laning';
    const [phaseKey, setPhaseKey] = useState(isLaning ? 'early' : variant === 'afterLaning' ? 'mid' : 'all');
    const [otherHeroId, setOtherHeroId] = useState(null);
    const [showDeaths, setShowDeaths] = useState(true);
    const selectId = useId();
    if (!heatmaps || heatmaps.heroes.length === 0) {
        return null;
    }

    // Each period shows in one tab only: 0–10 in "Fase de rotas", the rest (and the whole match) in "Posicionamento".
    const phases = isLaning
        ? PHASES.filter(candidate => candidate.key === 'early')
        : variant === 'afterLaning' ? PHASES.filter(candidate => candidate.key !== 'early') : PHASES;
    const phase = phases.find(candidate => candidate.key === phaseKey) ?? phases[0];
    const regionShares = phase.regions ? smartStats?.player.timeByRegionByPhase?.[phase.regions] : smartStats?.player.timeByRegion;
    const hasRegions = regionShares && Object.values(regionShares).some(share => share > 0);
    const player = heatmaps.heroes.find(hero => hero.isCurrentPlayer);
    const laneHeroIds = new Set([...(laning?.opponents ?? []), ...(laning?.allies ?? [])].map(hero => hero.heroId));
    const others = heatmaps.heroes.filter(hero => hero !== player && (!isLaning || laneHeroIds.has(hero.heroId)));
    if (isLaning && others.length === 0) {
        return null;
    }
    // The lane opponent in the laning tab; otherwise the enemy of the same role, else the lane opponent, else the
    // first enemy.
    const defaultOtherId = (isLaning ? laning?.opponents?.[0]?.heroId : smartStats?.counterpartHeroId)
        ?? laning?.opponents?.[0]?.heroId
        ?? others.find(hero => player && hero.team !== player.team)?.heroId
        ?? others[0]?.heroId;
    const other = others.find(hero => hero.heroId === (otherHeroId ?? defaultOtherId)) ?? others[0];
    const canShowDeaths = Boolean(player && deaths);
    const phaseDeaths = showDeaths && deaths
        ? deaths.filter(death => death.seconds >= phase.from && death.seconds < phase.to)
        : null;
    const otherTone = player && other && other.team === player.team ? 'win' : 'loss';
    const otherLabel = laning?.opponents?.some(hero => hero.heroId === other?.heroId) && isLaning
        ? 'Adversário'
        : other?.heroId === smartStats?.counterpartHeroId ? 'Mesma posição' : 'Comparação';

    const compareSelect = others.length > 0 && (
        <span className="control-field">
            <label htmlFor={selectId}>Comparar com:</label>
            <select id={selectId} value={other.heroId} onChange={event => setOtherHeroId(Number(event.target.value))}>
                {others.map(hero => (
                    <option key={hero.heroId} value={hero.heroId}>
                        {hero.heroName} ({player && hero.team === player.team ? 'aliado' : 'inimigo'})
                    </option>
                ))}
            </select>
        </span>
    );
    const deathsToggle = canShowDeaths && (
        <label className="heatmap-toggle">
            <input type="checkbox" checked={showDeaths} onChange={event => setShowDeaths(event.target.checked)} />
            {isLaning ? 'Mostrar suas mortes na rota' : 'Mostrar suas mortes'}
        </label>
    );
    const maps = (
        <div className="heatmap-pair">
            {player && <HeatmapPanel hero={player} label="Você" tone="win" phase={phase} heatmaps={heatmaps} deaths={phaseDeaths} />}
            {other && <HeatmapPanel hero={other} label={otherLabel} tone={otherTone} phase={phase} heatmaps={heatmaps} />}
        </div>
    );
    const indicators = (
        <HeatmapIndicators
            player={player}
            other={other}
            phaseKey={phase.indicators}
            phaseLabel={phase.label.toLowerCase()}
            matchAverage={heatmaps.matchAverage}
        />
    );

    if (isLaning) {
        return (
            <section className="card heatmap-card" aria-labelledby="laning-heatmap-title">
                <div className="card-split-head">
                    <div>
                        <h2 id="laning-heatmap-title" className="card-heading">Mapa de calor da rota</h2>
                        <p className="card-subtitle">Onde você e os heróis da rota passaram de 0 a 10 min</p>
                    </div>
                    {laning && LANES[laning.lane] && <span className="tag-outline mono">{LANES[laning.lane]}</span>}
                </div>
                <div className="control-box">
                    {compareSelect}
                    {deathsToggle && (
                        <p className="control-row">
                            {deathsToggle}
                            <span className="mono">0–10 min</span>
                        </p>
                    )}
                </div>
                {maps}
                <DensityLegend withDeaths={canShowDeaths && showDeaths} />
                <p className="section-note">
                    Cada mapa usa a própria escala: compare o formato, não a intensidade. Tempo morto não aparece; posição a cada {heatmaps.secondsPerSample} s.
                </p>
                <details className="inline-details">
                    <summary>Indicadores de posicionamento na rota</summary>
                    {indicators}
                </details>
            </section>
        );
    }

    return (
        <>
            <div className="card filter-bar" role="group" aria-label="Filtros do mapa de calor">
                {compareSelect}
                {phases.length > 1 && (
                    <span className="control-field">
                        <span id={`${selectId}-phase`}>Fase:</span>
                        <span className="segmented" role="group" aria-labelledby={`${selectId}-phase`}>
                            {phases.map(candidate => (
                                <button
                                    key={candidate.key}
                                    type="button"
                                    aria-pressed={candidate.key === phase.key}
                                    onClick={() => setPhaseKey(candidate.key)}
                                >
                                    {candidate.label}
                                </button>
                            ))}
                        </span>
                    </span>
                )}
                {deathsToggle}
            </div>
            <section className="card heatmap-card" aria-labelledby="heatmap-title">
                <div className="card-split-head">
                    <div>
                        <h2 id="heatmap-title" className="card-heading">
                            <TabIcon tab="map" className="section-icon" />
                            Mapa de calor de ocupação ({phase.label.toLowerCase()})
                        </h2>
                        <p className="card-subtitle">
                            Onde cada herói passou o tempo vivo, como nos mapas de calor do futebol. Posição a cada {heatmaps.secondsPerSample} s, lida do replay.
                        </p>
                    </div>
                    <DensityLegend withDeaths={canShowDeaths && showDeaths} />
                </div>
                <div className={`heatmap-layout${hasRegions ? ' has-regions' : ''}`}>
                    {maps}
                    {hasRegions && <RegionShares shares={regionShares} phase={phase} />}
                </div>
                <p className="section-note">Cada mapa usa a própria escala: compare o formato, não a intensidade. Tempo morto não aparece.</p>
                {indicators}
            </section>
        </>
    );
}

export default HeatmapSection;
