import { useMemo, useState } from 'react';
import { HeroIcon } from '../heroes/HeroImage';
import GameMap from './GameMap';
import HeatmapIndicators from './HeatmapIndicators';
import { renderHeatmap, sumCells } from './heatmap';
import { MINIMAP_ORIGIN, MINIMAP_SIZE, svgY, VIEW_BOX } from './mapGeometry';

// Where the player spent the match, as a heatmap on the game's minimap, next to another hero (by default the enemy
// of the same role). Like the heatmaps of a football player: red where they spent the most time.

// indicators: the key of the phase in the API's indicators (HeatmapIndicators.jsx).
const PHASES = [
    { key: 'all', label: 'Partida inteira', fields: ['earlyGame', 'midGame', 'lateGame'], indicators: 'all', from: 0, to: Infinity },
    { key: 'early', label: '0–10 min', fields: ['earlyGame'], indicators: 'earlyGame', from: 0, to: 600 },
    { key: 'mid', label: '10–20 min', fields: ['midGame'], indicators: 'midGame', from: 600, to: 1200 },
    { key: 'late', label: '20+ min', fields: ['lateGame'], indicators: 'lateGame', from: 1200, to: Infinity },
];

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

function HeatmapCard({ hero, label, phase, heatmaps, deaths }) {
    const minutes = aliveMinutes(hero, phase, heatmaps.secondsPerSample);
    return (
        <figure className={`heatmap-panel ${hero.isCurrentPlayer ? 'is-player' : ''}`}>
            <figcaption>
                <HeroIcon heroId={hero.heroId} />
                <span className="heatmap-who">
                    <strong>{label}</strong>
                    <span>{hero.heroName} · {hero.team === 'radiant' ? 'Radiant' : 'Dire'}</span>
                </span>
                <span className="heatmap-minutes">{minutes} min vivo</span>
            </figcaption>
            <div className="heatmap-frame">
                <svg viewBox={VIEW_BOX} role="img" aria-label={`Mapa de calor de ${hero.heroName}, ${phase.label}.`}>
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

// heatmaps, deaths, smartStats and laning come from the API (heatmaps is null until the replay is read and for
// replays read before heatmaps existed: then nothing shows). variant 'laning': only the first 10 minutes, compared
// with the heroes of the player's lane (the "Fase de rotas" tab); 'afterLaning': every period but the first 10
// minutes (the "Posicionamento" tab); otherwise every period. Outside 'laning', every hero.
function HeatmapSection({ heatmaps, deaths, smartStats, laning, variant }) {
    const isLaning = variant === 'laning';
    const [phaseKey, setPhaseKey] = useState(isLaning ? 'early' : 'all');
    const [otherHeroId, setOtherHeroId] = useState(null);
    const [showDeaths, setShowDeaths] = useState(true);
    if (!heatmaps || heatmaps.heroes.length === 0) {
        return null;
    }

    // Each period shows in one tab only: 0–10 in "Fase de rotas", the rest (and the whole match) in "Posicionamento".
    const phases = isLaning
        ? PHASES.filter(candidate => candidate.key === 'early')
        : variant === 'afterLaning' ? PHASES.filter(candidate => candidate.key !== 'early') : PHASES;
    const phase = phases.find(candidate => candidate.key === phaseKey) ?? phases[0];
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
    const titleId = isLaning ? 'laning-heatmap-title' : 'heatmap-title';
    const other = others.find(hero => hero.heroId === (otherHeroId ?? defaultOtherId)) ?? others[0];
    const phaseDeaths = showDeaths && deaths
        ? deaths.filter(death => death.seconds >= phase.from && death.seconds < phase.to)
        : null;

    return (
        <div className="card heatmap-card">
            <div className="card-head">
                <h3 id={titleId}>{isLaning ? 'Mapa de calor dos primeiros 10 minutos' : 'Mapa de calor'}</h3>
                <span className="card-aside">posição a cada {heatmaps.secondsPerSample} s, lida do replay</span>
            </div>
            <p className="section-intro">
                {isLaning
                    ? 'Onde você e os heróis da sua rota passaram a fase de rotas: vermelho é onde ficaram mais tempo; verde, onde passaram pouco.'
                    : 'Onde cada herói passou o tempo vivo, como nos mapas de calor do futebol: vermelho é onde ficou mais tempo; verde, onde passou pouco.'}
            </p>

            <div className="heatmap-controls">
                {phases.length > 1 && (
                <div className="segmented" role="group" aria-label="Período">
                    {phases.map(candidate => (
                        <button
                            key={candidate.key}
                            type="button"
                            aria-pressed={candidate.key === phaseKey}
                            onClick={() => setPhaseKey(candidate.key)}
                        >
                            {candidate.label}
                        </button>
                    ))}
                </div>
                )}
                {others.length > 0 && (
                    <label className="heatmap-compare">
                        Comparar com
                        <select value={other.heroId} onChange={event => setOtherHeroId(Number(event.target.value))}>
                            {others.map(hero => (
                                <option key={hero.heroId} value={hero.heroId}>
                                    {hero.heroName} ({player && hero.team === player.team ? 'aliado' : 'inimigo'})
                                </option>
                            ))}
                        </select>
                    </label>
                )}
                {player && deaths && (
                    <label className="heatmap-toggle">
                        <input type="checkbox" checked={showDeaths} onChange={event => setShowDeaths(event.target.checked)} />
                        Mostrar suas mortes
                    </label>
                )}
            </div>

            <div className="heatmap-grid">
                {player && (
                    <HeatmapCard hero={player} label="Você" phase={phase} heatmaps={heatmaps} deaths={phaseDeaths} />
                )}
                {other && (
                    <HeatmapCard
                        hero={other}
                        label={laning?.opponents?.some(hero => hero.heroId === other.heroId) && isLaning
                            ? 'Adversário na sua rota'
                            : other.heroId === smartStats?.counterpartHeroId ? 'Mesma posição, time inimigo' : 'Comparação'}
                        phase={phase}
                        heatmaps={heatmaps}
                    />
                )}
            </div>

            <div className="heatmap-legend" aria-hidden="true">
                <span>Pouco tempo</span>
                <span className="heatmap-legend-bar" />
                <span>Muito tempo</span>
            </div>
            <p className="section-note">
                Cada mapa usa a própria escala: compare o formato, não a intensidade absoluta. Tempo morto não aparece.
            </p>

            <HeatmapIndicators
                player={player}
                other={other}
                phaseKey={phase.indicators}
                phaseLabel={phase.label.toLowerCase()}
                matchAverage={heatmaps.matchAverage}
            />
        </div>
    );
}

export default HeatmapSection;
