import { useState } from 'react';

// An item's icon by its ID (the API sends the IDs: inventories, build, damage by item). No ID → no icon.
function ItemIcon({ itemId }) {
    const source = Number.isInteger(itemId) && itemId > 0 ? `/item-images/${itemId}.webp` : null;
    const [failedSource, setFailedSource] = useState(null);

    if (!source) {
        return null;
    }
    if (failedSource === source) {
        return <span className="item-image-placeholder" aria-hidden="true" />;
    }

    return (
        <img
            className="item-image"
            src={source}
            alt=""
            aria-hidden="true"
            loading="lazy"
            decoding="async"
            onError={() => setFailedSource(source)}
        />
    );
}

export default ItemIcon;