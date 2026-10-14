import { useId, useState } from 'react';
import { formatDuration, toPercent } from '../matches/format';
import { svgY, VIEW_BOX } from './mapGeometry';
import GameMap from './GameMap';
import TabIcon from './TabIcon';

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

// The game's own ward icons (item images: 42 Observer, 43 Sentry).
const ICONS = { observer: '/item-images/42.webp', sentry: '/item-images/43.webp' };

// The ward's icon in a circle, with a ring in its team's colour; a destroyed ward is faded and crossed out.
function WardMark({ ward, clipId }) {
    const size = 340;
    return (
        <>
            <circle className="ward-ring" r={size + 60} />
            <image
                className={`ward-icon${ward.destroyed ? ' is-destroyed' : ''}`}
                href={ICONS[ward.kind] ?? ICONS.observer}
                x={-size}
                y={-size}
                width={2 * size}
                height={2 * size}
                preserveAspectRatio="xMidYMid slice"
                clipPath={`url(#${clipId})`}
            />
            {ward.destroyed && (
                <path className="ward-cross" d={`M${-size * 0.55},${-size * 0.55}L${size * 0.55},${size * 0.55}M${size * 0.55},${-size * 0.55}L${-size * 0.55},${size * 0.55}`} />
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

// uptime: the share of the match with an observer of the player's team (smartStats; null without it).
// wards comes from the API (null until the replay is read; absent on older responses: then nothing shows).
function WardMap({ wards, uptime }) {
    const hasPlayer = wards?.some(ward => ward.isPlayersTeam !== null) ?? false;
    const [team, setTeam] = useState('all');
    const [period, setPeriod] = useState('all');
    const [selected, setSelected] = useState(null);
    // Letters and digits only: React's ids have characters a url(#…) reference may not take.
    const clipId = `ward-clip-${useId().replace(/[^a-zA-Z0-9]/g, '')}`;
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

    const count = (kind, mine) => wards.filter(ward => ward.kind === kind && isMine(ward) === mine).length;
    return (
        <section className="card map-card" aria-labelledby="ward-map-title">
            <div className="card-split-head">
                <h2 id="ward-map-title" className="card-heading">
                    <TabIcon tab="eye" className="section-icon icon-amber" />
                    Mapa de visão e wards
                </h2>
                <span className="tag-outline mono">
                    {uptime !== null && uptime !== undefined ? `${toPercent(uptime)}% com observer` : `${wards.length} wards`}
                </span>
            </div>
            <div className="map-filters">
                <Filter label="Time" options={teamLabels} value={team} onChange={setTeam} />
                <Filter label="Período" options={PERIODS} value={period} onChange={setPeriod} />
            </div>
            <div className="map-frame">
                <svg viewBox={VIEW_BOX} role="img" aria-label={`Mapa com ${shown.length} wards; os detalhes estão na tabela abaixo.`}>
                    <defs>
                        <clipPath id={clipId} clipPathUnits="objectBoundingBox">
                            <circle cx="0.5" cy="0.5" r="0.5" />
                        </clipPath>
                    </defs>
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
                            <WardMark ward={ward} clipId={clipId} />
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
            <ul className="chart-legend map-legend ward-legend">
                <li><span className="legend-ring team-mine" aria-hidden="true" /> {teamLabels.mine}</li>
                <li><span className="legend-ring team-theirs" aria-hidden="true" /> {teamLabels.theirs}</li>
                <li><img className="legend-icon" src={ICONS.observer} alt="" aria-hidden="true" /> Observers ({count('observer', true)} × {count('observer', false)})</li>
                <li><img className="legend-icon" src={ICONS.sentry} alt="" aria-hidden="true" /> Sentries ({count('sentry', true)} × {count('sentry', false)})</li>
                <li><span className="legend-shape destroyed" aria-hidden="true">×</span> Destruído</li>
            </ul>
            <p className="section-note">
                Mostrando {shown.length} de {wards.length}: {wards.filter(isMine).length} do {hasPlayer ? 'seu time' : 'Radiant'} e {wards.filter(ward => !isMine(ward)).length} do
                {hasPlayer ? ' adversário' : ' Dire'} (entre parênteses: {teamLabels.mine.toLowerCase()} × {teamLabels.theirs.toLowerCase()}).
            </p>
            <details className="inline-details">
                <summary>Ver os {shown.length} wards em tabela</summary>
                <div className="table-scroll" tabIndex={0} role="region" aria-label="Wards na partida, tabela com rolagem">
                    <table className="data-table">
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
