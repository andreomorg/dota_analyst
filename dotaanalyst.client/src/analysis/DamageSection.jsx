import { formatNumber } from '../matches/format';
import { HeroIcon } from '../heroes/HeroImage';
import { inflictorName } from './inflictors';
import ItemIcon from '../items/ItemIcon';

// The player's damage to and from enemy heroes, read from the replay: one measure per list, horizontal bars with
// the value written next to them (the bar only helps to compare). Green = dealt, red = taken.

const TOP_INFLICTORS = 8;

function sum(rows) {
    return rows.reduce((total, row) => total + row.amount, 0);
}

function BarList({ title, total, rows, max, labelOf, iconOf, emptyText, tone }) {
    return (
        <div className="bar-group">
            <h4 className="bar-group-title">
                {title}
                {total !== undefined && <span className="mono">{formatNumber(total)}</span>}
            </h4>
            {rows.length === 0
                ? <p className="section-note">{emptyText}</p>
                : (
                    <ol className="bar-list">
                        {/* Index keys: the list never reorders, and two heroes can share "Unknown hero". */}
                        {rows.map((row, index) => (
                            <li key={index} className="bar-row">
                                <span className="bar-label">
                                    {iconOf?.(row)}
                                    <span className="bar-label-text">{labelOf(row)}</span>
                                </span>
                                <span className="bar-value mono">{formatNumber(row.amount)}</span>
                                <span className="bar-track" aria-hidden="true">
                                    <span className={`bar-fill fill-${tone}`} style={{ width: `${max > 0 ? (row.amount / max) * 100 : 0}%` }} />
                                </span>
                            </li>
                        ))}
                    </ol>
                )}
        </div>
    );
}

// The biggest inflictors, then the rest added up.
function topInflictors(rows, heroName) {
    // Items come with their ID and catalog name from the API; abilities get a readable name here.
    const named = rows.map(row => ({
        label: row.itemName ?? inflictorName(row.inflictor, heroName),
        amount: row.amount,
        itemId: row.itemId ?? null,
    }));
    if (named.length <= TOP_INFLICTORS) {
        return named;
    }

    const rest = named.slice(TOP_INFLICTORS - 1);
    return [...named.slice(0, TOP_INFLICTORS - 1), { label: `Outras (${rest.length})`, amount: sum(rest) }];
}

// damage comes from the API (null until the replay is read, or without a point-of-view player: then nothing shows).
function DamageSection({ damage, heroName }) {
    if (!damage) {
        return null;
    }

    // The same scale for dealt and taken, so the two lists can be compared.
    const heroMax = Math.max(0, ...damage.toHeroes.map(row => row.amount), ...damage.fromHeroes.map(row => row.amount));
    const inflictors = topInflictors(damage.byInflictor, heroName);

    return (
        <div className="card damage-card">
            <div className="card-head">
                <h3 id="damage-title">Dano em heróis</h3>
                <span className="card-aside">a partida inteira; ilusões e invocações contam para o dono</span>
            </div>
            <div className="bar-groups">
                <BarList
                    title="Causado em cada herói"
                    total={sum(damage.toHeroes)}
                    rows={damage.toHeroes}
                    max={heroMax}
                    labelOf={row => row.heroName}
                    iconOf={row => <HeroIcon heroId={row.heroId} />}
                    emptyText="Você não causou dano em heróis inimigos."
                    tone="win"
                />
                <BarList
                    title="Recebido de cada herói"
                    total={sum(damage.fromHeroes)}
                    rows={damage.fromHeroes}
                    max={heroMax}
                    labelOf={row => row.heroName}
                    iconOf={row => <HeroIcon heroId={row.heroId} />}
                    emptyText="Você não recebeu dano de heróis inimigos."
                    tone="loss"
                />
            </div>
            {inflictors.length > 0 && (
                <BarList
                    title="Causado por habilidade e item"
                    rows={inflictors}
                    max={inflictors.reduce((max, row) => Math.max(max, row.amount), 0)}
                    labelOf={row => row.label}
                    iconOf={row => row.itemId && <ItemIcon itemId={row.itemId} />}
                    tone="neutral"
                />
            )}
        </div>
    );
}

export default DamageSection;
