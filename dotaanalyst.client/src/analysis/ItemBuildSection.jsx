import { formatDuration } from '../matches/format';

// The player's item build, read from the replay purchases: the starting items, then when each item was ready.

function startingText(items) {
    return items.map(item => (item.count > 1 ? `${item.name} ×${item.count}` : item.name)).join(', ');
}

// itemBuild comes from the API (null until the replay is read, without a point-of-view player or without the item
// catalog: then nothing shows).
function ItemBuildSection({ itemBuild }) {
    if (!itemBuild) {
        return null;
    }

    const { startingItems, items } = itemBuild;
    return (
        <section className="item-build" aria-labelledby="item-build-title">
            <h2 id="item-build-title">Build de itens</h2>
            <p className="laning-intro">
                Quando cada item ficou pronto, segundo as compras do replay. É aproximado: item vendido ou dado a um
                aliado continua contando para quem comprou.
            </p>

            {startingItems.length > 0 && (
                <p className="item-build-start">
                    <strong>Itens iniciais:</strong> {startingText(startingItems)}
                </p>
            )}

            {items.length === 0
                ? <p className="laning-note">Nenhum item grande nesta partida (só consumíveis e peças baratas).</p>
                : (
                    <ol className="item-build-list">
                        {items.map((item, index) => (
                            <li key={`${item.itemId}-${index}`}>
                                <span className="item-build-time">{formatDuration(item.seconds)}</span>
                                <span className="item-build-name">{item.name}</span>
                            </li>
                        ))}
                    </ol>
                )}
        </section>
    );
}

export default ItemBuildSection;
