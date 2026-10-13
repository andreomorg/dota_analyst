import { formatNumber } from '../matches/format';
import { HeroIcon } from '../heroes/HeroImage';
import ItemIcon from '../items/ItemIcon';

function teamStatus(won) {
    if (won === null || won === undefined) {
        return null;
    }
    return won
        ? { label: 'Vencedor', className: 'result-win' }
        : { label: 'Perdedor', className: 'result-loss' };
}

function ItemsCell({ items, itemIds, neutralItem, neutralItemId }) {
    if (items.length === 0 && !neutralItem) {
        return '—';
    }
    return (
        <span className="item-chip-list">
            {items.map((item, index) => (
                <span key={`${item}-${index}`} className="item-chip">
                    <ItemIcon itemId={itemIds?.[index]} />
                    {item}
                </span>
            ))}
            {neutralItem && (
                <span className="item-chip neutral-item">
                    <ItemIcon itemId={neutralItemId} />
                    Neutro: {neutralItem}
                </span>
            )}
        </span>
    );
}

function PlayerCard({ player }) {
    return (
        <article className={`team-player-card${player.isCurrentPlayer ? ' current-player' : ''}`}>
            <div className="team-player-heading">
                <h3>
                    <HeroIcon heroId={player.heroId} />
                    {player.heroName}
                    {player.isCurrentPlayer && <> <span className="you-badge">Você</span></>}
                </h3>
                <span className="hero-level">Nível {player.level}</span>
            </div>
            <dl className="team-player-stats">
                <div><dt>K / D / A</dt><dd>{player.kills} / {player.deaths} / {player.assists}</dd></div>
                <div><dt>LH / DN</dt><dd>{formatNumber(player.lastHits)} / {formatNumber(player.denies)}</dd></div>
                <div><dt>GPM / XPM</dt><dd>{formatNumber(player.goldPerMinute)} / {formatNumber(player.experiencePerMinute)}</dd></div>
                <div><dt>Patrimônio</dt><dd>{formatNumber(player.netWorth)}</dd></div>
                <div><dt>Dano a heróis</dt><dd>{formatNumber(player.heroDamage)}</dd></div>
                <div className="team-player-items">
                    <dt>Itens</dt>
                    <dd>
                        <ItemsCell
                            items={player.items}
                            itemIds={player.itemIds}
                            neutralItem={player.neutralItem}
                            neutralItemId={player.neutralItemId}
                        />
                    </dd>
                </div>
            </dl>
        </article>
    );
}

// won: true/false for this team, or null when the winner is unknown.
function TeamTable({ name, players, won }) {
    const status = teamStatus(won);

    return (
        <section className={`team ${won ? 'team-won' : ''}`}>
            <h2>
                {name}
                {status && <span className={`team-status ${status.className}`}> · {status.label}</span>}
            </h2>
            <div className="table-scroll team-table-scroll">
                <table className="team-table">
                    <caption className="visually-hidden">
                        Jogadores do time {name}{status ? ` (${status.label.toLowerCase()})` : ''}
                    </caption>
                    <thead>
                        <tr>
                            <th scope="col">Herói</th>
                            <th scope="col">K / D / A</th>
                            <th scope="col">LH / DN</th>
                            <th scope="col">GPM / XPM</th>
                            <th scope="col">Patrimônio</th>
                            <th scope="col">Dano a heróis</th>
                            <th scope="col">Itens</th>
                        </tr>
                    </thead>
                    <tbody>
                        {players.map((player, index) => (
                            // accountId is null for private profiles, so the position is part of the key.
                            <tr key={`${index}-${player.heroId}`} className={player.isCurrentPlayer ? 'current-player' : undefined}>
                                <th scope="row">
                                    <span className="hero-cell">
                                        <HeroIcon heroId={player.heroId} />
                                        {player.heroName}
                                    </span>
                                    {player.isCurrentPlayer && <>{' '}<span className="you-badge">Você</span></>}
                                    <span className="hero-level"> · nível {player.level}</span>
                                </th>
                                <td>{player.kills} / {player.deaths} / {player.assists}</td>
                                <td>{formatNumber(player.lastHits)} / {formatNumber(player.denies)}</td>
                                <td>{formatNumber(player.goldPerMinute)} / {formatNumber(player.experiencePerMinute)}</td>
                                <td>{formatNumber(player.netWorth)}</td>
                                <td>{formatNumber(player.heroDamage)}</td>
                                <td className="items-cell">
                                    <ItemsCell
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
            <div className="team-player-cards" aria-label={`Jogadores do time ${name}`}>
                {players.map((player, index) => (
                    <PlayerCard key={`${index}-${player.heroId}`} player={player} />
                ))}
            </div>
        </section>
    );
}

export default TeamTable;
