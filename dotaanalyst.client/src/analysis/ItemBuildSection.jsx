import { formatDuration } from '../matches/format';
import ItemIcon from '../items/ItemIcon';
import TabIcon from './TabIcon';

// The player's item build, read from the replay purchases (Stitch reference "Build"): the starting items, then one
// card per item in the order it was ready. The first item of 2.000+ gold (smartStats) stands out.

// itemBuild comes from the API (null until the replay is read, without a point-of-view player or without the item
// catalog: then nothing shows). firstBigItemSeconds: smartStats' first item of 2.000+ gold (null without it).
// neutral: the player's neutral item at the end ({ id, name }, from the scoreboard), or null.
function ItemBuildSection({ itemBuild, firstBigItemSeconds, neutral }) {
    if (!itemBuild) {
        return null;
    }

    const { startingItems, items } = itemBuild;
    const bigIndex = firstBigItemSeconds === null || firstBigItemSeconds === undefined
        ? -1
        : items.findIndex(item => item.seconds === firstBigItemSeconds);
    return (
        <section className="card item-build-card" aria-labelledby="item-build-title">
            <div className="card-split-head">
                <div>
                    <h2 id="item-build-title" className="card-heading">
                        <TabIcon tab="build" className="section-icon" />
                        Itens iniciais e ordem de compra
                    </h2>
                    <p className="card-subtitle">Quando cada item ficou pronto, segundo as compras do replay</p>
                </div>
                {bigIndex >= 0 && (
                    <span className="tag-outline mono">
                        Primeiro item grande aos {formatDuration(firstBigItemSeconds)}
                    </span>
                )}
            </div>

            <div className="item-track-scroll" tabIndex={0} role="region" aria-label="Compras ao longo da partida, lista com rolagem">
                <ol className="item-track">
                    {startingItems.length > 0 && (
                        <li className="item-card is-start">
                            <p className="item-card-head">
                                <span className="mono text-win">0:00</span>
                                <span>Iniciais</span>
                            </p>
                            <ul className="item-start-grid" aria-label="Itens iniciais">
                                {startingItems.map((item, index) => (
                                    <li key={`${item.itemId}-${index}`} title={item.name}>
                                        <ItemIcon itemId={item.itemId} />
                                        {item.count > 1 && <span className="item-count mono">×{item.count}</span>}
                                        <span className="visually-hidden">{item.name}{item.count > 1 ? ` (${item.count})` : ''}</span>
                                    </li>
                                ))}
                            </ul>
                            <p className="item-card-name">Itens iniciais</p>
                            <p className="item-card-price mono" title="Preço não informado" aria-label="Preço não informado">— ouro</p>
                        </li>
                    )}
                    {items.map((item, index) => (
                        <li key={`${item.itemId}-${index}`} className={`item-card${index === bigIndex ? ' is-highlight' : ''}`}>
                            {index === bigIndex && <span className="item-card-flag mono">1º item grande</span>}
                            <p className="item-card-head">
                                <span className="mono">{formatDuration(item.seconds)}</span>
                                <span>{item.assembled ? 'Montado' : 'Comprado'}</span>
                            </p>
                            <span className="item-card-art"><ItemIcon itemId={item.itemId} /></span>
                            <p className="item-card-name">{item.name}</p>
                            <p className="item-card-price mono" title="Preço não informado" aria-label="Preço não informado">— ouro</p>
                        </li>
                    ))}
                </ol>
            </div>
            {items.length === 0 && <p className="quiet-note">Nenhum item grande nesta partida (só consumíveis e peças baratas).</p>}

            {neutral?.name && (
                <div className="neutral-box">
                    <span className="neutral-title">
                        <TabIcon tab="star" className="section-icon" />
                        Item neutro no fim da partida:
                    </span>
                    <span className="neutral-item">
                        <ItemIcon itemId={neutral.id} />
                        {neutral.name}
                    </span>
                </div>
            )}
            <p className="section-note">
                Aproximado: item vendido ou dado a um aliado continua contando para quem comprou; receitas e consumíveis ficam de fora.
            </p>
        </section>
    );
}

export default ItemBuildSection;
