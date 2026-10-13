import { formatNumber } from '../matches/format';
import { HeroIcon } from '../heroes/HeroImage';
import ItemIcon from '../items/ItemIcon';
import { ResultChip, YouChip } from '../ui/Chips';
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
            {neutralItem && (
                <span className="item-slot item-neutral" title={`Neutro: ${neutralItem}`}>
                    {neutralItemId ? <ItemIcon itemId={neutralItemId} /> : <span className="item-image-placeholder" aria-hidden="true" />}
                    <span className="visually-hidden">neutro: {neutralItem}</span>
                </span>
            )}
        </span>
    );
}

// won: true/false for this team, or null when the winner is unknown. side: 'radiant' or 'dire'.
function TeamTable({ name, side, players, won, score }) {
    const netWorth = players.reduce((total, player) => total + player.netWorth, 0);

    return (
        <section className={`card team-card team-${side}`} aria-label={`Time ${name}`}>
            <div className="team-card-head">
                <h3>
                    <span className={`team-dot team-dot-${side}`} aria-hidden="true" />
                    {name}
                    <ResultChip won={won} winLabel="Vencedores" lossLabel="Derrotados" />
                </h3>
                <p className="team-card-totals mono">
                    {formatNumber(score)} abates · {formatNumber(Math.round(netWorth / 100) / 10)} mil de patrimônio
                </p>
            </div>
            <div className="table-scroll">
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
                                        <HeroIcon heroId={player.heroId} />
                                        {player.heroName}
                                        {player.isCurrentPlayer && <YouChip />}
                                    </span>
                                </th>
                                <td className="num-col mono">{player.level}</td>
                                <td className="center-col"><Kda kills={player.kills} deaths={player.deaths} assists={player.assists} /></td>
                                <td className="num-col mono">{formatNumber(player.netWorth)}</td>
                                <td className="num-col mono">{formatNumber(player.lastHits)} / {formatNumber(player.denies)}</td>
                                <td className="num-col mono">{formatNumber(player.goldPerMinute)} / {formatNumber(player.experiencePerMinute)}</td>
                                <td className="num-col mono">{formatNumber(player.heroDamage)}</td>
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
