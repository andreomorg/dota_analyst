import { useState } from 'react';
import { formatDuration } from '../matches/format';
import { svgY, VIEW_BOX } from './mapGeometry';
import GameMap from './GameMap';

// Both teams' wards on the game's minimap (GameMap.jsx). Shape = kind (circle observer, diamond sentry), colour = team (blue the
// player's team, red the other; the validated diverging pair — so colour is never the only cue), a cross =
// destroyed. Filters in one row above the map; a table with every ward shown.

const TEAMS = { mine: 'Seu time', theirs: 'Adversário', all: 'Ambos' };
const PERIODS = { laning: 'Até 10:00', later: 'Depois', all: 'Tudo' };
const KINDS = { observer: 'Observer', sentry: 'Sentry' };

function lifetime(ward) {
    return ward.removedAt === null ? null : ward.removedAt - ward.placedAt;
}

function Filter({ label, options, value, onChange }) {
    return (
        <div className="segmented" role="group" aria-label={label}>
            {Object.entries(options).map(([key, text]) => (
                <button key={key} type="button" aria-pressed={value === key} onClick={() => onChange(key)}>{text}</button>
            ))}
        </div>
    );
}

function WardMark({ ward }) {
    const size = 330;
    return (
        <>
            {ward.kind === 'sentry'
                ? <rect className="ward-shape" x={-size} y={-size} width={2 * size} height={2 * size} transform="rotate(45)" />
                : <circle className="ward-shape" r={size * 1.1} />}
            {ward.destroyed && (
                <path className="ward-cross" d={`M${-size * 0.6},${-size * 0.6}L${size * 0.6},${size * 0.6}M${size * 0.6},${-size * 0.6}L${-size * 0.6},${size * 0.6}`} />
            )}
        </>
    );
}

function describe(ward) {
    const life = lifetime(ward);
    return ward.destroyed
        ? `destruído${ward.destroyedByHeroName ? ` por ${ward.destroyedByHeroName}` : ''} após ${formatDuration(life)}`
        : life === null ? 'até o fim da partida' : `expirou após ${formatDuration(life)}`;
}

// wards comes from the API (null until the replay is read; absent on older responses: then nothing shows).
function WardMap({ wards }) {
    const hasPlayer = wards?.some(ward => ward.isPlayersTeam !== null) ?? false;
    const [team, setTeam] = useState('all');
    const [period, setPeriod] = useState('all');
    const [selected, setSelected] = useState(null);
    if (!wards || wards.length === 0) {
        return null;
    }

    // Without a point-of-view player, "mine" is Radiant.
    const isMine = ward => (hasPlayer ? ward.isPlayersTeam : ward.team === 'radiant');
    const shown = wards
        .map((ward, index) => ({ ward, index }))
        .filter(({ ward }) => team === 'all' || (team === 'mine') === isMine(ward))
        .filter(({ ward }) => period === 'all' || (period === 'laning') === ward.inLaningPhase);
    const current = shown.find(({ index }) => index === selected)?.ward;
    const teamLabels = hasPlayer ? TEAMS : { mine: 'Radiant', theirs: 'Dire', all: 'Ambos' };

    return (
        <section className="death-map ward-map" aria-labelledby="ward-map-title">
            <h2 id="ward-map-title">Wards</h2>
            <p className="laning-intro">
                {wards.filter(isMine).length} do {hasPlayer ? 'seu time' : 'Radiant'} e {wards.filter(ward => !isMine(ward)).length} do
                {hasPlayer ? ' adversário' : ' Dire'}. Mostrando {shown.length}.
            </p>
            <div className="map-filters">
                <Filter label="Time" options={teamLabels} value={team} onChange={setTeam} />
                <Filter label="Período" options={PERIODS} value={period} onChange={setPeriod} />
            </div>
            <ul className="chart-legend death-legend">
                <li><span className="legend-swatch team-mine" aria-hidden="true" /> {teamLabels.mine}</li>
                <li><span className="legend-swatch team-theirs" aria-hidden="true" /> {teamLabels.theirs}</li>
                <li><span className="legend-shape observer" aria-hidden="true" /> Observer</li>
                <li><span className="legend-shape sentry" aria-hidden="true" /> Sentry</li>
                <li><span className="legend-shape destroyed" aria-hidden="true">×</span> Destruído</li>
            </ul>
            <div className="death-map-frame">
                <svg viewBox={VIEW_BOX} role="img" aria-label={`Mapa com ${shown.length} wards; os detalhes estão na tabela abaixo.`}>
                    <GameMap />
                    {shown.map(({ ward, index }) => (
                        <g
                            key={index}
                            className={`ward-marker ${isMine(ward) ? 'team-mine' : 'team-theirs'}${selected === index ? ' is-selected' : ''}`}
                            transform={`translate(${ward.x},${svgY(ward.y)})`}
                            onPointerEnter={() => setSelected(index)}
                            onPointerLeave={() => setSelected(null)}
                            onClick={() => setSelected(selected === index ? null : index)}
                        >
                            <circle className="death-hit" r="600" />
                            <WardMark ward={ward} />
                        </g>
                    ))}
                </svg>
                {current && (
                    <div className="chart-tooltip death-tooltip" role="status">
                        <strong>{KINDS[current.kind]} · {formatDuration(current.placedAt)}</strong>
                        <span>{current.placedByHeroName ?? 'Herói desconhecido'} ({isMine(current) ? teamLabels.mine : teamLabels.theirs})</span>
                        <span>{describe(current)}</span>
                    </div>
                )}
            </div>
            <details className="timeline-table">
                <summary>Ver os {shown.length} wards em tabela</summary>
                <div className="table-scroll">
                    <table className="death-table">
                        <thead>
                            <tr>
                                <th scope="col">Plantado</th>
                                <th scope="col">Tipo</th>
                                <th scope="col">Quem plantou</th>
                                <th scope="col">Resultado</th>
                            </tr>
                        </thead>
                        <tbody>
                            {shown.map(({ ward, index }) => (
                                <tr key={index}>
                                    <th scope="row">{formatDuration(ward.placedAt)}</th>
                                    <td>{KINDS[ward.kind]}</td>
                                    <td>{ward.placedByHeroName ?? '—'} ({isMine(ward) ? teamLabels.mine : teamLabels.theirs})</td>
                                    <td>{describe(ward)}</td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            </details>
        </section>
    );
}

export default WardMap;
