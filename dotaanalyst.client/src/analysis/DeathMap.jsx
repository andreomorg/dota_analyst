import { useState } from 'react';
import { formatDuration } from '../matches/format';

import { svgY, VIEW_BOX } from './mapGeometry';
import GameMap from './GameMap';
import TabIcon from './TabIcon';

// Where the player died, on the game's minimap (GameMap.jsx).

const KILLERS = {
    tower: 'Torre',
    creeps: 'Creeps',
    neutrals: 'Neutros',
    roshan: 'Roshan',
    other: 'Outro',
};

function killerName(death) {
    if (death.killer === 'hero') {
        return death.killerHeroName ?? 'Herói';
    }
    return KILLERS[death.killer] ?? death.killer;
}

// deaths comes from the API (null until the replay is read; absent on older responses: then nothing shows).
function DeathMap({ deaths }) {
    const [selected, setSelected] = useState(null);
    if (!deaths) {
        return null;
    }

    const laning = deaths.filter(death => death.inLaningPhase).length;
    const mid = deaths.filter(death => death.seconds >= 600 && death.seconds < 1200).length;
    const late = deaths.filter(death => death.seconds >= 1200).length;
    const byKiller = Object.entries(deaths.reduce((counts, death) => {
        const name = death.killer === 'hero' ? 'heróis' : (KILLERS[death.killer] ?? death.killer).toLowerCase();
        return { ...counts, [name]: (counts[name] ?? 0) + 1 };
    }, {})).sort((a, b) => b[1] - a[1]);
    return (
        <section className="card map-card" aria-labelledby="death-map-title">
            <div className="card-split-head">
                <h2 id="death-map-title" className="card-heading">
                    <TabIcon tab="skull" className="section-icon icon-loss" />
                    Mapa de mortes
                </h2>
                <span className="tag-outline tag-loss mono">{deaths.length} {deaths.length === 1 ? 'morte' : 'mortes'}</span>
            </div>
            {deaths.length === 0
                ? (
                    <>
                        <div className="map-frame">
                            <svg viewBox={VIEW_BOX} role="img" aria-label="Mapa de mortes: nenhuma morte nesta partida.">
                                <GameMap />
                            </svg>
                        </div>
                        <p className="quiet-note">Você não morreu nesta partida. Nenhuma morte para marcar no mapa.</p>
                    </>
                )
                : (
                    <>
                        <ul className="chart-legend map-legend">
                            <li><span className="legend-ring death-laning" aria-hidden="true" /> Até 10:00</li>
                            <li><span className="legend-ring death-later" aria-hidden="true" /> Depois dos 10:00</li>
                            <li><span className="legend-ring death-aegis" aria-hidden="true" /> Voltou com Aegis</li>
                        </ul>
                        <div className="map-frame">
                            <svg viewBox={VIEW_BOX} role="img" aria-label={`Mapa com ${deaths.length} mortes; os detalhes estão na tabela abaixo.`}>
                                <GameMap />
                                {deaths.map((death, index) => (
                                    <g
                                        key={`${death.seconds}-${index}`}
                                        className={`death-marker ${death.reincarnated ? 'death-aegis' : death.inLaningPhase ? 'death-laning' : 'death-later'}${selected === index ? ' is-selected' : ''}`}
                                        transform={`translate(${death.x},${svgY(death.y)})`}
                                        onPointerEnter={() => setSelected(index)}
                                        onPointerLeave={() => setSelected(null)}
                                        onClick={() => setSelected(selected === index ? null : index)}
                                    >
                                        {/* A bigger, invisible hit area than the mark. */}
                                        <circle className="death-hit" r="700" />
                                        <circle className="death-dot" r="430" />
                                        <text className="death-number" textAnchor="middle" dy="170">{index + 1}</text>
                                    </g>
                                ))}
                            </svg>
                            {selected !== null && deaths[selected] && (
                                <div className="chart-tooltip death-tooltip" role="status">
                                    <strong>{selected + 1}. {formatDuration(deaths[selected].seconds)}</strong>
                                    <span>Morto por: {killerName(deaths[selected])}</span>
                                    {deaths[selected].reincarnated && <span>Voltou com Aegis (não conta no placar)</span>}
                                </div>
                            )}
                        </div>
                        <div className="summary-box">
                            <p className="summary-box-head">
                                <span>Mortes por fase:</span>
                                <span className="mono">0–10 min: <strong>{laning}</strong> · 10–20: <strong>{mid}</strong> · 20+: <strong>{late}</strong></span>
                            </p>
                            <p>Quem matou: {byKiller.map(([name, count]) => `${name} (${count})`).join(', ')}.</p>
                        </div>
                        <details className="inline-details">
                            <summary>Ver as mortes em tabela</summary>
                            <div className="table-scroll" tabIndex={0} role="region" aria-label="Mortes na partida, tabela com rolagem">
                                <table className="data-table">
                                    <caption className="visually-hidden">Suas mortes, em ordem</caption>
                                    <thead>
                                        <tr>
                                            <th scope="col">#</th>
                                            <th scope="col">Tempo</th>
                                            <th scope="col">Morto por</th>
                                            <th scope="col">Fase</th>
                                        </tr>
                                    </thead>
                                    <tbody>
                                        {deaths.map((death, index) => (
                                            <tr key={`${death.seconds}-${index}`} className={selected === index ? 'is-you' : undefined}>
                                                <th scope="row" className="mono">{index + 1}</th>
                                                <td className="mono">{formatDuration(death.seconds)}</td>
                                                <td>{killerName(death)}{death.reincarnated ? ' (voltou com Aegis)' : ''}</td>
                                                <td>{death.inLaningPhase ? 'Rotas' : 'Depois'}</td>
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

export default DeathMap;
