import { formatDuration } from '../matches/format';
import ItemIcon from '../items/ItemIcon';
import SectionHead from '../ui/SectionHead';

// The player's item build, read from the replay purchases (the Stitch item timeline): the starting items, then when
// each item was ready, left to right on a line.

function StartingItems({ items }) {
    return (
        <ul className="item-chip-list" aria-label="Itens iniciais">
            {items.map((item, index) => (
                <li className="item-chip" key={`${item.itemId}-${index}`}>
                    <ItemIcon itemId={item.itemId} />
                    {item.name}{item.count > 1 ? <span className="mono"> ×{item.count}</span> : ''}
                </li>
            ))}
        </ul>
    );
}

// itemBuild comes from the API (null until the replay is read, without a point-of-view player or without the item
// catalog: then nothing shows).
function ItemBuildSection({ itemBuild }) {
    if (!itemBuild) {
        return null;
    }

    const { startingItems, items } = itemBuild;
    return (
        <section className="section" aria-labelledby="item-build-title">
            <SectionHead id="item-build-title" title="Itens">
                <span>Quando cada item ficou pronto, segundo o replay</span>
            </SectionHead>
            <div className="card">
                {startingItems.length > 0 && (
                    <div className="item-start">
                        <span className="stat-label">Itens iniciais</span>
                        <StartingItems items={startingItems} />
                    </div>
                )}

                {items.length === 0
                    ? <p className="section-note">Nenhum item grande nesta partida (só consumíveis e peças baratas).</p>
                    : (
                        <div className="item-timeline-scroll">
                            <ol className="item-timeline">
                                {items.map((item, index) => (
                                    <li key={`${item.itemId}-${index}`} className="item-step">
                                        <span className="item-step-time mono">{formatDuration(item.seconds)}</span>
                                        <span className="item-step-icon"><ItemIcon itemId={item.itemId} /></span>
                                        <span className="item-step-name">{item.name}</span>
                                    </li>
                                ))}
                            </ol>
                        </div>
                    )}
                <p className="section-note">
                    Aproximado: item vendido ou dado a um aliado continua contando para quem comprou.
                </p>
            </div>
        </section>
    );
}

export default ItemBuildSection;
