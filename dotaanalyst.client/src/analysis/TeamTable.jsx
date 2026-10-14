import { formatNumber } from '../matches/format';
import { HeroIcon } from '../heroes/HeroImage';
import ItemIcon from '../items/ItemIcon';

import Kda from '../ui/Kda';

// One team of the match (the Stitch "Tabela completa de jogadores"): a header with the result and the team's totals,
// then one row per player. The table scrolls sideways inside its card on narrow screens.

// The final inventory as icons; the names are in the tooltip and for screen readers.
function Items({ items, itemIds, neutralItem, neutralItemId }) {
    if (items.length === 0 && !neutralItem) {
        return '—';
    }
    return (
        <span className="item-icons">
            {items.map((item, index) => (
                <span key={`${item}-${index}`} className="item-slot" title={item}>
                    {itemIds?.[index] ? <ItemIcon itemId={itemIds[index]} /> : <span className="item-image-placeholder" aria-hidden="true" />}
                    <span className="visually-hidden">{item}{index < items.length - 1 || neutralItem ? ', ' : ''}</span>
                </span>
            ))}
            {/* The empty slots of the six-slot inventory, as in the game. */}
            {Array.from({ length: Math.max(0, INVENTORY_SLOTS - items.length) }, (_, index) => (
                <span key={`empty-${index}`} className="item-slot is-empty" aria-hidden="true" />
            ))}
            {neutralItem && (
                <span className="item-slot item-neutral" title={`Neutro: ${neutralItem}`}>
                    {neutralItemId ? <ItemIcon itemId={neutralItemId} /> : <span className="item-image-placeholder" aria-hidden="true" />}
                    <span className="visually-hidden">neutro: {neutralItem}</span>
                </span>
            )}
        </span>
    );
}

const INVENTORY_SLOTS = 6;

// 11830 -> "11,8k".
function thousands(value) {
    return `${formatNumber(Math.round(value / 100) / 10)}k`;
}

// won: true/false for this team, or null when the winner is unknown. side: 'radiant' or 'dire'.
function TeamTable({ name, side, players, won, score }) {
    const netWorth = players.reduce((total, player) => total + player.netWorth, 0);
    const teamGpm = players.reduce((total, player) => total + player.goldPerMinute, 0);
    const known = won !== null && won !== undefined;

    return (
        <section className={`card team-card team-${side}`} aria-labelledby={`team-${side}-title`}>
            <div className="team-card-head">
                <h2 id={`team-${side}-title`} className="team-card-title">
                    <span className={`team-dot team-dot-${side}`} aria-hidden="true" />
                    <span className={known ? (won ? 'text-win' : 'text-loss') : undefined}>{name}</span>
                    <span className="team-card-meta">
                        • {formatNumber(score)} abates • {known ? (won ? 'Vencedores' : 'Derrotados') : 'resultado desconhecido'}
                    </span>
                </h2>
                <p className="team-card-totals">
                    Patrimônio médio: <strong className="mono">{players.length > 0 ? thousands(netWorth / players.length) : '—'}</strong>
                    <span className="team-card-gap">GPM do time: <strong className="mono">{formatNumber(teamGpm)}</strong></span>
                </p>
            </div>
            <div className="table-scroll" tabIndex={0} role="region" aria-label={`Jogadores do ${name}, tabela com rolagem`}>
                <table className="data-table team-table">
                    <caption className="visually-hidden">Jogadores do time {name}</caption>
                    <thead>
                        <tr>
                            <th scope="col">Herói</th>
                            <th scope="col" className="num-col">Nível</th>
                            <th scope="col" className="center-col">K / D / A</th>
                            <th scope="col" className="num-col">Patrimônio</th>
                            <th scope="col" className="num-col">LH / DN</th>
                            <th scope="col" className="num-col">GPM / XPM</th>
                            <th scope="col" className="num-col">Dano a heróis</th>
                            <th scope="col">Itens finais</th>
                        </tr>
                    </thead>
                    <tbody>
                        {players.map((player, index) => (
                            // accountId is null for private profiles, so the position is part of the key.
                            <tr key={`${index}-${player.heroId}`} className={player.isCurrentPlayer ? 'is-you' : undefined}>
                                <th scope="row">
                                    <span className="hero-cell">
                                        <picture>
                                            {Number.isInteger(player.heroId) && player.heroId > 0 && (
                                                <source media="(min-width: 1025px)" srcSet={`/hero-images/full/${player.heroId}.webp`} />
                                            )}
                                            <HeroIcon heroId={player.heroId} />
                                        </picture>
                                        {player.heroName}
                                        {player.isCurrentPlayer && <span className="tag-you">Você</span>}
                                    </span>
                                </th>
                                <td className="num-col mono">{player.level}</td>
                                <td className="center-col"><Kda kills={player.kills} deaths={player.deaths} assists={player.assists} /></td>
                                <td className={`num-col mono${player.isCurrentPlayer ? ' text-win' : ''}`}>{formatNumber(player.netWorth)}</td>
                                <td className="num-col mono">{formatNumber(player.lastHits)} / {formatNumber(player.denies)}</td>
                                <td className="num-col mono">{formatNumber(player.goldPerMinute)} / {formatNumber(player.experiencePerMinute)}</td>
                                <td className="num-col mono team-strong">{formatNumber(player.heroDamage)}</td>
                                <td>
                                    <Items
                                        items={player.items}
                                        itemIds={player.itemIds}
                                        neutralItem={player.neutralItem}
                                        neutralItemId={player.neutralItemId}
                                    />
                                </td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>
        </section>
    );
}

export default TeamTable;
