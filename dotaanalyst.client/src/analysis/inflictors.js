// Readable names for the API's internal ability/item names, until there is an ability catalog.

function capitalize(words) {
    return words.map(word => word.charAt(0).toUpperCase() + word.slice(1)).join(' ');
}

// "Queen of Pain" -> "queenofpain", "Anti-Mage" -> "antimage": how internal names usually spell a hero.
function compact(heroName) {
    return (heroName ?? '').toLowerCase().replace(/[^a-z0-9]/g, '');
}

// item_dagon_5 -> "Dagon 5"; lich_frost_nova (heroName "Lich") -> "Frost Nova"; null -> basic attacks.
// The hero prefix is only removed when it matches heroName (dark_willow_bramble_maze with "Dark Willow"); otherwise
// the whole name shows (skeleton_king_hellfire_blast for Wraith King), which is uglier but never wrong.
export function inflictorName(inflictor, heroName) {
    if (!inflictor) {
        return 'Ataques básicos';
    }

    const words = inflictor.split('_').filter(Boolean);
    if (words[0] === 'item' && words.length > 1) {
        return capitalize(words.slice(1));
    }

    const hero = compact(heroName);
    for (let length = 1; hero && length < words.length; length++) {
        if (words.slice(0, length).join('') === hero) {
            return capitalize(words.slice(length));
        }
    }

    return capitalize(words);
}
