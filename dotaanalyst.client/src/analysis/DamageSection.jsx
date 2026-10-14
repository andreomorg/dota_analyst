import { useState } from 'react';
import { formatNumber, toPercent } from '../matches/format';
import { HeroIcon } from '../heroes/HeroImage';
import ItemIcon from '../items/ItemIcon';
import { inflictorName } from './inflictors';
import TabIcon from './TabIcon';

// The player's damage to and from enemy heroes, read from the replay (Stitch reference "Lutas e objetivos"): one card
// with a bar per hero (dealt and taken, same scale) and one with the sources of the damage dealt (abilities and
// items). Illusions and summons count for their owner; only damage on the hero itself counts.

function sum(rows) {
    return rows.reduce((total, row) => total + row.amount, 0);
}

function HeroBars({ title, rows, max, total, tone, emptyText }) {
    return (
        <div className="bar-group">
            {title && <h3 className="bar-group-title">
                {title}
                <span className="mono">Total: {formatNumber(total)}</span>
            </h3>}
            {rows.length === 0
                ? <p className="quiet-note">{emptyText}</p>
                : (
                    <ol className="bar-list">
                        {/* Index keys: the list never reorders, and two heroes can share "Unknown hero". */}
                        {rows.map((row, index) => (
                            <li key={index} className="bar-row">
                                <span className="bar-label">
                                    <HeroIcon heroId={row.heroId} />
                                    <span className="bar-label-text">{row.heroName}</span>
                                    {row.isCurrentPlayer && <span className="tag-you">Você</span>}
                                </span>
                                <span className="bar-value mono">
                                    {formatNumber(row.amount)} <span className="bar-share">({total > 0 ? toPercent(row.amount / total) : 0}%)</span>
                                </span>
                                <span className="bar-track" aria-hidden="true">
                                    <span className={`bar-fill fill-${row.tone ?? tone}`} style={{ width: `${max > 0 ? (row.amount / max) * 100 : 0}%` }} />
                                </span>
                            </li>
                        ))}
                    </ol>
                )}
        </div>
    );
}

// damage comes from the API (null until the replay is read, or without a point-of-view player: then nothing shows).
export function DamageHeroesCard({ damage, players = [] }) {
    const [detailsOpen, setDetailsOpen] = useState(() => !window.matchMedia('(min-width: 1025px)').matches);
    if (!damage) {
        return null;
    }
    // The same scale for dealt and taken, so the two lists can be compared.
    const max = Math.max(0, ...damage.toHeroes.map(row => row.amount), ...damage.fromHeroes.map(row => row.amount));
    const totals = players.filter(player => Number.isFinite(player.heroDamage)).map(player => ({
        ...player,
        amount: player.heroDamage,
        tone: player.isCurrentPlayer ? 'you' : player.team === 'radiant' ? 'win' : 'loss',
    })).sort((a, b) => b.amount - a.amount);
    const details = <>
        <HeroBars title="Causado em cada herói" rows={damage.toHeroes} total={sum(damage.toHeroes)} max={max} tone="win" emptyText="Você não causou dano em heróis inimigos." />
        <HeroBars title="Recebido de cada herói" rows={damage.fromHeroes} total={sum(damage.fromHeroes)} max={max} tone="loss" emptyText="Você não recebeu dano de heróis inimigos." />
        <p className="section-note">Ilusões e invocações contam para o dono; só conta o dano no herói em si.</p>
    </>;
    return (
        <section className="card damage-card" aria-labelledby="damage-title">
            <div className="card-split-head">
                <h2 id="damage-title" className="card-heading">
                    <TabIcon tab="chart" className="section-icon" />
                    Dano Causado a Heróis
                </h2>
                <span className="section-aside">{totals.length ? `Total: ${formatNumber(sum(totals))}` : 'a partida inteira'}</span>
            </div>
            {totals.length > 0 ? <>
                <div className="damage-summary">
                    <HeroBars rows={totals.slice(0, 5)} total={sum(totals)} max={totals[0].amount} tone="win" emptyText="Sem dano registrado." />
                    <p className="section-note">Cinco maiores danos do placar; os dez heróis estão em Jogadores.</p>
                </div>
                <details className="damage-details" open={detailsOpen} onToggle={event => setDetailsOpen(event.currentTarget.open)}>
                    <summary>Dano causado e recebido por herói</summary>
                    {details}
                </details>
            </> : details}
        </section>
    );
}

// Every source of the damage dealt, biggest first, with its share of the total.
export function DamageSourcesCard({ damage, heroName }) {
    if (!damage || damage.byInflictor.length === 0) {
        return null;
    }
    const total = sum(damage.byInflictor);
    return (
        <section className="card damage-card" aria-labelledby="damage-sources-title">
            <div className="card-split-head">
                <h2 id="damage-sources-title" className="card-heading">
                    <TabIcon tab="trend" className="section-icon" />
                    Fontes de dano{heroName ? `: ${heroName}` : ''}
                </h2>
                <span className="section-aside mono">Total: {formatNumber(total)}</span>
            </div>
            <div className="table-scroll" tabIndex={0} role="region" aria-label="Fontes de dano, tabela com rolagem">
                <table className="data-table sources-table">
                    <caption className="visually-hidden">Dano causado em heróis por habilidade e item</caption>
                    <thead>
                        <tr>
                            <th scope="col">Habilidade / item</th>
                            <th scope="col" className="source-missing-field">Tipo</th>
                            <th scope="col" className="num-col">Dano causado</th>
                            <th scope="col" className="num-col source-missing-field">Dano recebido</th>
                            <th scope="col" className="num-col">% do total</th>
                        </tr>
                    </thead>
                    <tbody>
                        {damage.byInflictor.map((row, index) => (
                            <tr key={index}>
                                <th scope="row">
                                    <span className="hero-cell">
                                        {row.itemId ? <ItemIcon itemId={row.itemId} /> : <span className="source-dot" aria-hidden="true" />}
                                        {row.itemName ?? inflictorName(row.inflictor, heroName)}
                                    </span>
                                </th>
                                <td className="source-missing-field" aria-label="Tipo de dano não informado">—</td>
                                <td className="num-col mono text-win">{formatNumber(row.amount)}</td>
                                <td className="num-col mono source-missing-field" aria-label="Dano recebido por fonte não informado">—</td>
                                <td className="num-col mono">{total > 0 ? `${toPercent(row.amount / total)}%` : '—'}</td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>
        </section>
    );
}
